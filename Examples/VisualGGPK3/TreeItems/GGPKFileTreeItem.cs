using System;

using LibGGPK3.Records;

namespace VisualGGPK3.TreeItems;
public class GGPKFileTreeItem : FileTreeItem {
	public virtual FileRecord Record { get; }
	public new GGPKDirectoryTreeItem Parent => (GGPKDirectoryTreeItem)base.Parent;

	protected internal GGPKFileTreeItem(FileRecord record, GGPKDirectoryTreeItem parent) : base(record.Name, parent) {
		Record = record;
	}

	public override ReadOnlyMemory<byte> Read() {
		return Record.Read();
	}

	public override void Write(ReadOnlySpan<byte> content) {
		Record.Write(content);
	}

	public override string GetPath() => Record.GetPath();
}