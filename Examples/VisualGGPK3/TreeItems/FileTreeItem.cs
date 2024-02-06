using Eto.Drawing;
using Eto.Forms;
using System;
using System.IO;
using System.Reflection;

namespace VisualGGPK3.TreeItems {
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
			Dat,
			Image,
			Dds,
			OGG
		}
		public DataFormat Format => Path.GetExtension(Name).ToLowerInvariant() switch {
			".act" or ".ais" or ".amd" or ".ao" or ".aoc" or ".arl" or ".arm" or ".atlas" or ".cht" or ".clt" or ".dct" or ".ddt" or ".dgr" or ".dlp" or ".ecf" or ".edp" or ".env" or ".epk" or ".et" or ".ffx" or ".fxgraph" or ".gft" or ".gt" or ".idl" or ".idt" or ".json" or ".mat" or ".mtd" or ".ot" or ".otc" or ".pet" or ".red" or ".rs" or ".sm" or ".tgr" or ".tgt" or ".trl" or ".tsi" or ".tst" or ".ui" or ".xml" // Unicode
			or ".txt" or ".csv" or ".filter" or ".fx" or ".hlsl" or ".mel" or ".properties" or ".slt" => DataFormat.Text, // UTF8
			".dat" or ".dat64" or ".datl" or ".datl64" => DataFormat.Dat,
			".dds" or ".header" => DataFormat.Dds,
			".jpg" or ".png" or ".bmp" or ".gif" or ".jpeg" or ".ico" or ".tiff" => DataFormat.Image,
			".ogg" or ".ogv" or ".oga" or ".spx" or ".ogx" => DataFormat.OGG,
			_ => DataFormat.Unknown
		};
	}
}