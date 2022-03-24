using LibGGPK3;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace GGPKFullCompact3 {
	public static class Program {
		private static readonly CancellationTokenSource cancel = new();
		public static void Main(string[] args) {
			try {
				var version = Assembly.GetExecutingAssembly().GetName().Version!;
				Console.WriteLine($"GGPKFullCompact3 (v{version.Major}.{version.Minor}.{version.Build})  Copyright (C) 2022 aianlinb");
				Console.WriteLine();
				if (args.Length == 0) {
					args = new string[2];
					Console.Write("Path to Content.ggpk: ");
					args[0] = Console.ReadLine()!;
					Console.Write("Path to save new GGPK: ");
					args[1] = Console.ReadLine()!;
					Console.WriteLine();
				} else if (args.Length == 1) {
					Console.WriteLine("Usage: GGPKFullCompact3.exe <Path to Content.ggpk> <Path to save new GGPK>");
					Console.WriteLine();
					Console.WriteLine("Enter to exit . . .");
					Console.ReadLine();
					return;
				}
				if (!File.Exists(args[0])) {
					Console.WriteLine("File Not Found: \"" + args[0] + "\"");
					Console.WriteLine();
					Console.WriteLine("Enter to exit . . .");
					Console.ReadLine();
					return;
				}
				args[0] = Path.GetFullPath(args[0]);
				args[1] = Path.GetFullPath(args[1]);
				if (args[0] == args[1])
					throw new ArgumentException("The new ggpk path cannot be the same with the old ggpk path");

				Console.WriteLine("GGPK path: " + args[0]);
				Console.WriteLine("New GGPK path: " + args[1]);
				Console.WriteLine();
				Console.WriteLine("Reading ggpk file . . .");

				var ggpk = new GGPK(args[0]);
				var max = -1;
				var prog = -1;
				var nodes = ggpk.RecursiveTree(ggpk.Root).ToList();
				var size = new FileInfo(args[0]).Length;
				var size2 = ggpk.GgpkRecord.Length + nodes.Sum(n => (long)n.Length);
				Console.WriteLine("Old GGPK size: " + size);
				Console.WriteLine("New GGPK size: " + size2);

				Console.CancelKeyPress += OnCancelKeyPress;
				Console.WriteLine("Start compaction . . .  (Ctrl + C to cancel)");
				Console.WriteLine();
				var tsk = ggpk.FullCompactAsync(args[1], cancel.Token, new Progress<int>(i => {
					prog = i;
					if (prog > max)
						max = prog;
				}), nodes);
				while (prog < 0) {
					Thread.Sleep(200);
					if (tsk.Exception != null)
						throw tsk.Exception;
				}
				while (!tsk.IsCompleted) {
					Console.WriteLine($"Remaining records to be written: {prog}/{max}");
					Thread.Sleep(1500);
				}
				Console.CancelKeyPress -= OnCancelKeyPress;
				Console.WriteLine($"Remaining records to be written: {prog}/{max}");
				Console.WriteLine();
				ggpk.Dispose();
				cancel.Dispose();

				if (tsk.Exception != null)
					throw tsk.Exception!;
				if (tsk.IsCanceled)
					Console.WriteLine("Cancelled!");
				else {
					Console.WriteLine("Done!");
					Console.WriteLine("Reduced " + (size - size2) + " bytes");
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