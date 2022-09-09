using LibBundledGGPK;
using System;
using System.IO;
using System.Reflection;

namespace ExtractBundledGGPK3 {
	public class Program {
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

				Console.WriteLine("GGPK: " + args[0]);
				Console.WriteLine("Path to extract: " + args[1]);
				Console.WriteLine("Path to save: " + args[2]);
				Console.WriteLine("Reading ggpk file . . .");
				var ggpk = new BundledGGPK(args[0]);
				Console.WriteLine("Searching files . . .");
				var node = ggpk.Index.FindNode(args[1]);
				if (node == null) {
					Console.WriteLine("Not found in GGPK: " + args[1]);
					Console.WriteLine();
					Console.WriteLine("Enter to exit . . .");
					Console.ReadLine();
					return;
				}
				Console.WriteLine("Extracting files . . .");
				ggpk.Index.Extract(node, args[2]);
				ggpk.Dispose();
				Console.WriteLine("Done!");
			} catch (Exception e) {
				Console.Error.WriteLine(e);
			}
			Console.WriteLine();
			Console.WriteLine("Enter to exit . . .");
			Console.ReadLine();
		}
	}
}