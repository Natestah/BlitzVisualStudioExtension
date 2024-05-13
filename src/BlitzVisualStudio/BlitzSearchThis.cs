using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace BlitzVisualStudio
{
	/// <summary>
	/// Command handler
	/// </summary>
	internal sealed class BlitzSearchThis
	{
		/// <summary>
		/// Command ID.
		/// </summary>
		public const int CommandId = 0x0100;

		/// <summary>
		/// Command menu group (command set GUID).
		/// </summary>
		public static readonly Guid CommandSet = new Guid("b838b65e-b0f4-4106-aa88-e936bf0b2e0c");

		/// <summary>
		/// VS Package that provides this command, not null.
		/// </summary>
		private readonly AsyncPackage package;

		/// <summary>
		/// Initializes a new instance of the <see cref="BlitzSearchThis"/> class.
		/// Adds our command handlers for menu (commands must exist in the command table file)
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		/// <param name="commandService">Command service to add command to, not null.</param>
		private BlitzSearchThis(AsyncPackage package, OleMenuCommandService commandService)
		{
			this.package = package ?? throw new ArgumentNullException(nameof(package));
			commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

			var menuCommandID = new CommandID(CommandSet, CommandId);
			var menuItem = new MenuCommand(this.Execute, menuCommandID);
			commandService.AddCommand(menuItem);
			PoorMansIPC.Instance.RegisterAction("VISUAL_STUDIO_GOTO", Goto);
		}

		/// <summary>
		/// Gets the instance of the command.
		/// </summary>
		public static BlitzSearchThis Instance
		{
			get;
			private set;
		}

		/// <summary>
		/// Gets the service provider from the owner package.
		/// </summary>
		private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
		{
			get
			{
				return this.package;
			}
		}

		/// <summary>
		/// Initializes the singleton instance of the command.
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		public static async Task InitializeAsync(AsyncPackage package)
		{
			// Switch to the main thread - the call to AddCommand in BlitzSearchThis's constructor requires
			// the UI thread.
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

			OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
			Instance = new BlitzSearchThis(package, commandService);

			
		}

		private async void Goto(string gotoCommand)
		{
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
			var splitString = gotoCommand.Split(',');
			if(splitString.Length != 3)
			{
				// Show a message box to prove we were here
				VsShellUtilities.ShowMessageBox(
					this.package,
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

			if( !int.TryParse(lineStr, out var line))
			{
				line = 1;
			}

			if (!int.TryParse(columnStr, out var column))
			{
				column = 1;
			}

			var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
			dte.MainWindow.Activate();
			dte.ItemOperations.OpenFile(file);
			((EnvDTE.TextSelection)dte.ActiveDocument.Selection).MoveToLineAndOffset(line, column+1);
		}
		 
		/// <summary>
		/// This function is the callback used to execute the command when the menu item is clicked.
		/// See the constructor to see how the menu item is associated with this function using
		/// OleMenuCommandService service and MenuCommand class.
		/// </summary>
		/// <param name="sender">Event sender.</param>
		/// <param name="e">Event args.</param>
		private void Execute(object sender, EventArgs e)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			string title = "Blitz Search This";

			var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;

			if (dte.ActiveDocument != null)
			{
				var selection = (TextSelection)dte.ActiveDocument.Selection;
				string text = selection.Text;
				if(string.IsNullOrEmpty(text))
				{
					if (selection.IsEmpty)
					{
						selection.WordLeft(false);
						selection.WordRight(true);
					}
					text = selection.Text;
					if(!string.IsNullOrEmpty(text))
					{
						text = $"@{text}";
					}
				}

				string envProgramFiles = Environment.GetEnvironmentVariable("PROGRAMFILES");
				string envAppData =  Environment.GetEnvironmentVariable("APPDATA");
				string blitzPath = Path.Combine(envProgramFiles, "Blitz", "Blitz.exe");
				if( !File.Exists(blitzPath))
				{
					// Show a message box to prove we were here
					VsShellUtilities.ShowMessageBox(
						this.package,
						"Failed To locate Blitz.exe, please visit https://natestah.com to download",
						title,
						OLEMSGICON.OLEMSGICON_INFO,
						OLEMSGBUTTON.OLEMSGBUTTON_OK,
						OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
				}

				string userPath = PoorMansIPC.Instance.GetPoorMansIPCPath();
				Directory.CreateDirectory(userPath);

				string fullPath = Path.Combine(userPath, "SET_SEARCH.txt");
				File.WriteAllText(fullPath, text);
				System.Diagnostics.Process.Start(blitzPath);
			}
		}
	}
}
