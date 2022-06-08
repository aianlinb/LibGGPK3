using LibBundledGGPK;
using LibDat2;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Index = LibBundle3.Index;

namespace PoeChinese3 {
	public class Program {
		public static void Main(string[] args) {
			try {
				Console.OutputEncoding = Encoding.UTF8;
				var assembly = Assembly.GetExecutingAssembly();
				var version = assembly.GetName().Version!;
				Console.WriteLine($"PoeChinese3 (v{version.Major}.{version.Minor}.{version.Build})  Copyright (C) 2022 aianlinb"); // ©
				Console.WriteLine($"流亡黯道 - 啟/禁用繁體中文語系  By aianlinb");
				Console.WriteLine();

				var definitions = assembly.GetManifestResourceStream("PoeChinese3.DatDefinitions.json")!;
				DatContainer.ReloadDefinitions(definitions);
				definitions.Close();

				if (args.Length == 0) {
					args = new string[1];
					Console.WriteLine($"請輸入檔案路徑 (原版 / Steam版)");
					Console.Write("Path to (Content.ggpk / _.index.bin): ");
					args[0] = Console.ReadLine()!.Trim('\'', '"', ' ', '\r', '\n', '\t');
					Console.WriteLine();
				}
				if (!File.Exists(args[0])) {
					Console.WriteLine("檔案不存在 (File not found): " + args[0]);
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
						Console.WriteLine("正在套用 (Modifying) . . .");
						Modify(ggpk.index);
						ggpk.Dispose();
						Console.WriteLine("Done!");
						Console.WriteLine("中文化完成！ 再次使用以還原");
						break;
					case ".bin":
						Console.WriteLine("Index path: " + args[0]);
						Console.WriteLine("Reading index file . . .");
						var index = new Index(args[0], false);
						Console.WriteLine("Modifying . . .");
						Modify(index);
						index.Dispose();
						Console.WriteLine("Done!");
						Console.WriteLine("中文化完成！ 再次使用以還原");
						break;
					default:
						Console.WriteLine("Unknown file extension: " + Path.GetFileName(args[0]));
						break;
				}
			} catch (Exception e) {
				var color = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Error.WriteLine(e);
				Console.ForegroundColor = color;
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