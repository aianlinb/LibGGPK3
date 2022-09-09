using Eto.Drawing;
using Eto.Forms;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace VisualGGPK3.TreeItems {
	public abstract class DirectoryTreeItem : ITreeItem {
		protected internal static readonly Bitmap DirectoryIcon = new(Assembly.GetExecutingAssembly().GetManifestResourceStream("VisualGGPK3.Resources.dir.ico"));
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

		public abstract IReadOnlyList<ITreeItem> Children { get; }

		ITreeItem ITreeItem<ITreeItem>.Parent { get => Parent!; set => throw new InvalidOperationException(); }

		public virtual bool Initialized { get; protected internal set; }

		public virtual ITreeItem this[int index] => Initialized ? Children[index] : new TreeItem { Text = "Loading . . .", Parent = this };
		public virtual int Count => Initialized ? Children.Count : 1;
		
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

		/// <summary>
		/// Use to sort the children of directory.
		/// </summary>
		public sealed class TreeComparer : IComparer<ITreeItem> {
			public static readonly TreeComparer Instance = new();
			/*
			[System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
			private static extern int StrCmpLogicalW(string x, string y);  // Too slow
			*/
			private TreeComparer() { }

#pragma warning disable CS8767
			public int Compare(ITreeItem x, ITreeItem y) {
				if (x is DirectoryTreeItem) {
					if (y is DirectoryTreeItem)
						return x.Text.CompareTo(y.Text);
					else
						return -1;
				} else {
					if (y is DirectoryTreeItem)
						return 1;
					else
						return x.Text.CompareTo(y.Text);
				}
			}
		}
	}
}