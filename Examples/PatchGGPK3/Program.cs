using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

using LibGGPK3;
using LibGGPK3.Records;

using SystemExtensions.Collections;

namespace PatchGGPK3;
public static class Program {
	public static void Main(string[] args) {
		var pause = false;
		try {
			var version = Assembly.GetExecutingAssembly().GetName().Version!;
			Console.WriteLine($"PatchGGPK3 (v{version.Major}.{version.Minor}.{version.Build})  Copyright (C) 2022 aianlinb"); // ©
			Console.WriteLine();
			if (args.Length == 0) {
				pause = true;
				args = new string[2];
				Console.Write("Path to Content.ggpk: ");
				args[0] = Console.ReadLine()!;
				Console.Write("Path to zip file: ");
				args[1] = Console.ReadLine()!;
			} else if (args.Length != 2 && args.Length != 3) {
				Console.WriteLine("Usage: PatchGGPK3 <PathToGGPK> <ZipFile> [allowAdd]");
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
			var allowAdd = args.Length == 3 && args[2].Equals("allowAdd", StringComparison.InvariantCultureIgnoreCase);
			Console.WriteLine("GGPK: " + args[0]);
			Console.WriteLine("Patch file: " + args[1]);

			Console.WriteLine("Reading ggpk file . . .");
			using var ggpk = new GGPK(args[0]);
			Console.WriteLine("Replacing files . . .");
			var zip = ZipFile.OpenRead(args[1]);
			Console.WriteLine($"Done! Replaced {GGPK.Replace(ggpk.Root, zip.Entries, (fr, p, added) => {
				Console.WriteLine((added ? "Added: " : "Replaced: ") + p);
				return false;
			}, allowAdd)} files.");
		} catch (Exception e) {
			Console.Error.WriteLine(e);
		}

		if (pause) {
			Console.WriteLine();
			Console.WriteLine("Enter to exit . . .");
			Console.ReadLine();
		}
	}
}