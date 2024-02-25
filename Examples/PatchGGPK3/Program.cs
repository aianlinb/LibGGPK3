using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Reflection;

using LibGGPK3;
using LibGGPK3.Records;

namespace PatchGGPK3 {
	public static class Program {
		public static void Main(string[] args) {
			try {
				var version = Assembly.GetExecutingAssembly().GetName().Version!;
				Console.WriteLine($"PatchGGPK3 (v{version.Major}.{version.Minor}.{version.Build})  Copyright (C) 2022 aianlinb"); // ©
				Console.WriteLine();
				if (args.Length == 0) {
					args = new string[2];
					Console.Write("Path to Content.ggpk: ");
					args[0] = Console.ReadLine()!;
					Console.Write("Path to zip file: ");
					args[1] = Console.ReadLine()!;
				} else if (args.Length != 2) {
					Console.WriteLine("Usage: PatchGGPK3 <PathToGGPK> <ZipFile>");
					Console.WriteLine();
					Console.WriteLine("Enter to exit . . .");
					Console.ReadLine();
					return;
				}
				if (!File.Exists(args[0])) {
					Console.WriteLine("FileNotFound: " + args[0]);
					Console.WriteLine();
					Console.WriteLine("Enter to exit . . .");
					Console.ReadLine();
					return;
				}
				if (!File.Exists(args[1])) {
					Console.WriteLine("FileNotFound: " + args[1]);
					Console.WriteLine();
					Console.WriteLine("Enter to exit . . .");
					Console.ReadLine();
					return;
				}

				Console.WriteLine("GGPK: " + args[0]);
				Console.WriteLine("Patch file: " + args[1]);
				Console.WriteLine("Reading ggpk file . . .");
				using var ggpk = new GGPK(args[0]);
				Console.WriteLine("Replacing files . . .");
				var zip = ZipFile.OpenRead(args[1]);

				int successed = 0, failed = 0;
				Console.WriteLine();
				foreach (var entry in zip.Entries) {
					if (entry.FullName.EndsWith('/'))
						continue;
					Console.Write("Replacing " + entry.FullName + " . . . ");
					if (!ggpk.TryFindNode(entry.FullName, out var node) || node is not FileRecord fr) {
						++failed;
						Console.WriteLine();
						Console.WriteLine("Not found in GGPK!");
						continue;
					}
					var len = (int)entry.Length;
					var b = ArrayPool<byte>.Shared.Rent(len);
					try {
						using (var fs = entry.Open())
							fs.ReadExactly(b, 0, len);
						fr.Write(new(b, 0, len));
					} finally {
						ArrayPool<byte>.Shared.Return(b);
					}
					++successed;
					Console.WriteLine("Done");
				}
				Console.WriteLine();
				Console.WriteLine("All finished!");
				Console.WriteLine($"Replaced {successed} files, {failed} files failed");
			} catch (Exception e) {
				Console.Error.WriteLine(e);
			}
			Console.WriteLine();
			Console.WriteLine("Enter to exit . . .");
			Console.ReadLine();
		}
	}
}