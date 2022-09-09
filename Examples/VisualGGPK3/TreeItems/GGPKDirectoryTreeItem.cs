using Eto.Forms;
using LibGGPK3.Records;
using System.Collections.ObjectModel;
using System.Linq;

namespace VisualGGPK3.TreeItems {
	public class GGPKDirectoryTreeItem : DirectoryTreeItem {
		public virtual DirectoryRecord Record { get; }
		public override GGPKDirectoryTreeItem? Parent { get; }
#pragma warning disable CS0618
		protected internal GGPKDirectoryTreeItem(DirectoryRecord record, GGPKDirectoryTreeItem? parent, TreeView tree) : base(record.Name, tree) {
			Record = record;
			Parent = parent;
		}

		protected ReadOnlyCollection<ITreeItem>? _Children;
		public override ReadOnlyCollection<ITreeItem> Children {
			get {
				_Children ??= new(Record.Children.Select(
					t => t is FileRecord f ?
					(ITreeItem)new GGPKFileTreeItem(f, this) :
					new GGPKDirectoryTreeItem((DirectoryRecord)t, this, Tree)
				).OrderBy(t => t, TreeComparer.Instance).ToList());
				return _Children;
			}
		}
	}
}