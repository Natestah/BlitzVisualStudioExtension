using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shapes;

namespace BlitzVisualStudio
{
	public class GotoDirective
	{
		public string SolutionName { get; set; }
		public string FileName { get; set; }
		public int Line { get; set; }
		public int Column { get; set; }
	}
}
