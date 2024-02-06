using Eto;
using Eto.Forms;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace VisualGGPK3.TreeItems {
	[ContentProperty("ChildItems")]
	public class DriveDirectoryTreeItem : DirectoryTreeItem {
		public virtual string Path { get; }
		public override DriveDirectoryTreeItem? Parent { get; }
#pragma warning disable CS0618
		protected internal DriveDirectoryTreeItem(string path, DriveDirectoryTreeItem? parent, TreeView tree) : base(path, tree) {
			Path = path;
			Parent = parent;
		}

		protected ReadOnlyCollection<ITreeItem>? _ChildItems;
		public override ReadOnlyCollection<ITreeItem> ChildItems {
			get {
				if (_ChildItems is null) {			// Already sorted
					var list = Directory.EnumerateDirectories(Path)/*.OrderBy(p => p)*/.Select(p => (ITreeItem)new DriveDirectoryTreeItem(p, this, Tree)).ToList();
					list.AddRange(Directory.EnumerateFiles(Path)/*.OrderBy(p => p)*/.Select(p => (ITreeItem)new DriveFileTreeItem(p, this)));
					list.TrimExcess();
					_ChildItems = new(list);
				}
				return _ChildItems;
			}
		}

		public override int Extract(string path) {
			Directory.CreateDirectory(path);
			var count = 0;
			foreach (var f in ChildItems) {
				if (f is DriveDirectoryTreeItem di) {
					count += di.Extract($"{path}/{di.Name}");
				} else if (f is DriveFileTreeItem fi) {
					File.Copy(fi.Path, $"{path}/{fi.Name}");
					++count;
				} else
					throw new InvalidCastException("Unexpected type: " + f.GetType().ToString());
			}
			return count;
		}

		public override int Replace(string path) {
			var count = 0;
			foreach (var f in ChildItems) {
				if (f is DriveDirectoryTreeItem di) {
					count += di.Replace($"{path}/{di.Name}");
				} else if (f is DriveFileTreeItem fi) {
					path = $"{path}/{fi.Name}";
					if (File.Exists(path)) {
						File.Copy(path, fi.Path);
						++count;
					}
				} else
					throw new InvalidCastException("Unexpected type: " + f.GetType().ToString());
			}
			return count;
		}
	}
}