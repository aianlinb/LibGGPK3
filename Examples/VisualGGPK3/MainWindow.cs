using Eto.Drawing;
using Eto.Forms;
using System.Reflection;

namespace VisualGGPK3 {
	public class MainWindow : Form {
		public MainWindow() {
			var version = Assembly.GetExecutingAssembly().GetName().Version!;
			Title = $"VisualGGPK3 (v{version.Major}.{version.Minor}.{version.Build})";
			ClientSize = new(400, 200);
			Content = new Label() {
				Size = ClientSize,
				TextAlignment = TextAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Text = "This program is not yet implemented",
				TextColor = Colors.Red,
				Font = new(SystemFont.Default, 16)
			};
			// TODO . . .
		}
	}
}