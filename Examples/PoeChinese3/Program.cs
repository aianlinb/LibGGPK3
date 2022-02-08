using LibBundledGGPK;
using LibDat2;
using System;
using System.IO;
using System.Reflection;
using Index = LibBundle3.Index;

#nullable disable
namespace PoeChinese3 {
	public class Program {
		public static void Main(string[] args) {
			try {
				Console.TreatControlCAsInput = true;
				var version = Assembly.GetExecutingAssembly().GetName().Version;
				Console.WriteLine($"PoeChinese3 (v{version.Major}.{version.Minor}.{version.Build})  Copyright (C) 2022 aianlinb."); // ©
				Console.WriteLine();
				if (args.Length == 0) {
					args = new string[1];
					Console.Write("Path to Content.ggpk / _.index.bin: ");
					args[0] = Console.ReadLine()!;
					Console.WriteLine();
				}
				if (!File.Exists(args[0])) {
					Console.WriteLine("File not found: " + args[0]);
					Console.WriteLine();
					Console.WriteLine("Enter to exit . . .");
					Console.ReadLine();
					return;
				}
				args[0] = Path.GetFullPath(args[0]);

				switch (Path.GetExtension(args[0]).ToLower()) {
					case ".ggpk":
						Console.WriteLine("GGPK path: " + args[0]);
						Console.WriteLine("Reading ggpk file . . .");
						var ggpk = new BundledGGPK(args[0], false);
						Console.WriteLine("Modifying . . .");
						Modify(ggpk.index);
						ggpk.Dispose();
						Console.WriteLine("Done!");
						break;
					case ".bin":
						Console.WriteLine("Index path: " + args[0]);
						Console.WriteLine("Reading index file . . .");
						var index = new Index(args[0], false);
						Console.WriteLine("Modifying . . .");
						Modify(index);
						index.Dispose();
						Console.WriteLine("Done!");
						break;
					default:
						Console.WriteLine("Unknown file extension: " + Path.GetFileName(args[0]));
						break;
				}
			} catch (Exception e) {
				Console.Error.WriteLine(e);
			}
			Console.WriteLine();
			Console.WriteLine("Enter to exit . . .");
			Console.ReadLine();
		}

		public static void Modify(Index index) {
			if (!index.TryGetFile("Data/Languages.dat", out var lang))
				throw new("Cannot find file: Data/Languages.dat");
			var dat = new DatContainer(lang.Read().ToArray(), "Languages.dat");
			int frn = 1, tch = 6;
			for (var i = 0; i < dat.FieldDatas.Count; ++i) {
				var s = (string)dat.FieldDatas[i][1].Value;
				if (s == "French")
					frn = i;
				else if (s == "Traditional Chinese")
					tch = i;
			}
			var rowFrn = dat.FieldDatas[frn];
			var rowTch = dat.FieldDatas[tch];
			var tmp = rowFrn[1];
			rowFrn[1] = rowTch[1];
			rowTch[1] = tmp;
			tmp = rowFrn[2];
			rowFrn[2] = rowTch[2];
			rowTch[2] = tmp;
			lang.Write(dat.Save(false, false)); // also saved the index
		}
	}
}