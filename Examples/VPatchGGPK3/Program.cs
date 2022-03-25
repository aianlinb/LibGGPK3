using Eto.Forms;
using System;
using System.Configuration;

namespace VPatchGGPK3 {
	public static class Program {
		/// <summary>
		///  The main entry point for the application.
		/// </summary>
		[STAThread]
		public static void Main() {
			var app = new Application();
			var form = new MainWindow();
			app.UnhandledException += (o, e) => MessageBox.Show(form, e.ExceptionObject.ToString(), "Error", MessageBoxType.Error);
			app.Run(form);
		}
	}
}