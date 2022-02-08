using System;
using System.IO;
using System.IO.Compression;

namespace PatchBundle3 {
	public class Program {
		public static void Main(string[] args) {
			Console.TreatControlCAsInput = true;
			Console.WriteLine("PatchBundle3  Copyright (C) 2022 aianlinb."); // ©
			Console.WriteLine();
			if (args.Length == 0) {
				args = new string[2];
				Console.Write("Path To _.index.bin: ");
				args[0] = Console.ReadLine()!;
				Console.Write("Path To Zip File: ");
				args[1] = Console.ReadLine()!;
			} else if (args.Length != 2) {
				Console.WriteLine("Usage: PatchBundledGGPK3 <PathToIndexBin> <ZipFile>");
				return;
			}
			if (!File.Exists(args[0])) {
				Console.WriteLine("FileNotFound: " + args[0]);
				return;
			}
			if (!File.Exists(args[1])) {
				Console.WriteLine("FileNotFound: " + args[1]);
				return;
			}

			Console.WriteLine("Index file: " + args[0]);
			Console.WriteLine("Patch file: " + args[1]);
			Console.WriteLine("Reading index file . . .");
			var index = new LibBundle3.Index(args[0]);
			Console.WriteLine("Replacing files . . .");
			var zip = ZipFile.OpenRead(args[1]);

			index.Replace(zip.Entries);
			Console.WriteLine("Done!");
		}
	}
}