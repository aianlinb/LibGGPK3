using LibBundledGGPK3;
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace PatchBundledGGPK3 {
	public static class Program {
		public static void Main(string[] args) {
			try {
				var version = Assembly.GetExecutingAssembly().GetName().Version!;
				Console.WriteLine($"PatchBundledGGPK3 (v{version.Major}.{version.Minor}.{version.Build})  Copyright (C) 2022 aianlinb"); // ©
				Console.WriteLine();
				if (args.Length == 0) {
					args = new string[2];
					Console.Write("Path to Content.ggpk: ");
					args[0] = Console.ReadLine()!;
					Console.Write("Path to zip file: ");
					args[1] = Console.ReadLine()!;
				} else if (args.Length != 2) {
					Console.WriteLine("Usage: PatchBundledGGPK3 <PathToGGPK> <ZipFile>");
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
				using var ggpk = new BundledGGPK(args[0], false);
				Console.WriteLine("Replacing files . . .");
				using var zip = ZipFile.OpenRead(args[1]);
				var count = LibBundle3.Index.Replace(ggpk.Index, zip.Entries);
				Console.WriteLine("Done!");
				Console.WriteLine($"Replced {count} files");
			} catch (Exception e) {
				Console.Error.WriteLine(e);
			}
			Console.WriteLine();
			Console.WriteLine("Enter to exit . . .");
			Console.ReadLine();
		}
	}
}