using LibBundledGGPK;
using LibDat2;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Index = LibBundle3.Index;

namespace PoeChinese3 {
	public class Program {
		public static void Main(string[] args) {
			try {
				Console.OutputEncoding = Encoding.UTF8;
				var assembly = Assembly.GetExecutingAssembly();
				var version = assembly.GetName().Version!;
				Console.WriteLine($"PoeChinese3 (v{version.Major}.{version.Minor}.{version.Build})  Copyright (C) 2022-2023 aianlinb"); // ©
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
						Modify(ggpk.Index);
						ggpk.Dispose();
						Console.WriteLine("Done!");
						Console.WriteLine("中文化完成！ 再次執行以還原");
						break;
					case ".bin":
						Console.WriteLine("Index path: " + args[0]);
						Console.WriteLine("Reading index file . . .");
						var index = new Index(args[0], false);
						Console.WriteLine("正在套用 (Modifying) . . .");
						Modify(index);
						index.Dispose();
						Console.WriteLine("Done!");
						Console.WriteLine("中文化完成！ 再次執行以還原");
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

		public static unsafe void Modify(Index index) {
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
			(rowTch[1], rowFrn[1]) = (rowFrn[1], rowTch[1]); // swap
			(rowTch[2], rowFrn[2]) = (rowFrn[2], rowTch[2]);

			if (!index.TryGetFile("Art/UIImages1.txt", out var uiImages)) {
				var color = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine("Warning: Cannot find file: Art/UIImages1.txt");
				Console.WriteLine("Warning: The national flag pattern in start menu won't be replaced");
				Console.WriteLine("警告: 找不到 UIImages1.txt，登入畫面的國旗圖案將不會改變");
				Console.ForegroundColor = color;
				lang.Write(dat.Save(false, false)); // also saved the index
				return;
			}
			var memory = uiImages.Read();
			var p = memory.Pin();
			var span = new Span<char>(p.Pointer, memory.Length); // Or memory.Span for Span<byte>
			var span2 = span[span.IndexOf("\"Art/2DArt/UIImages/Common/FlagIcons/")..];
			var fr4K = span2.IndexOf("\"Art/2DArt/UIImages/Common/FlagIcons/4K/fr\"");
			var zhTW4K = span2.IndexOf("\"Art/2DArt/UIImages/Common/FlagIcons/4K/zhTW\"");
			if (fr4K < zhTW4K) {
				span2[(fr4K + 43)..zhTW4K].CopyTo(span2[(fr4K + 45)..]); // move two chars back
				"zhTW\"".CopyTo(span2[(fr4K + 40)..]);
				"\"Art/2DArt/UIImages/Common/FlagIcons/4K/fr\"".CopyTo(span2[(zhTW4K + 2)..]);
			} else {
				span2[(zhTW4K + 45)..fr4K].CopyTo(span2[(zhTW4K + 43)..]); // move two chars forward
				"fr\"".CopyTo(span2[(zhTW4K + 40)..]);
				"\"Art/2DArt/UIImages/Common/FlagIcons/4K/zhTW\"".CopyTo(span2[(fr4K - 2)..]);
			}
			var fr = span2.IndexOf("\"Art/2DArt/UIImages/Common/FlagIcons/fr\"");
			var zhTW = span2.IndexOf("\"Art/2DArt/UIImages/Common/FlagIcons/zhTW\"");
			if (fr < zhTW) {
				span2[(fr + 40)..zhTW].CopyTo(span2[(fr + 42)..]); // move two chars back
				"zhTW\"".CopyTo(span2[(fr + 37)..]);
				"\"Art/2DArt/UIImages/Common/FlagIcons/fr\"".CopyTo(span2[(zhTW + 2)..]);
			} else {
				span2[(zhTW + 42)..fr].CopyTo(span2[(zhTW + 40)..]); // move two chars forward
				"fr\"".CopyTo(span2[(zhTW + 37)..]);
				"\"Art/2DArt/UIImages/Common/FlagIcons/zhTW\"".CopyTo(span2[(fr - 2)..]);
			}

			var br = index.GetSmallestBundle();
			var bundle = br.Bundle;
			var ms = new MemoryStream(bundle.UncompressedSize);
			ms.Write(bundle.ReadData());
			var langDat = dat.Save(false, false);
			lang.Redirect(br, (int)ms.Length, langDat.Length);
			ms.Write(langDat);
			uiImages.Redirect(br, (int)ms.Length, span.Length);
			ms.Write(new(p.Pointer, memory.Length));
			p.Dispose();
			bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
			ms.Close();
			index.Save();
		}
	}
}