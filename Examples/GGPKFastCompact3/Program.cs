using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LibGGPK3;

namespace GGPKFastCompact3;
public static class Program {
	public static void Main(string[] args) {
		try {
			Console.OutputEncoding = System.Text.Encoding.UTF8;
			var asm = Assembly.GetExecutingAssembly();
			var version = asm.GetName().Version!;
			Console.WriteLine($"{asm.GetName().Name} (v{version.Major}.{version.Minor}.{version.Build}{(version.Revision == 0
				? "" : "." + version.Revision)})  {asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright}");
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
			Console.WriteLine("Starting compaction . . .");
			Console.WriteLine();
			using (var cancel = new CancellationTokenSource()) {
				void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) {
					e.Cancel = true;
					cancel.Cancel();
				}
				Console.CancelKeyPress += OnCancelKeyPress;
				var running = true;
				var lastTime = DateTime.UtcNow;
				try {
					Task.Run(async () => {
						while (running) {
							if (prog != -1) {
								var now = DateTime.UtcNow;
								if ((now - lastTime).TotalMilliseconds >= 600) {
									Console.WriteLine($"Remaining FreeRecords to be filled: {prog}/{max}");
									lastTime = now;
								}
							}
							await Task.Delay(500);
						}
					});
					ggpk.FastCompact(cancel.Token, new Progress<int>(i => {
						// Here will be called by multiple threads.
						prog = i;
						if (prog > max)
							max = prog;
					}));
				} catch (OperationCanceledException) {
					Console.WriteLine("Cancelled!");
				} finally {
					Console.CancelKeyPress -= OnCancelKeyPress;
					running = false;
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