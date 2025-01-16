using System;
using System.IO;
using System.Reflection;

using LibBundledGGPK3;

namespace ExtractBundledGGPK3;
public static class Program {
	public static void Main(string[] args) {
		try {
			var version = Assembly.GetExecutingAssembly().GetName().Version!;
			Console.WriteLine($"ExtractBundledGGPK3 (v{version.Major}.{version.Minor}.{version.Build})  Copyright (C) 2022 aianlinb"); // ©
			Console.WriteLine();
			if (args.Length == 0) {
				args = new string[3];
				Console.Write("Path to Content.ggpk: ");
				args[0] = Console.ReadLine()!;
				Console.Write("Path to directory/file to extract: ");
				args[1] = Console.ReadLine()!;
				Console.Write("Path to save the extracted directory/file: ");
				args[2] = Console.ReadLine()!;
			} else if (args.Length != 3) {
				Console.WriteLine("Usage: ExtractBundledGGPK3 <PathToGGPK> <PathToExtract> <PathToSave>");
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

			var nodePath = args[1].TrimEnd('/');
			var path = args[2];
			Console.WriteLine("GGPK: " + args[0]);
			Console.WriteLine("Path in ggpk to extract: " + nodePath);
			Console.WriteLine("Path to save: " + path);
			Console.WriteLine("Reading ggpk file . . .");
			//using var ggpk = new BundledGGPK(args[0]);
			using var ggpk = new BundledGGPK(args[0], false);
			var failed = ggpk.Index.ParsePaths();
			if (failed != 0) {
				var tmp = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"Warning: Failed to parse path of {failed} files in the index file");
				Console.ForegroundColor = tmp;
			}
			Console.WriteLine("Searching files . . .");
			if (!ggpk.Index.TryFindNode(nodePath, out var node)) {
				Console.WriteLine("Not found in GGPK: " + nodePath);
				Console.WriteLine();
				Console.WriteLine("Enter to exit . . .");
				Console.ReadLine();
				return;
			}
			Console.WriteLine("Extracting files . . .");
			Console.WriteLine($"Done! Extracted {LibBundle3.Index.ExtractParallel(node, path, (fr, path) => {
				Console.WriteLine("Extracted: " + path);
				return false;
			})} files.");
		} catch (Exception e) {
			Console.Error.WriteLine(e);
		}
		Console.WriteLine();
		Console.WriteLine("Enter to exit . . .");
		Console.ReadLine();
	}
}