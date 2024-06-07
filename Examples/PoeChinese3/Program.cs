using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

using LibBundle3.Records;

using LibBundledGGPK3;

using LibDat2;

using Index = LibBundle3.Index;

namespace PoeChinese3;
public static class Program {
	public static void Main(string[] args) {
#if !DEBUG
		try {
#endif
		Console.OutputEncoding = Encoding.UTF8;
		var assembly = Assembly.GetExecutingAssembly();
		var version = assembly.GetName().Version!;
		if (version.Revision != 0)
			Console.WriteLine($"PoeChinese3 (v{version.Major}.{version.Minor}.{version.Build}.{version.Revision})  Copyright (C) 2022-2024 aianlinb"); // ©
		else
			Console.WriteLine($"PoeChinese3 (v{version.Major}.{version.Minor}.{version.Build})  Copyright (C) 2022-2024 aianlinb"); // ©
		Console.WriteLine($"流亡黯道 - 啟/禁用繁體中文語系  By aianlinb");
		Console.WriteLine();

		using (var definitions = assembly.GetManifestResourceStream("PoeChinese3.DatDefinitions.json")!)
			DatContainer.ReloadDefinitions(definitions);

		string? path;
		if (args.Length == 0) {
			Console.WriteLine($"請輸入檔案路徑");
			Console.Write("Path to Content.ggpk (_.index.bin for Steam/Epic): ");
			path = Console.ReadLine()?.Trim();
			if (path?.Length > 1 && path[0] == '"' && path[^1] == '"')
				path = path[1..^1].Trim();
			Console.WriteLine();
		} else
			path = args[0].Trim();
		if (!File.Exists(path)) {
			Console.WriteLine("檔案不存在 (File not found): " + path);
			Console.WriteLine();
			Console.WriteLine("Enter to exit . . .");
			Console.ReadLine();
			return;
		}
		path = Path.GetFullPath(path);

		switch (Path.GetExtension(path).ToLowerInvariant()) {
			case ".ggpk":
				Console.WriteLine("GGPK path: " + path);
				Console.WriteLine("Reading ggpk file . . .");
				using (var ggpk = new BundledGGPK(path, false)) {
					Console.WriteLine("正在套用 (Modifying) . . .");
					Modify(ggpk.Index);
				}
				Console.WriteLine("Done!");
				Console.WriteLine("中文化完成！ 再次執行以還原");
				break;
			case ".bin":
				Console.WriteLine("Index path: " + path);
				Console.WriteLine("Reading index file . . .");
				using (var index = new Index(path, false)) {
					Console.WriteLine("正在套用 (Modifying) . . .");
					Modify(index);
				}
				Console.WriteLine("Done!");
				Console.WriteLine("中文化完成！ 再次執行以還原");
				break;
			default:
				Console.WriteLine("Unknown file extension: " + Path.GetFileName(path));
				break;
		}
#if !DEBUG
		} catch (Exception e) {
			var color = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.WriteLine(e);
			Console.ForegroundColor = color;
		}
#endif
		Console.WriteLine();
		Console.WriteLine("Enter to exit . . .");
		Console.ReadLine();
	}

	public static unsafe void Modify(Index index) {
		if (!index.TryGetFile("Data/Languages.dat", out var lang))
			throw new FileNotFoundException("Cannot find file: \"Data/Languages.dat\" in ggpk/index");
		var datMmemory = lang.Read();
		DatContainer dat;
		fixed (byte* p = datMmemory.Span)
			using (var ms = new UnmanagedMemoryStream(p, datMmemory.Length))
				dat = new DatContainer(ms, "Languages.dat"); // TODO: LibDat3 rewrite

		// Traditional Chinese applying
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
		var data = dat.Save(false, false);

		if (!index.TryGetFile("Art/UIImages1.txt", out var uiImages)) {
			var color = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("Warning: Cannot find file: Art/UIImages1.txt");
			Console.WriteLine("The national flag pattern in start menu won't be replaced");
			Console.WriteLine("警告: 找不到 Art/UIImages1.txt，登入畫面的國旗圖案將不會改變");
			Console.ForegroundColor = color;
			lang.Write(data); // also saved the index
			return;
		}

		// National flag changing
		var memory = uiImages.Read();
		// memory.Span will return ReadOnlySpan<byte>, so use memory.Pin() instead for modifying
		// This may modfiy the cached data directly, but this program won't use it anymore, so it's fine
		using (var p = memory.Pin()) {
			var span = new Span<char>(p.Pointer, memory.Length);
			var span2 = span[span.IndexOf("Common/FlagIcons/")..];
			var fr4K = span2.IndexOf("Common/FlagIcons/4K/fr\"");
			var zhTW4K = span2.IndexOf("Common/FlagIcons/4K/zhTW\"");
			if (fr4K < zhTW4K) {
				span2[(fr4K + 23)..(zhTW4K + 20)].CopyTo(span2[(fr4K + 25)..]); // move two chars back
				"zhTW\"".CopyTo(span2[(fr4K + 20)..]);
				"fr\"".CopyTo(span2[(zhTW4K + 22)..]);
			} else {
				span2[(zhTW4K + 25)..(fr4K + 20)].CopyTo(span2[(zhTW4K + 23)..]); // move two chars forward
				"fr\"".CopyTo(span2[(zhTW4K + 20)..]);
				"zhTW\"".CopyTo(span2[(fr4K + 18)..]);
			}
			var fr = span2.IndexOf("Common/FlagIcons/fr\"");
			var zhTW = span2.IndexOf("Common/FlagIcons/zhTW\"");
			if (fr < zhTW) {
				span2[(fr + 20)..(zhTW + 17)].CopyTo(span2[(fr + 22)..]); // move two chars back
				"zhTW\"".CopyTo(span2[(fr + 17)..]);
				"fr\"".CopyTo(span2[(zhTW + 19)..]);
			} else {
				span2[(zhTW + 22)..(fr + 17)].CopyTo(span2[(zhTW + 20)..]); // move two chars forward
				"fr\"".CopyTo(span2[(zhTW + 17)..]);
				"zhTW\"".CopyTo(span2[(fr + 15)..]);
			}
		}

		// Save
		Index.Replace([lang, uiImages], (FileRecord fr, int _, out ReadOnlySpan<byte> content) => {
			if (fr == lang)
				content = data;
			else if (fr == uiImages)
				content = memory.Span;
			else
				throw new UnreachableException();
			return true;
		});
	}
}