using EnvDTE;
using EnvDTE80;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualBasic;
using System;
using System.IO;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Linq;
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
		protected override async System.Threading.Tasks.Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
		{
			// When initialized asynchronously, the current thread may be a background thread at this point.
			// Do any initialization that requires the UI thread after switching to the UI thread.
			await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
			await BlitzSearchThis.InitializeAsync(this);
			await BlitzReplaceThis.InitializeAsync(this);
			PoorMansIPC.Instance.RegisterAction("VISUAL_STUDIO_GOTO", Goto);
			PoorMansIPC.Instance.RegisterAction("VISUAL_STUDIO_GOTO_PREVIEW", GotoPreview);
			PoorMansIPC.Instance.RegisterAction("VISUAL_STUDIO_GOTO_PREVIEW_JSON", GotoPreviewJson);
			PoorMansIPC.Instance.RegisterAction("VISUAL_STUDIO_GOTO_JSON", GotoJson);

			bool isSolutionLoaded = await VS.Solutions.IsOpenAsync();

			if (isSolutionLoaded)
			{
				HandleOpenSolution();
			}

			VS.Events.SolutionEvents.OnAfterOpenSolution += SolutionEvents_OnAfterOpenSolution;

			VS.Events.SolutionEvents.OnAfterOpenProject += SolutionEvents_OnAfterOpenProject;
			VS.Events.SolutionEvents.OnAfterRenameProject += SolutionEvents_OnAfterRenameProject;

			VS.Events.DocumentEvents.Opened += DocumentEvents_Opened;
			VS.Events.DocumentEvents.Closed += DocumentEvents_Closed;
			VS.Events.SelectionEvents.SelectionChanged += SelectionEvents_SelectionChanged;
		}

		private void SolutionEvents_OnAfterRenameProject(Community.VisualStudio.Toolkit.Project obj)
		{
			HandleOpenSolution();
		}

		private void SolutionEvents_OnAfterOpenProject(Community.VisualStudio.Toolkit.Project obj)
		{
			HandleOpenSolution();
		}

		private void SolutionEvents_OnAfterOpenSolution(Community.VisualStudio.Toolkit.Solution obj)
		{
			HandleOpenSolution();
		}

		private void SelectionEvents_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			ThreadHelper.JoinableTaskFactory.Run(async delegate {
				await Set_VSProjectAsync(e.To);
			});
			if(e.To.Type == SolutionItemType.PhysicalFile)
			{
				SendActiveFilesList();
			}
		}

		private void DocumentEvents_Closed(string obj)
		{
			SendActiveFilesList();
		}

		private void SendActiveFilesList(string file = null)
		{
			ThreadHelper.JoinableTaskFactory.Run(async delegate {

				var solution = await VS.Solutions.GetCurrentSolutionAsync();
				string solutionFileName = solution != null ? solution.FullPath : null;
				var activeFiles = new ActiveFilesList { SolutionFileName = solutionFileName };
				var files = new HashSet<string>();
				if(file != null)
				{
					files.Add(file);
				}
				foreach (var item in await VS.Windows.GetAllDocumentWindowsAsync())
				{
					var docView = await item.GetDocumentViewAsync();
					if(docView == null)
					{
						continue;
					}
					if (docView.FilePath != null)
					{
						files.Add(docView.FilePath);
					}
				}

				if (files.Count == 0)
				{
					return;
				}

				activeFiles.ActiveFiles = files.ToList();

				var fileText = System.Text.Json.JsonSerializer.Serialize(activeFiles);
				WriteIpcMessage("VS_ACTIVE_FILES", fileText);
			});
		}

		private void DocumentEvents_Opened(string obj)
		{
			ThreadHelper.JoinableTaskFactory.Run(async delegate { 
				var documentView = await VS.Documents.GetActiveDocumentViewAsync();
				if (documentView != null && documentView.FilePath is string fileName)
				{
					await Set_VSProjectAsync(fileName);
				}
			});
			SendActiveFilesList(obj);
		}

		private async Task Set_VSProjectAsync(SolutionItem item)
		{
			string activeFile = null;
			
			if(item != null && item.Type == SolutionItemType.PhysicalFile)
			{
				activeFile = item.FullPath;
			}

			var solution = await VS.Solutions.GetCurrentSolutionAsync();
			while (item != null && item.Parent != null)
			{
				item = item.Parent;
				if (item.Type == SolutionItemType.Project)
				{
					var export = new SelectedProjectExport()
					{
						ActiveFileInProject = activeFile,
						Name = item.FullPath,
						BelongsToSolution = solution.FullPath
					};
					var fileText = System.Text.Json.JsonSerializer.Serialize<SelectedProjectExport>(export);
					WriteIpcMessage("VS_PROJECT", fileText);
					break;
				}
			}
		}
		private async Task Set_VSProjectAsync(string fileName)
		{
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
			SolutionItem item = await PhysicalFile.FromFileAsync(fileName);
			await Set_VSProjectAsync(item);
		}

		public void HandleOpenSolution(object sender = null, EventArgs e = null)
		{
			ThreadHelper.JoinableTaskFactory.Run(async delegate { 
				var solution = await VS.Solutions.GetCurrentSolutionAsync();

				if (solution == null)
				{
					return;
				}

				var projects = await VS.Solutions.GetAllProjectsAsync();
				var projectCollection = new List<Project>();
				var solutionExport = new SolutionExport() { Name = solution.FullPath, Projects = projectCollection };

				foreach (Community.VisualStudio.Toolkit.Project project in projects)
				{
					var projectExport = new Project() { Name = project.FullPath };
					projectCollection.Add(projectExport);
					var fileSet = new HashSet<string>();
					RecursiveGetFiles(fileSet, project);
					projectExport.Files = fileSet.ToList();
				}

				var fileText = System.Text.Json.JsonSerializer.Serialize<SolutionExport>(solutionExport);
				WriteIpcMessage("VS_SOLUTION", fileText);
			});
		}

		private void RecursiveGetFiles( HashSet<string> files, SolutionItem item)
		{
			if(item.Type == SolutionItemType.PhysicalFile)
			{
				files.Add(item.FullPath);
			}

			if(item.Children != null)
			{
				foreach(var child in item.Children)
				{
					RecursiveGetFiles(files, child);
				}
			}
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

			string blitzPath = GetBlitzPath();
			BlitzInstallCheck();
			WriteIpcMessage(commandName, text);
			BootStrapBlitz();
		}

		public void WriteIpcMessage( string commandName,  string text)
		{
			string userPath = PoorMansIPC.Instance.GetPoorMansIPCPath();
			Directory.CreateDirectory(userPath);
			string fullPath = Path.Combine(userPath, $"{commandName}.txt");
			for (int i = 0; i < 3; i++)
			{
				try
				{
					File.WriteAllText(fullPath, text);
				}
				catch 
				{
					System.Threading.Thread.Sleep(30);
					continue;
				}
			}
		}

		public void BootStrapBlitz()
		{

			try
			{
				//If blitz is running, return.. 
				if (System.Diagnostics.Process.GetProcessesByName("Blitz").Length > 0)
				{
					return;
				}
			}
			catch
			{
				// assumes security issue, it's ok since BlitzSearch is Single Instance.. just call it.
			}
			System.Diagnostics.Process.Start(GetBlitzPath());
		}

		public string GetBlitzPath()
		{
			string envProgramFiles = Environment.GetEnvironmentVariable("PROGRAMFILES");
			return Path.Combine(envProgramFiles, "Blitz", "Blitz.exe");
		}

		public bool BlitzInstallCheck()
		{
			string blitzPath = GetBlitzPath();
			if (!File.Exists(blitzPath))
			{
				// Show a message box then go to download page.
				VsShellUtilities.ShowMessageBox(
					this,
					"Failed To locate Blitz.exe, please visit https://natestah.com to download",
					"BlitzSearch Is Not Installed",
					OLEMSGICON.OLEMSGICON_INFO,
					OLEMSGBUTTON.OLEMSGBUTTON_OK,
					OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

				System.Diagnostics.Process.Start("https://natestah.com/download");

				return false;
			}
			return true;
		}


		private void Goto(string gotoCommand)
		{
			ThreadHelper.JoinableTaskFactory.Run(async delegate
			{
				await GotoExecuteAsync(gotoCommand, preview: false);
			});
		}

		private void GotoPreview(string gotoCommand)
		{
			ThreadHelper.JoinableTaskFactory.Run(async delegate
			{
				await GotoExecuteAsync(gotoCommand, preview: true);
			});
		}

		private void GotoJson(string gotoCommand)
		{
			ThreadHelper.JoinableTaskFactory.Run(async delegate
			{
				await GotoExecuteJSONAsync(gotoCommand, preview: false);
			});
		}

		private void GotoPreviewJson(string gotoCommand)
		{
			ThreadHelper.JoinableTaskFactory.Run(async delegate
			{
				await GotoExecuteJSONAsync(gotoCommand, preview: true);
			});
		}


		private async Task GotoExecuteJSONAsync(string gotoCommand, bool preview)
		{
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);

			GotoDirective directive = null;

			try
			{
				directive = System.Text.Json.JsonSerializer.Deserialize<GotoDirective>(gotoCommand);
			}
			catch
			{
				return;
			}



			var solution = await VS.Solutions.GetCurrentSolutionAsync();

			//Generally keeps from having multiple instances responding to the command.
			if(directive.SolutionName != solution.FullPath)
			{
				return;
			}

			if (preview)
			{
				await VS.Documents.OpenInPreviewTabAsync(directive.FileName);
			}
			else
			{
				await VS.Documents.OpenAsync(directive.FileName);
			}


			//Todo: See about DTE -> Community wrapper here.
			var dte = GetGlobalService(typeof(DTE)) as DTE2;
			((EnvDTE.TextSelection)dte.ActiveDocument.Selection).MoveToLineAndOffset(directive.Line, directive.Column + 1);
		}

		private async Task GotoExecuteAsync(string gotoCommand, bool preview)
		{
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);

			//Todo: Remove this if/when I get Version checking between Blitz<->Plugin, or just after some time 11/1/25
			var legacyChar = ','; // This was a mistake as ',' is a legal file character. 

			var splitChar = gotoCommand.Contains(';') ? ';' : legacyChar;

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

			//Todo: See about DTE -> Community wrapper here.
			var dte = GetGlobalService(typeof(DTE)) as DTE2;
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
