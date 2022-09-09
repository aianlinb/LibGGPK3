using Eto;
using Eto.Forms;
using LibBundle3.Nodes;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using static LibBundle3.Index;

namespace VisualGGPK3.TreeItems {
	[ContentProperty("_Children")]
	public class BundleDirectoryTreeItem : DirectoryTreeItem, IDirectoryNode {
		public override BundleDirectoryTreeItem? Parent { get; }
		ITreeNode? ITreeNode.Parent => Parent;
#pragma warning disable CS0618
		protected internal BundleDirectoryTreeItem(string name, BundleDirectoryTreeItem? parent, TreeView tree) : base(name, tree) {
			Parent = parent;
		}

		protected readonly List<ITreeItem> _Children = new();
		protected ReadOnlyCollection<ITreeItem>? _ReadOnlyChildren;
		public override ReadOnlyCollection<ITreeItem> Children {
			get {
				if (_ReadOnlyChildren == null) {
					_ListWrapper = null;
					_Children.Sort(static (x, y) => {
						if (x is DirectoryTreeItem) {
							if (y is DirectoryTreeItem)
								return 0;
							return -1;
						} else {
							if (y is DirectoryTreeItem)
								return 1;
							return 0;
						}
					});
					_ReadOnlyChildren = new(_Children);
				}
				return _ReadOnlyChildren;
			}
		}

		private ListWrapper<ITreeItem, ITreeNode>? _ListWrapper;
		IList<ITreeNode> IDirectoryNode.Children => _ListWrapper ??= new ListWrapper<ITreeItem, ITreeNode>(_Children);

		protected internal static CreateDirectoryInstance GetFuncCreateInstance(TreeView tree) {
			IDirectoryNode CreateInstance(string name, IDirectoryNode? parent) {
				return new BundleDirectoryTreeItem(name, parent as BundleDirectoryTreeItem, tree);
			}
			return CreateInstance;
		}

		protected class ListWrapper<T, TResult> : IList<TResult> where T : class? where TResult : class? {
			protected IList<T> list;

			public ListWrapper(IList<T> list) {
				this.list = list;
			}

			public int IndexOf(TResult item) {
				return list.IndexOf((item as T)!);
			}

			public void Insert(int index, TResult item) {
				list.Insert(index, (item as T)!);
			}

			public void RemoveAt(int index) {
				list.RemoveAt(index);
			}

			public TResult this[int index] { get => (list[index] as TResult)!; set => list[index] = (value as T)!; }

			public void Add(TResult item) {
				list.Add((item as T)!);
			}

			public void Clear() {
				list.Clear();
			}

			public bool Contains(TResult item) {
				return list.Contains((item as T)!);
			}

			public void CopyTo(TResult[] array, int arrayIndex) {
				list.Cast<TResult>().ToList().CopyTo(array, arrayIndex);
			}

			public bool Remove(TResult item) {
				return list.Remove((item as T)!);
			}

			public int Count => list.Count;

			public bool IsReadOnly => list.IsReadOnly;

			public IEnumerator<TResult> GetEnumerator() {
				return list.Cast<TResult>().GetEnumerator();
			}

			IEnumerator IEnumerable.GetEnumerator() {
				return ((IEnumerable)list).GetEnumerator();
			}
		}
	}
}