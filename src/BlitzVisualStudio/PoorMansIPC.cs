using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System.Threading.Tasks;
namespace BlitzVisualStudio
{

	public class PoorMansIPC
	{
		public static PoorMansIPC Instance = new PoorMansIPC();

		private Dictionary<string, Action<string>> _actions = new Dictionary<string, Action<string>>();
		private Dictionary<string, Action<string>> _solutionAction= new Dictionary<string, Action<string>>();

		private FileSystemWatcher _fileSystemWatcher;
		private Func<string, bool> _solutionTest;

		public PoorMansIPC()
		{
			var specificFolder = GetPoorMansIPCPath(); 
			Directory.CreateDirectory(specificFolder);

			var watcher = new FileSystemWatcher(specificFolder, "*");
			watcher.EnableRaisingEvents = true;
			watcher.Created += WatcherOnCreated;
			watcher.Renamed += WatcherOnRenamed;
			watcher.Deleted += WatcherOnDeleted;
			watcher.Changed += WatcherOnChanged;
			_fileSystemWatcher = watcher;
		}

		public string GetPoorMansIPCPath()
		{
			string envAppData = Environment.GetEnvironmentVariable("APPDATA");
			return Path.Combine(envAppData, "NathanSilvers", "POORMANS_IPC");

		}

		public void RegisterAction(string name, Action<string> action) => _actions[name] = action;

		public void RegisterSolutionAction(string name, Action<string> action) => _solutionAction[name] = action;

		private void DoActionWithFile(string fullFilename)
		{
			ThreadHelper.JoinableTaskFactory.Run(async delegate
			{
				await DoActionWithFileAsync(fullFilename);
			});
		}

		private async Task DoActionWithFileAsync(string fullFilename)
		{
			try
			{
				string action = Path.GetFileNameWithoutExtension(fullFilename).ToUpper();

				bool isSolutionSpecificAction = false;
				string solutionName = string.Empty;
				if (action.Contains(","))
				{
					isSolutionSpecificAction = true;
					var split = action.Split(',');
					if (split.Length != 2)
					{
						//not currently supporting anything else
						return;
					}
					action = split[0];
					solutionName = split[1];
				}

				if (!_actions.TryGetValue(action, out var function))
				{
					return;
				}

				//before reading the file lets see if the solution matches
				if (isSolutionSpecificAction && !_solutionTest(solutionName))
				{
					return;
				}

				string text = null;
				for (int i = 0; i < 10; i++)
				{
					try
					{
						using (var fi = File.Open(fullFilename, FileMode.Open, FileAccess.Read, FileShare.Read))
						{
							using (var sr = new StreamReader(fi))
							{
								text = await sr.ReadToEndAsync();
							}
						}
					}
					catch (Exception)
					{
						// wait for a bit.. 
						//await Task.Delay(20);
					}
				}

				if (text != null)
				{
					function.Invoke(text);
				}
			}
			catch (Exception e)
			{
				//Todo Message box.
				Console.WriteLine(e);
			}
		}

		private void WatcherOnChanged(object sender, FileSystemEventArgs e) => DoActionWithFile(e.FullPath);

		private void WatcherOnDeleted(object sender, FileSystemEventArgs e) => DoActionWithFile(e.FullPath);

		private void WatcherOnRenamed(object sender, RenamedEventArgs e) => DoActionWithFile(e.FullPath);

		private void WatcherOnCreated(object sender, FileSystemEventArgs e) => DoActionWithFile(e.FullPath);

		public void ExecuteWithin(DateTime utcNow, TimeSpan withinTime)
		{
			foreach (var file in Directory.EnumerateFiles(_fileSystemWatcher.Path))
			{
				var lastModified = File.GetLastWriteTimeUtc(file);
				if (utcNow - lastModified < withinTime)
				{
					DoActionWithFile(file);
				}
			}
		}

		internal void RegisterSolutionTest(Func<string, bool> isSolutionCurrent)
		{
			_solutionTest = isSolutionCurrent;
		}
	}
}
