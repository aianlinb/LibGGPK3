using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace LibBundle3.Nodes {
	public class DirectoryNode : IDirectoryNode, IReadOnlyList<ITreeNode> {
		public virtual string Name { get; }
		public virtual DirectoryNode? Parent { get; }
		IDirectoryNode? ITreeNode.Parent => Parent;
		protected List<ITreeNode>? _Children;
		public virtual IList<ITreeNode> Children => _Children ??= new();

		protected DirectoryNode(string name, DirectoryNode? parent) {
			Name = name;
			Parent = parent;
		}

		/// <summary>
		/// Call <see cref="this[string]"/>
		/// </summary>
		public virtual bool TryGetChild(string name, [NotNullWhen(true)] out ITreeNode? child) {
			return (child = this[name]) != null;
		}

		public virtual int Count => Children.Count;
		public virtual ITreeNode this[int index] => Children[index];
		public virtual ITreeNode? this[string name] => Children.FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
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