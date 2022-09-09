using LibBundle3.Nodes;
using LibBundle3.Records;
using System;
using System.IO;

namespace VisualGGPK3.TreeItems {
	public class BundleFileTreeItem : FileTreeItem, IFileNode {
		public FileRecord Record { get; }
		public new BundleDirectoryTreeItem Parent => (BundleDirectoryTreeItem)base.Parent;
		ITreeNode ITreeNode.Parent => (ITreeNode)base.Parent;

		protected internal BundleFileTreeItem(FileRecord record, BundleDirectoryTreeItem parent) : base(Path.GetFileName(record.Path), parent) {
			Record = record;
		}

		public override ReadOnlyMemory<byte> Read() {
			return Record.Read();
		}

		public override void Write(ReadOnlySpan<byte> content) {
			Record.Write(content);
		}

		protected internal static IFileNode CreateInstance(FileRecord record, IDirectoryNode parent) {
			return new BundleFileTreeItem(record, (BundleDirectoryTreeItem)parent);
		}
	}
}