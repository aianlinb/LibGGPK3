using System;
using System.IO;
using System.Reflection;

using Eto.Drawing;
using Eto.Forms;

namespace VisualGGPK3.TreeItems;
public abstract class FileTreeItem : ITreeItem {
	protected internal static readonly Bitmap FileIcon;
	static FileTreeItem() {
		try {
			using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("VisualGGPK3.Resources.file.ico");
			FileIcon = new(s);
		} catch {
			FileIcon = null!;
		}
	}
	public virtual string Name { get; }
	public virtual string Text { get => Name; set => throw new InvalidOperationException(); }
	public virtual Image Image => FileIcon;

	public virtual DirectoryTreeItem Parent { get; }

	protected internal FileTreeItem(string name, DirectoryTreeItem parent) {
		Name = name;
		Parent = parent;
	}

	public abstract ReadOnlyMemory<byte> Read();
	public abstract void Write(ReadOnlySpan<byte> content);

	ITreeItem ITreeItem<ITreeItem>.Parent { get => Parent; set => throw new InvalidOperationException(); }
	ITreeItem IDataStore<ITreeItem>.this[int index] => throw new InvalidOperationException();
	int IDataStore<ITreeItem>.Count => 0;

	public virtual bool Expandable => false;
	public virtual bool Expanded { get; set; }
	string IListItem.Key => Name;

	public enum DataFormat {
		Unknown,
		Text,
		Image,
		DdsImage,
		// Following are not implemented yet
		Dat,
		Sound,
		Video
	}
	public DataFormat Format {
		get {
			var ext = Path.GetExtension(Name).ToLowerInvariant();
			if (ext == ".header")
				return Name.EndsWith(".dds.header", StringComparison.OrdinalIgnoreCase) ? DataFormat.DdsImage : DataFormat.Unknown;
			return ext switch {
				".act" or ".ais" or ".amd" or ".ao" or ".aoc" or ".arl" or ".arm" or ".atlas" or ".cht" or ".clt" or ".config" or ".csd"
					or ".dct" or ".ddt" or ".dgr" or ".dlp" or ".ecf" or ".edp" or ".env" or ".epk" or ".et" or ".ffx" or ".fxgraph" or ".gft"
					or ".gt" or ".h" or ".it" or ".itc" or ".idl" or ".idt" or ".json" or ".mat" or ".mtd" or ".ot" or ".otc" or ".pet"
					or ".red" or ".rs" or ".sm" or ".tgr" or ".tgt" or ".tmo" or ".toy" or ".trl" or ".tsi" or ".tst" or ".ui" or ".xml" // Unicode
				or ".txt" or ".csv" or ".filter" or ".fx" or ".hlsl" or ".mel" or ".properties" or ".slt" => DataFormat.Text, // UTF8
				".dat" or ".dat64" or ".datc" or ".datc64" or ".datl" or ".datl64" or "datlc" or "datlc64" => DataFormat.Dat,
				".dds" => DataFormat.DdsImage,
				".jpg" or ".png" or ".bmp" or ".gif" or ".jpeg" or ".ico" or ".tiff" => DataFormat.Image,
				".ogg" or ".bank" or ".wav" or ".mp3" => DataFormat.Sound,
				".bk2" or ".mp4" => DataFormat.Video,
				_ => DataFormat.Unknown
			};
		}
	}

	public abstract string GetPath();
}