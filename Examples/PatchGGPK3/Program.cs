using LibGGPK3;
using LibGGPK3.Records;
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace PatchGGPK3 {
	public class Program {
		public static void Main(string[] args) {
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
			var ggpk = new GGPK(args[0]);
			Console.WriteLine("Replacing files . . .");
			var zip = ZipFile.OpenRead(args[1]);

			int successed = 0, failed = 0;
			Console.WriteLine();
			foreach (var e in zip.Entries) {
				if (e.FullName.EndsWith('/'))
					continue;
				Console.Write("Replacing " + e.FullName + " . . . ");
				if (ggpk.FindNode(e.FullName) is not FileRecord fr) {
					++failed;
					Console.WriteLine();
					Console.WriteLine("Not found in GGPK!");
					continue;
				}
				var fs = e.Open();
				var b = new byte[e.Length];
				for (var l = 0; l < b.Length;)
					l += fs.Read(b, l, b.Length - l);
				fs.Close();
				fr.ReplaceContent(b);
				++successed;
				Console.WriteLine("Done");
			}
			ggpk.Dispose();
			Console.WriteLine();
			Console.WriteLine("All finished!");
			Console.WriteLine($"Replaced {successed} files, {failed} files failed");
			Console.WriteLine();
			Console.WriteLine("Enter to exit . . .");
			Console.ReadLine();
		}
	}
}