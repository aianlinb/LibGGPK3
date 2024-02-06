using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace LibBundle3.Nodes {
	public class DirectoryNode : IDirectoryNode, IReadOnlyList<ITreeNode> {
		public virtual string Name { get; }
		public virtual DirectoryNode? Parent { get; }
		IDirectoryNode? ITreeNode.Parent => Parent;
		protected List<ITreeNode>? _Children;
		public virtual IList<ITreeNode> Children => _Children ??= [];

		protected DirectoryNode(string name, DirectoryNode? parent) {
			Name = name;
			Parent = parent;
		}

		public virtual int Count => Children.Count;
		public virtual ITreeNode this[int index] => Children[index];
		public virtual ITreeNode? this[ReadOnlySpan<char> name] {
			get {
				foreach (var child in Children)
					if (child.Name.AsSpan().SequenceEqual(name))
						return child;
				return null;
			}
		}
		public virtual IEnumerator<ITreeNode> GetEnumerator() {
			return Children.GetEnumerator();
		}
		IEnumerator IEnumerable.GetEnumerator() {
			return GetEnumerator();
		}

		/// <summary>
		/// See <see cref="Index.BuildTree"/>
		/// </summary>
		protected internal static IDirectoryNode CreateInstance(string name, IDirectoryNode? parent) {
			return new DirectoryNode(name, parent as DirectoryNode);
		}
	}
}