using System;
using System.Collections.ObjectModel;
using System.Linq;

using Eto;
using Eto.Forms;

using LibGGPK3;
using LibGGPK3.Records;

namespace VisualGGPK3.TreeItems;
[ContentProperty("ChildItems")]
public class GGPKDirectoryTreeItem : DirectoryTreeItem {
	public virtual DirectoryRecord Record { get; }
	public override GGPKDirectoryTreeItem? Parent { get; }
#pragma warning disable CS0618
	protected internal GGPKDirectoryTreeItem(DirectoryRecord record, GGPKDirectoryTreeItem? parent, TreeView tree) : base(record.Name, tree) {
		Record = record;
		Parent = parent;
	}

	protected internal ReadOnlyCollection<ITreeItem>? _ChildItems;
	public override ReadOnlyCollection<ITreeItem> ChildItems =>
		_ChildItems ??= new(Record.OrderBy(tn => tn, TreeNode.NodeComparer.Instance).Select(
				t => t is FileRecord f ?
				(ITreeItem)new GGPKFileTreeItem(f, this) :
				new GGPKDirectoryTreeItem((DirectoryRecord)t, this, Tree)
			).ToList());

	public override int Extract(string path) {
		return GGPK.Extract(Record, path); // TODO: Progress
	}

	public override int Extract(Action<string, ReadOnlyMemory<byte>> callback, string endsWith = "") {
		endsWith = endsWith.ToLowerInvariant();
		var count = 0;
		foreach (var (fr, path) in TreeNode.RecurseFiles(Record).AsParallel()) {
			if (!path.EndsWith(endsWith, StringComparison.Ordinal))
				continue;
			callback(path, fr.Read());
			++count;
		}
		return count;
	}

	public override int Replace(string path) {
		return GGPK.Replace(Record, path); // TODO: Progress
	}

	public override string GetPath() => Record.GetPath();
}