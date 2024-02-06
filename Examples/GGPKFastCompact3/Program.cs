using LibGGPK3;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace GGPKFastCompact3 {
	public static class Program {
		private static readonly CancellationTokenSource cancel = new();
		public static void Main(string[] args) {
			try {
				var version = Assembly.GetExecutingAssembly().GetName().Version!;
				Console.WriteLine($"GGPKFastCompact3 (v{version.Major}.{version.Minor}.{version.Build})  Copyright (C) 2022 aianlinb");
				Console.WriteLine();
				if (args.Length == 0) {
					Console.Write("Path to Content.ggpk: ");
					args = [Console.ReadLine()!];
					Console.WriteLine();
				}
				if (!File.Exists(args[0])) {
					Console.WriteLine("File Not Found: \"" + args[0] + "\"");
					Console.WriteLine();
					Console.WriteLine("Enter to exit . . .");
					Console.ReadLine();
					return;
				}
				args[0] = Path.GetFullPath(args[0]);

				Console.WriteLine("GGPK path: " + args[0]);
				var size = new FileInfo(args[0]).Length;
				Console.WriteLine("GGPK size: " + size);
				Console.WriteLine("Reading ggpk file . . .");

				using var ggpk = new GGPK(args[0]);
				var max = -1;
				var prog = -1;
				Console.WriteLine("Start compaction . . .");
				Console.WriteLine();
				Console.CancelKeyPress += OnCancelKeyPress;
				using var tsk = ggpk.FastCompactAsync(cancel.Token, new Progress<int>(i => {
					prog = i;
					if (prog > max)
						max = prog;
				}));
				while (prog < 0) {
					Thread.Sleep(200);
					if (tsk.Exception is not null)
						throw tsk.Exception;
				}
				while (!tsk.IsCompleted) {
					Console.WriteLine($"Remaining FreeRecords to be filled: {prog}/{max}");
					Thread.Sleep(500);
				}
				Console.CancelKeyPress -= OnCancelKeyPress;
				Console.WriteLine($"Remaining FreeRecords to be filled: {prog}/{max}");
				Console.WriteLine();
				cancel.Dispose();
				if (tsk.Exception is not null)
					throw tsk.Exception!;
				if (tsk.IsCanceled)
					Console.WriteLine("Cancelled!");
				else
					Console.WriteLine("Done!");
				var size2 = new FileInfo(args[0]).Length;
				Console.WriteLine("GGPK size: " + size2);
				Console.WriteLine("Reduced " + (size - size2) + " bytes");
				Console.WriteLine("Total size of remaining FreeRecords: " + ggpk.FreeRecords.Sum(f => f.Length));
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