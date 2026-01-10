using System;
using System.Collections.Generic;

namespace LibBundle3.Nodes;
public class DirectoryNode : IDirectoryNode {
	public virtual string Name { get; }
	public virtual DirectoryNode? Parent { get; }
	IDirectoryNode? ITreeNode.Parent => Parent;
	public virtual List<ITreeNode> Children { get; } = [];

	protected DirectoryNode(string name, DirectoryNode? parent) {
		Name = name;
		Parent = parent;
	}

	public virtual ITreeNode? this[ReadOnlySpan<char> name] {
		get { // Binary search
			int lo = 0;
			int hi = Children.Count - 1;
			while (lo <= hi) {
				int i = (int)(((uint)hi + (uint)lo) >> 1);
				var node = Children[i];
				int c = name.CompareTo(node.Name, StringComparison.InvariantCultureIgnoreCase);
				if (c == 0)
					return node;
				else if (c > 0)
					lo = i + 1;
				else
					hi = i - 1;
			}
			return null;
		}
	}

	/// <summary>
	/// See <see cref="Index.BuildTree(Index.CreateDirectoryInstance, Index.CreateFileInstance, bool)"/>
	/// </summary>
	protected internal static IDirectoryNode CreateInstance(string name, IDirectoryNode? parent) {
		return new DirectoryNode(name, parent as DirectoryNode);
	}
}