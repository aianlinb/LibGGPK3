using System;
using System.Buffers;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

using LibBundledGGPK3;

using LibDat2;
using SystemExtensions;

using SystemExtensions.Spans;

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
		Console.WriteLine($"流亡黯道 - 啟用繁體中文語系  By aianlinb");
		Console.WriteLine();

		using (var definitions = assembly.GetManifestResourceStream("PoeChinese3.DatDefinitions.json")!)
			DatContainer.ReloadDefinitions(definitions);

		string? path;
		if (args.Length == 0) {
			Console.WriteLine($"請輸入檔案路徑");
			Console.Write("Path to Content.ggpk (_.index.bin for Steam/Epic): ");
			path = Console.ReadLine()!.Trim();
			if (path.Length > 1 && path[0] == '"' && path[^1] == '"')
				path = path[1..^1].Trim();
			Console.WriteLine();
		} else
			path = args[0].Trim();
		if (!File.Exists(Utils.ExpandPath(path))) {
			Console.WriteLine("檔案不存在 (File not found): " + path);
			Console.WriteLine();
			Console.WriteLine("Enter to exit . . .");
			Console.ReadLine();
			return;
		}

		switch (Path.GetExtension(path).ToLowerInvariant()) {
			case ".ggpk":
				Console.WriteLine("GGPK path: " + path);
				Console.WriteLine("Reading ggpk file . . .");
				using (var ggpk = new BundledGGPK(path, false))
					Run(ggpk.Index);
				break;
			case ".bin":
				Console.WriteLine("Index path: " + path);
				Console.WriteLine("Reading index file . . .");
				using (var index = new Index(path, false))
					Run(index);
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

	public static unsafe void Run(Index index) {
		ApplyTraditionalChinese(index);

		Environment.CurrentDirectory = AppContext.BaseDirectory;
		if (File.Exists("Font.ttc"))
			ApplyFont(index, Path.GetFullPath("Font.ttc"));
		else if (File.Exists("Font.ttf"))
			ApplyFont(index, Path.GetFullPath("Font.ttf"));
		
		// Taiwan flag
		ApplyNationalFlag(index);

		index.Save();
		Console.WriteLine("Done!");
		Console.WriteLine("中文化完成！");
	}

	public static unsafe void ApplyTraditionalChinese(Index index) {
		Console.WriteLine("Reading Languages.dat . . .");
		if (!index.TryGetFile("Data/Languages.dat", out var lang))
			throw new FileNotFoundException("Cannot find file: \"Data/Languages.dat\" in ggpk/index");
		var datMmemory = lang.Read();
		DatContainer dat;
		fixed (byte* p = datMmemory.Span)
			using (var ms = new UnmanagedMemoryStream(p, datMmemory.Length))
				dat = new DatContainer(ms, "Languages.dat"); // TODO: LibDat3 rewrite

		// Traditional Chinese applying
		Console.WriteLine("Applying Traditional Chinese . . .");
		int frn = -1, tch = -1;
		for (var i = 0; i < dat.FieldDatas.Count; ++i) {
			var s = (string)dat.FieldDatas[i][1].Value;
			if (s == "French")
				frn = i; // 1
			else if (s == "Traditional Chinese") {
				if ((string)dat.FieldDatas[i][3].Value == "fr") // already applied
					return;
				tch = i; // 6
			}
		}

		if (frn == -1 || tch == -1) {
			var color = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("Warning: Cannot find French or Traditional Chinese in Languages.dat, skip applying . . .");
			Console.WriteLine("警告: 找不到法文或繁體中文語系，將跳過語系套用");
			Console.ForegroundColor = color;
			return;
		}
		var rowFrn = dat.FieldDatas[frn];
		var rowTch = dat.FieldDatas[tch];
		(rowTch[1], rowFrn[1]) = (rowFrn[1], rowTch[1]); // swap
		(rowTch[2], rowFrn[2]) = (rowFrn[2], rowTch[2]);
		lang.Write(dat.Save(false, false));
	}

	public static void ApplyFont(Index index, string fontFilePath) {
		Console.WriteLine($"Applying font: {fontFilePath} . . .");
		if (!index.TryGetFile("Art/2DArt/Fonts/Koruri-Regular.ttf", out var font)) {
			var color = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("Warning: Cannot find file: Art/2DArt/Fonts/Koruri-Regular.ttf");
			Console.WriteLine("The font won't be replaced");
			Console.WriteLine("警告: GGPK/Index中找不到 Art/2DArt/Fonts/Koruri-Regular.ttf，字型將不會套用");
			Console.ForegroundColor = color;
			return;
		}
		if (!index.TryGetFile("Metadata/UI/UISettings.Traditional Chinese.xml", out var xml)) {
			var color = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("Warning: Cannot find file: Metadata/UI/UISettings.Traditional Chinese.xml");
			Console.WriteLine("The font won't be replaced");
			Console.WriteLine("警告: GGPK/Index中找不到 Metadata/UI/UISettings.Traditional Chinese.xml，字型將不會套用");
			Console.ForegroundColor = color;
			return;
		}

		var b = File.ReadAllBytes(fontFilePath);
		if (font.Size != b.Length || !font.Read().Span.SequenceEqual(b)) // not yet applied
			font.Write(b);

		var str = MemoryMarshal.Cast<byte, char>(xml.Read().Span);
		var i = str.IndexOf("\">\r\n") + 4;
		if (str[i..].StartsWith("\t<!-- Added by aianlinb -->"))
			return; // already applied
		const string data = "\t<!-- Added by aianlinb -->\r\n"
			+ "\t<InstalledFont id=\"Custom\" typeface=\"Custom\" value=\"Art/2DArt/Fonts/Koruri-Regular.ttf\"/>\r\n";
		
		var result = $"{str[..i]}{data}{str[i..]}".Replace("Microsoft JhengHei", "Custom");
		xml.Write(MemoryMarshal.AsBytes(result.AsSpan()));
	}

	public static void ApplyNationalFlag(Index index) {
		Console.WriteLine("Changing national flag . . .");
		if (!index.TryGetFile("Art/UIImages1.txt", out var uiImages)) {
			var color = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("Warning: Cannot find file: Art/UIImages1.txt in ggpk/index");
			Console.WriteLine("The national flag pattern in start menu won't be replaced");
			Console.WriteLine("警告: GGPK/Index中找不到 Art/UIImages1.txt，登入畫面的國旗圖案將不會改變");
			Console.ForegroundColor = color;
			return;
		}

		// Above line may modfiy the cached data directly, but this program won't use it anymore, so it's fine
		var span = MemoryMarshal.Cast<byte, char>(MemoryMarshal.AsMemory(uiImages.Read()).Span);
		var span2 = span[span.IndexOf("Common/FlagIcons/")..];
		var fr4K = span2.IndexOf("Common/FlagIcons/4K/fr\"");
		var zhTW4K = span2.IndexOf("Common/FlagIcons/4K/zhTW\"");
		if (fr4K < zhTW4K) {
			span2[(fr4K + 23)..(zhTW4K + 20)].CopyTo(span2[(fr4K + 25)..]); // move two chars back
			"zhTW\"".CopyTo(span2[(fr4K + 20)..]);
			"fr\"".CopyTo(span2[(zhTW4K + 22)..]);
		} else {
			return; // already applied
			/*span2[(zhTW4K + 25)..(fr4K + 20)].CopyTo(span2[(zhTW4K + 23)..]); // move two chars forward
			"fr\"".CopyTo(span2[(zhTW4K + 20)..]);
			"zhTW\"".CopyTo(span2[(fr4K + 18)..]);*/
		}
		var fr = span2.IndexOf("Common/FlagIcons/fr\"");
		var zhTW = span2.IndexOf("Common/FlagIcons/zhTW\"");
		if (fr < zhTW) {
			span2[(fr + 20)..(zhTW + 17)].CopyTo(span2[(fr + 22)..]); // move two chars back
			"zhTW\"".CopyTo(span2[(fr + 17)..]);
			"fr\"".CopyTo(span2[(zhTW + 19)..]);
		} else {
			return; // already applied
			/*span2[(zhTW + 22)..(fr + 17)].CopyTo(span2[(zhTW + 20)..]); // move two chars forward
			"fr\"".CopyTo(span2[(zhTW + 17)..]);
			"zhTW\"".CopyTo(span2[(fr + 15)..]);*/
		}

		uiImages.Write(MemoryMarshal.AsBytes(span));
	}
}