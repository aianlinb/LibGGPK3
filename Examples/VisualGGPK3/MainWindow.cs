using Eto.Forms;
using System.Reflection;

namespace VisualGGPK3 {
	public class MainWindow : Form {
		public MainWindow() {
			var version = Assembly.GetExecutingAssembly().GetName().Version!;
			Title = $"VisualGGPK3 (v{version.Major}.{version.Minor}.{version.Build})";
			ClientSize = new(600, 400);
			Content = new Panel();
			// TODO . . .
		}
	}
}