using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace PatchBundle3;
public static class Program {
	public static void Main(string[] args) {
		var pause = false;
		try {
			var version = Assembly.GetExecutingAssembly().GetName().Version!;
			Console.WriteLine($"PatchBundle3 (v{version.Major}.{version.Minor}.{version.Build})  Copyright (C) 2022 aianlinb"); // ©
			Console.WriteLine();
			if (args.Length == 0) {
				pause = true;
				args = new string[2];
				Console.Write("Path To _.index.bin: ");
				args[0] = Console.ReadLine()!;
				Console.Write("Path to zip file: ");
				args[1] = Console.ReadLine()!;
			} else if (args.Length != 2) {
				Console.WriteLine("Usage: PatchBundledGGPK3 <PathToIndexBin> <ZipFile>");
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

			Console.WriteLine("Index file: " + args[0]);
			Console.WriteLine("Patch file: " + args[1]);
			Console.WriteLine("Reading index file . . .");
			using var index = new LibBundle3.Index(args[0], false);
			Console.WriteLine("Replacing files . . .");
			using var zip = ZipFile.OpenRead(args[1]);
			Console.WriteLine($"Done! Replaced {LibBundle3.Index.Replace(index, zip.Entries, (fr, path) => {
				Console.WriteLine("Replaced: " + path);
				return false;
			})} files");
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