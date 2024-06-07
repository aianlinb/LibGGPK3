using System;
using System.Collections.Generic;
using System.Reflection;

using Eto.Drawing;
using Eto.Forms;

namespace VisualGGPK3.TreeItems;
public abstract class DirectoryTreeItem : ITreeItem {
	protected internal static readonly Bitmap DirectoryIcon;
	static DirectoryTreeItem() {
		try {
			using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("VisualGGPK3.Resources.dir.ico");
			DirectoryIcon = new(s);
		} catch {
			DirectoryIcon = null!;
		}
	}
	public virtual string Name { get; }
	/// <summary>
	/// Do not modify this
	/// </summary>
	public virtual string Text { get => Name; set => throw new InvalidOperationException(); }
	public virtual Image Image => DirectoryIcon;

	public abstract DirectoryTreeItem? Parent { get; }
#pragma warning disable CS0618
	protected DirectoryTreeItem(string name, TreeView tree) {
		Name = name;
		Tree = tree;
	}

	public abstract IReadOnlyList<ITreeItem> ChildItems { get; }

	ITreeItem ITreeItem<ITreeItem>.Parent { get => Parent!; set => throw new InvalidOperationException(); }

	public virtual bool Initialized { get; protected internal set; }

	public virtual ITreeItem this[int index] => Initialized ? ChildItems[index] : new TreeItem { Text = "Loading . . .", Parent = this };
	public virtual int Count => Initialized ? ChildItems.Count : 1;

	public virtual bool Expandable => !Initialized || Count > 0;

	protected bool _Expanded;
	public virtual bool Expanded {
		get => _Expanded;
		set {
			_Expanded = value;
			if (value && !Initialized) {
				Initialized = true;
#if !Windows
				Tree.RefreshItem(this);
#endif
			}
		}
	}

	string IListItem.Key => Name + "/";

	public readonly TreeView Tree;

	public abstract int Extract(string path);

	public abstract int Replace(string path);
}