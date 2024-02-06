using Eto;
using Eto.Forms;
using LibBundle3.Nodes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Index = LibBundle3.Index;

namespace VisualGGPK3.TreeItems {
	[ContentProperty("ChildItems")]
	public class BundleDirectoryTreeItem : DirectoryTreeItem, IDirectoryNode {
		public override BundleDirectoryTreeItem? Parent { get; }
		IDirectoryNode? ITreeNode.Parent => Parent;
#pragma warning disable CS0618
		protected internal BundleDirectoryTreeItem(string name, BundleDirectoryTreeItem? parent, TreeView tree) : base(name, tree) {
			Parent = parent;
		}

		protected readonly List<ITreeNode> _Children = new();
		public virtual IList<ITreeNode> Children => _Children;

		protected ListWrapper<ITreeItem, ITreeNode>? _ChildItems;
		public override IReadOnlyList<ITreeItem> ChildItems {
			get {
				if (_ChildItems is null) {
					var tmp = new ITreeNode[_Children.Count];
					int j = 0, k = 0;
					for (var i = 0; i < _Children.Count; ++i) {
						if (_Children[i] is IDirectoryNode dn)
							_Children[j++] = dn;
						else
							tmp[k++] = _Children[i];
					}
					tmp.AsSpan()[..k].CopyTo(CollectionsMarshal.AsSpan(_Children)[j..]);
					_ChildItems = new(_Children);
				}
				return _ChildItems;
			}
		}

		public override int Extract(string path) {
			return Index.Extract(this, path);
		}

		public override int Replace(string path) {
			return Index.Replace(this, path);
		}

		protected internal static Index.CreateDirectoryInstance GetFuncCreateInstance(TreeView tree) {
			IDirectoryNode CreateInstance(string name, IDirectoryNode? parent) {
				return new BundleDirectoryTreeItem(name, parent as BundleDirectoryTreeItem, tree);
			}
			return CreateInstance;
		}

		protected class ListWrapper<To, From> : IReadOnlyList<To> where To : class? where From : class? {
			protected readonly IReadOnlyList<From> baseList;
			public ListWrapper(IReadOnlyList<From> list) {
				baseList = list;
			}

			public virtual To this[int index] => (baseList[index] as To)!;

			public virtual int Count => baseList.Count;

			public virtual IEnumerator<To> GetEnumerator() => (IEnumerator<To>)baseList.GetEnumerator();

			IEnumerator IEnumerable.GetEnumerator() => baseList.GetEnumerator();
		}
	}
}