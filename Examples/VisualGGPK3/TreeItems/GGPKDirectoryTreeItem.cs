using Eto;
using Eto.Forms;
using LibGGPK3;
using LibGGPK3.Records;
using System.Collections.ObjectModel;
using System.Linq;
using static LibGGPK3.Records.TreeNode;

namespace VisualGGPK3.TreeItems {
	[ContentProperty("ChildItems")]
	public class GGPKDirectoryTreeItem : DirectoryTreeItem {
		public virtual DirectoryRecord Record { get; }
		public override GGPKDirectoryTreeItem? Parent { get; }
#pragma warning disable CS0618
		protected internal GGPKDirectoryTreeItem(DirectoryRecord record, GGPKDirectoryTreeItem? parent, TreeView tree) : base(record.Name, tree) {
			Record = record;
			Parent = parent;
		}

		protected ReadOnlyCollection<ITreeItem>? _ChildItems;
		public override ReadOnlyCollection<ITreeItem> ChildItems =>
			_ChildItems ??= new(Record.Children.OrderBy(tn => tn, NodeComparer.Instance).Select(
					t => t is FileRecord f ?
					(ITreeItem)new GGPKFileTreeItem(f, this) :
					new GGPKDirectoryTreeItem((DirectoryRecord)t, this, Tree)
				).ToList());

		public override int Extract(string path) {
			return GGPK.Extract(Record, path);
		}

		public override int Replace(string path) {
			return GGPK.Replace(Record, path);
		}
	}
}