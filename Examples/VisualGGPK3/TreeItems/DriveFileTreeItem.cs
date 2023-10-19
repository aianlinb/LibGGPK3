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
			using (var f = File.Open(Path, new FileStreamOptions() {
				Mode = FileMode.Open,
				Access = FileAccess.Read,
				Share = FileShare.ReadWrite,
				Options = FileOptions.SequentialScan,
				BufferSize = 0
			}))
				if (f.Length > 0) {
					var b = new byte[f.Length];
					f.ReadExactly(b, 0, b.Length);
					return b;
				}
			//return ReadOnlyMemory<byte>.Empty;
			return File.ReadAllBytes(Path); // https://source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/IO/File.cs,658
		}

		public override void Write(ReadOnlySpan<byte> content) {
			using var f = File.Open(Path, new FileStreamOptions() {
				Mode = FileMode.Truncate,
				Access = FileAccess.Write,
				Share = FileShare.ReadWrite,
				Options = FileOptions.WriteThrough | FileOptions.SequentialScan,
				BufferSize = 0
			});
			f.Write(content);
		}
	}
}