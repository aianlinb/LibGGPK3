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
			using var f = File.OpenHandle(Path, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.None, content.Length);
			RandomAccess.Write(f, content, 0);
		}
	}
}