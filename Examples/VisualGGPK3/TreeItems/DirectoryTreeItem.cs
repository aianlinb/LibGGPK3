using System;
using System.Collections.Generic;
using System.Reflection;

using Eto.Drawing;
using Eto.Forms;

using SystemExtensions.Collections;

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

	public abstract int Extract(Action<string, ReadOnlyMemory<byte>> callback, string endsWith = "");

	public abstract int Replace(string path);

	public abstract string GetPath();

	public static string GetPath(ITreeItem node) {
		var builder = new ValueList<char>(stackalloc char[128]);
		using (builder) {
#pragma warning disable CS0728
            GetPath(ref builder, node);
#pragma warning restore CS0728
            return new(builder.AsReadOnlySpan());
		}
	}

	private static void GetPath(ref ValueList<char> builder, ITreeItem node) {
		if (node.Parent is null) // Root
			return;
		GetPath(ref builder, node.Parent);
		builder.AddRange(node.Text.AsSpan());
		if (node is DirectoryTreeItem)
			builder.Add('/');
	}
}