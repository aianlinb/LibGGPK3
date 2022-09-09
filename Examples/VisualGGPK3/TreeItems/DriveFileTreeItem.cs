using System;
using System.IO;

namespace VisualGGPK3.TreeItems {
	public class DriveFileTreeItem : FileTreeItem {
		public virtual string Path { get; }
		public new DriveDirectoryTreeItem Parent => (DriveDirectoryTreeItem)base.Parent;

		protected internal DriveFileTreeItem(string path, DriveDirectoryTreeItem parent) : base(System.IO.Path.GetFileName(path), parent) {
			Path = path;
		}

		public override ReadOnlyMemory<byte> Read() {
			return File.ReadAllBytes(Path);
		}

		public override void Write(ReadOnlySpan<byte> content) {
			var f = File.Create(Path);
			f.Write(content);
			f.Close();
		}
	}
}