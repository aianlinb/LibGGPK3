using Eto.Forms;
using System;

namespace VisualGGPK3 {
	public static class Program {
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		public static void Main(string[] args) {
			var app = new Application();
			var form = new MainWindow(args.Length != 0 ? args[0] : null);
			app.UnhandledException += (o, e) => MessageBox.Show(form, e.ExceptionObject.ToString(), "Error", MessageBoxType.Error);
			app.Run(form);
		}
	}
}