using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

using LibGGPK3;

namespace GGPKFastCompact3 {
	public static class Program {
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
				using (var cancel = new CancellationTokenSource()) {
					void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) {
						e.Cancel = true;
						cancel.Cancel();
					}
					Console.CancelKeyPress += OnCancelKeyPress;
					var lastTime = DateTime.UtcNow;
					try {
						ggpk.FastCompact(cancel.Token, new Progress<int>(i => {
							prog = i;
							if (prog > max)
								max = prog;
							var now = DateTime.UtcNow;
							if ((now - lastTime).TotalMilliseconds >= 600) {
								Console.WriteLine($"Remaining FreeRecords to be filled: {prog}/{max}");
								lastTime = now;
							}
						}));
					} catch (OperationCanceledException) {
						Console.WriteLine("Cancelled!");
					} finally {
						Console.CancelKeyPress -= OnCancelKeyPress;
					}
				}
				Console.WriteLine($"Remaining FreeRecords to be filled: {prog}/{max}");
				Console.WriteLine();
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
	}
}