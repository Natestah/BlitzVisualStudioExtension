using System;
using System.Collections.Generic;
using System.IO;

namespace BlitzVisualStudio
{
	public class PoorMansIPC
	{
		public static PoorMansIPC Instance = new PoorMansIPC();

		private Dictionary<string, Action<string>> _actions = new Dictionary<string, Action<string>>();

		private FileSystemWatcher _fileSystemWatcher;
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

		private void DoActionWithFile(string fullFilename)
		{
			try
			{
				string action = Path.GetFileNameWithoutExtension(fullFilename).ToUpper();
				if (_actions.TryGetValue(action, out var function))
				{
					string text = null;
					for (int i = 0; i < 10; i++)
					{
						try
						{
							using (var fi = File.Open(fullFilename, FileMode.Open, FileAccess.Read, FileShare.Read))
							{
								using (var sr = new StreamReader(fi))
								{
									text = sr.ReadToEnd();
								}
							}
						}
						catch (Exception ex)
						{
							// wait for a bit.. 
							System.Threading.Thread.Sleep(20);
						}
					}
					if(text != null)
					{
						function.Invoke(text);
					}
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
	}
}
