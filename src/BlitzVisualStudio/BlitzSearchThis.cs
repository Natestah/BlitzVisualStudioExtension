﻿using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
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

			var menuCommandID = new CommandID(BlitzVisualStudioPackage.CommandSet, CommandId);
			var menuItem = new MenuCommand(this.Execute, menuCommandID);
			commandService.AddCommand(menuItem);
		}

		/// <summary>
		/// Gets the instance of the command.
		/// </summary>
		public static BlitzSearchThis Instance
		{
			get;
			private set;
		}


		[Import]
		internal SVsServiceProvider ServiceProvider = null;

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
			if (package is BlitzVisualStudioPackage blitzVisualStudioPackage)
			{
				blitzVisualStudioPackage.SendSearchContext("Blitz Search This", "SET_SEARCH");
			}
		}
	}
}
