using EnvDTE;
using EnvDTE80;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualBasic;
using System;
using System.IO;
using System.IO.Packaging;
using System.Runtime.InteropServices;
using System.Threading;
using Package = Microsoft.VisualStudio.Shell.Package;
using Task = System.Threading.Tasks.Task;

namespace BlitzVisualStudio
{
	/// <summary>
	/// This is the class that implements the package exposed by this assembly.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The minimum requirement for a class to be considered a valid package for Visual Studio
	/// is to implement the IVsPackage interface and register itself with the shell.
	/// This package uses the helper classes defined inside the Managed Package Framework (MPF)
	/// to do it: it derives from the Package class that provides the implementation of the
	/// IVsPackage interface and uses the registration attributes defined in the framework to
	/// register itself and its components with the shell. These attributes tell the pkgdef creation
	/// utility what data to put into .pkgdef file.
	/// </para>
	/// <para>
	/// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
	/// </para>
	/// </remarks>
	[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
	[Guid(BlitzVisualStudioPackage.PackageGuidString)]
	[ProvideMenuResource("Menus.ctmenu", 1)]
	[ProvideAutoLoad(UIContextGuids80.CodeWindow, PackageAutoLoadFlags.BackgroundLoad)]
	[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
	public sealed class BlitzVisualStudioPackage : AsyncPackage
	{
		/// <summary>
		/// Command menu group (command set GUID).
		/// </summary>
		public static readonly Guid CommandSet = new Guid("b838b65e-b0f4-4106-aa88-e936bf0b2e0c");

		/// <summary>
		/// BlitzVisualStudioPackage GUID string.
		/// </summary>
		public const string PackageGuidString = "310f1a03-8e3d-40ed-8fd2-92ba9aa31ec2";

		#region Package Members

		/// <summary>
		/// Initialization of the package; this method is called right after the package is sited, so this is the place
		/// where you can put all the initialization code that rely on services provided by VisualStudio.
		/// </summary>
		/// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
		/// <param name="progress">A provider for progress updates.</param>
		/// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
		protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
		{
			// When initialized asynchronously, the current thread may be a background thread at this point.
			// Do any initialization that requires the UI thread after switching to the UI thread.
			await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
			await BlitzSearchThis.InitializeAsync(this);
			await BlitzReplaceThis.InitializeAsync(this);
			PoorMansIPC.Instance.RegisterAction("VISUAL_STUDIO_GOTO", Goto);
			PoorMansIPC.Instance.RegisterAction("VISUAL_STUDIO_GOTO_PREVIEW", GotoPreview);
		}

		/// <summary>
		/// Sets Blitz Search Executable to Search for the active selection or automatic word under the caret.
		/// </summary>
		/// <param name="title">Identity for Error Context</param>
		/// <param name="commandName">Command string, this is the file that is Used for Poormans IPC ( SET_SEARCH, SET_REPLACE )</param>
		public void SendSearchContext(string title, string commandName)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			var dte = GetGlobalService(typeof(DTE)) as DTE2;

			if (dte.ActiveDocument == null)
			{
				return;
			}
			var selection = (TextSelection)dte.ActiveDocument.Selection;
			string text = selection.Text;
			if (string.IsNullOrEmpty(text))
			{
				if (selection.IsEmpty)
				{
					selection.WordLeft(false);
					selection.WordRight(true);
				}
				text = selection.Text;
				if (!string.IsNullOrEmpty(text))
				{
					if (text.ToLower().Equals(text))
					{
						text = $"^@{text}";
					}
					else
					{
						text = $"@{text}";
					}
				}
			}

			string envProgramFiles = Environment.GetEnvironmentVariable("PROGRAMFILES");
			string blitzPath = Path.Combine(envProgramFiles, "Blitz", "Blitz.exe");
			if (!File.Exists(blitzPath))
			{
				// Show a message box to prove we were here
				VsShellUtilities.ShowMessageBox(
					this,
					"Failed To locate Blitz.exe, please visit https://natestah.com to download",
					title,
					OLEMSGICON.OLEMSGICON_INFO,
					OLEMSGBUTTON.OLEMSGBUTTON_OK,
					OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
			}

			string userPath = PoorMansIPC.Instance.GetPoorMansIPCPath();
			Directory.CreateDirectory(userPath);

			string fullPath = Path.Combine(userPath, $"{commandName}.txt");
			File.WriteAllText(fullPath, text);
			System.Diagnostics.Process.Start(blitzPath);
		}
		private async void Goto(string gotoCommand)
		{
			GotoExecute(gotoCommand, preview: false);
		}

		private async void GotoPreview(string gotoCommand)
		{
			GotoExecute(gotoCommand, preview: true);
		}

		private async void GotoExecute(string gotoCommand, bool preview)
		{
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
			var splitString = gotoCommand.Split(',');
			if (splitString.Length != 3)
			{
				// Show a message box to prove we were here
				VsShellUtilities.ShowMessageBox(
					this,
					"Failed To Parse command from VISUAL_STUDIO_GOTO, must be 'file,lineNumber,column'",
					"Goto Failure",
					OLEMSGICON.OLEMSGICON_INFO,
					OLEMSGBUTTON.OLEMSGBUTTON_OK,
					OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
				return;
			}

			string file = splitString[0];
			string lineStr = splitString[1];
			string columnStr = splitString[2];

			if (!int.TryParse(lineStr, out var line))
			{
				line = 1;
			}

			if (!int.TryParse(columnStr, out var column))
			{
				column = 1;
			}

			if (!File.Exists(file))
			{
				return;
			}

			var dte = GetGlobalService(typeof(DTE)) as DTE2;
			//var dte = (DTE)ServiceProvider.GetService(typeof(DTE));
			if(preview)
			{
				await VS.Documents.OpenInPreviewTabAsync(file);
			}
			else
			{
				dte.MainWindow.Activate();
				dte.ItemOperations.OpenFile(file);
			}
			((EnvDTE.TextSelection)dte.ActiveDocument.Selection).MoveToLineAndOffset(line, column + 1);
		}

		#endregion
	}
}
