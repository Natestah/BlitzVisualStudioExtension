using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlitzVisualStudio
{
	public class SolutionExport
	{
		public string Name { get; set; }
		public List<Project> Projects { get; set; }
	}

	public class Project
	{
		public string Name { get; set; }
		public List<string> Files { get; set; }
	}

	public class SelectedProjectExport
	{
		public string ActiveFileInProject { get; set; }
		public string Name { get; set; }
		public string BelongsToSolution { get; set; }
	}
}
