using LibBundledGGPK;
using System;
using System.IO;
using System.IO.Compression;

namespace PatchBundledGGPK3 {
	public class Program {
		public static void Main(string[] args) {
			Console.TreatControlCAsInput = true;
			Console.WriteLine("PatchBundledGGPK3  Copyright (C) 2021-2022 aianlinb."); // ©
			Console.WriteLine();
			if (args.Length == 0) {
				args = new string[2];
				Console.Write("Path To GGPK: ");
				args[0] = Console.ReadLine()!;
				Console.Write("Path To Zip File: ");
				args[1] = Console.ReadLine()!;
			} else if (args.Length != 2) {
				Console.WriteLine("Usage: PatchBundledGGPK3 <PathToGGPK> <ZipFile>");
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

			Console.WriteLine("GGPK: " + args[0]);
			Console.WriteLine("Patch file: " + args[1]);
			Console.WriteLine("Reading ggpk file . . .");
			var ggpk = new BundledGGPK(args[0]);
			Console.WriteLine("Replacing files . . .");
			var zip = ZipFile.OpenRead(args[1]);

			ggpk.index.Replace(zip.Entries);
			Console.WriteLine("Done!");
		}
	}
}