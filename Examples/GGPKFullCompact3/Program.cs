using LibGGPK3;
using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace GGPKFullCompact3 {
	public static class Program {
		private static readonly CancellationTokenSource cancel = new();
		public static void Main(string[] args) {
			try {
				var version = Assembly.GetExecutingAssembly().GetName().Version!;
				Console.WriteLine($"GGPKFullCompact3 (v{version.Major}.{version.Minor}.{version.Build})  Copyright (C) 2022 aianlinb."); // ©
				Console.WriteLine();
				if (args.Length == 0) {
					args = new string[1];
					Console.Write("Path To GGPK: ");
					args[0] = Console.ReadLine()!;
					Console.WriteLine();
				}
				if (!File.Exists(args[0])) {
					Console.WriteLine("File Not Found: " + args[0]);
					Console.WriteLine("Enter to exit . . .");
					Console.ReadLine();
					return;
				}
				args[0] = Path.GetFullPath(args[0]);

				Console.WriteLine("GGPK path: " + args[0]);
				var size = new FileInfo(args[0]).Length;
				Console.WriteLine("GGPK size: " + size);
				Console.WriteLine("Reading ggpk file . . .");

				var ggpk = new GGPK(args[0]);
				var max = -1;
				var prog = -1;
				Console.CancelKeyPress += OnCancelKeyPress;
				Console.WriteLine("Start compaction . . .  (Ctrl + C to cancel)");
				Console.WriteLine();
				var tsk = ggpk.FullCompactAsync(args[0] + ".new", cancel.Token, new Progress<int>(i => {
					prog = i;
					if (prog > max)
						max = prog;
				}));
				while (prog < 0)
					Thread.Sleep(100);
				while (!tsk.IsCompleted) {
					Console.WriteLine($"Remaining records to be written: {prog}/{max}");
					Thread.Sleep(400);
				}
				Console.CancelKeyPress -= OnCancelKeyPress;
				Console.WriteLine($"Remaining records to be written: {prog}/{max}");
				Console.WriteLine();
				ggpk.Dispose();
				cancel.Dispose();

				if (tsk.IsCanceled) {
					File.Delete(args[0] + ".new");
					if (tsk.Exception != null)
						throw tsk.Exception.InnerException!;
					Console.WriteLine("Cancelled!");
				} else {
					if (tsk.Exception != null)
						throw tsk.Exception.InnerException!;
					Console.WriteLine("Done!");
					File.Move(args[0], args[0] + ".bak");
					File.Move(args[0] + ".new", args[0]);
					var size2 = new FileInfo(args[0]).Length;
					Console.WriteLine("GGPK size: " + size2);
					Console.WriteLine("Reduced " + (size - size2) + " bytes");
					Console.WriteLine("Old ggpk was saved as: " + args[0] + ".bak");
				}
			} catch (Exception e) {
				Console.Error.WriteLine(e);
			}
			Console.WriteLine();
			Console.WriteLine("Enter to exit . . .");
			Console.ReadLine();
		}

		private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) {
			e.Cancel = true;
			cancel.Cancel();
		}
	}
}