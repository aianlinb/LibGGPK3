using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Eto;
using Eto.Forms;

using LibBundle3.Nodes;

using Index = LibBundle3.Index;

namespace VisualGGPK3.TreeItems;
[ContentProperty("ChildItems")]
public class BundleDirectoryTreeItem : DirectoryTreeItem, IDirectoryNode {
	public override BundleDirectoryTreeItem? Parent { get; }
	IDirectoryNode? ITreeNode.Parent => Parent;
#pragma warning disable CS0618
	protected internal BundleDirectoryTreeItem(string name, BundleDirectoryTreeItem? parent, TreeView tree) : base(name, tree) {
		Parent = parent;
	}

	public virtual List<ITreeNode> Children { get; } = [];

	protected ListWrapper<ITreeItem, ITreeNode>? _ChildItems;
	public override IReadOnlyList<ITreeItem> ChildItems {
		get {
			if (_ChildItems is null) {
				var tmp = new ITreeNode[Children.Count];
				int j = 0, k = 0;
				for (var i = 0; i < Children.Count; ++i) {
					if (Children[i] is IDirectoryNode dn)
						Children[j++] = dn;
					else
						tmp[k++] = Children[i];
				}
				tmp.AsSpan()[..k].CopyTo(CollectionsMarshal.AsSpan(Children)[j..]);
				_ChildItems = new(Children);
			}
			return _ChildItems;
		}
	}

	public override int Extract(string path) { // TODO: Progress
		return Index.ExtractParallel(this, path);
	}

	public override int Extract(Action<string, ReadOnlyMemory<byte>> callback, string endsWith = "") { // TODO: Progress
		endsWith = endsWith.ToLowerInvariant();
		var basePath = GetPath().Length;
		return Index.ExtractParallel(Index.Recursefiles(this).Select(fn => fn.Record)
			.Where(fr => fr.Path.EndsWith(endsWith, StringComparison.Ordinal)), (fr, data) => {
			if (data.HasValue)
				callback(fr.Path[basePath..], data.Value);
			return false;
		});
	}

	public override int Replace(string path) { // TODO: Progress
		return Index.Replace(this, path);
	}

	public override string GetPath() => ITreeNode.GetPath(this);

	protected internal static Index.CreateDirectoryInstance GetFuncCreateInstance(TreeView tree) {
		IDirectoryNode CreateInstance(string name, IDirectoryNode? parent) {
			return new BundleDirectoryTreeItem(name, parent as BundleDirectoryTreeItem, tree);
		}
		return CreateInstance;
	}

	protected class ListWrapper<To, From>(IReadOnlyList<From> list) : IReadOnlyList<To> where To : class? where From : class? {
		protected readonly IReadOnlyList<From> baseList = list;

		public virtual To this[int index] => (baseList[index] as To)!;

		public virtual int Count => baseList.Count;

		public virtual IEnumerator<To> GetEnumerator() => (IEnumerator<To>)baseList.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => baseList.GetEnumerator();
	}
}