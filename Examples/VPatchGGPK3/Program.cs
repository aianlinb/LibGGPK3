using System;

using Eto.Forms;

namespace VPatchGGPK3;
public static class Program {
	/// <summary>
	///  The main entry point for the application.
	/// </summary>
	[STAThread]
	public static void Main() {
#if Mac
		Eto.Style.Add<Eto.Mac.Forms.ApplicationHandler>(null, handler => handler.AllowClosingMainForm = true);
#endif
		var app = new Application();
		app.UnhandledException += (o, e) => MessageBox.Show(app.MainForm, e.ExceptionObject.ToString(), "Error", MessageBoxType.Error);
		app.Run(app.MainForm = new MainWindow());
	}
}