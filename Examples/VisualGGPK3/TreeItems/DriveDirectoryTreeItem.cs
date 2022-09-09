using Eto.Forms;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace VisualGGPK3.TreeItems {
	public class DriveDirectoryTreeItem : DirectoryTreeItem {
		public virtual string Path { get; }
		public override DriveDirectoryTreeItem? Parent { get; }
#pragma warning disable CS0618
		protected internal DriveDirectoryTreeItem(string path, DriveDirectoryTreeItem? parent, TreeView tree) : base(path, tree) {
			Path = path;
			Parent = parent;
		}

		protected ReadOnlyCollection<ITreeItem>? _Children;
		public override ReadOnlyCollection<ITreeItem> Children {
			get {
				if (_Children == null) {
					var list = Directory.EnumerateDirectories(Path).OrderBy(p => p).Select(p => (ITreeItem)new DriveDirectoryTreeItem(p, this, Tree)).ToList();
					list.AddRange(Directory.EnumerateFiles(Path).OrderBy(p => p).Select(p => (ITreeItem)new DriveFileTreeItem(p, this)));
					_Children = new(list);
				}
				return _Children;
			}
		}

		public int Extract(string path) {
			Directory.CreateDirectory(path);
			path += "\\" + Name;
			var count = 0;
			foreach (var f in Children) {
				if (f is DriveDirectoryTreeItem di) {
					count += di.Extract(path);
				} else if (f is DriveFileTreeItem fi) {
					File.Copy(fi.Path, path);
					++count;
				} else
					throw new("Unexpected type: " + f.GetType().ToString());
			}
			return count;
		}

		public int Replace(string path) {
			path += "\\" + Name;
			var count = 0;
			foreach (var f in Children) {
				if (f is DriveDirectoryTreeItem di) {
					count += di.Replace(path);
				} else if (f is DriveFileTreeItem fi) {
					if (File.Exists(path)) {
						File.Copy(path, fi.Path);
						++count;
					}
				} else
					throw new("Unexpected type: " + f.GetType().ToString());
			}
			return count;
		}
	}
}