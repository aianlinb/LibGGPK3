using System;

namespace LibBundle3.Nodes {
	/// <summary>
	/// Do not implement this interface, use <see cref="IFileNode"/> and <see cref="IDirectoryNode"/> instead
	/// </summary>
	public interface ITreeNode {
		/// <summary>
		/// Parent node of this node, or null if this is the Root node
		/// </summary>
		public ITreeNode? Parent { get; }
		public string Name { get; }

		/// <summary>
		/// Get the absolute path of <paramref name="node"/> in the tree, not starts with '/', and ends with '/' if this is a directory
		/// </summary>
		public static string GetPath(ITreeNode node) {
			if (node is IDirectoryNode) {
				if (node.Parent == null)
					return string.IsNullOrWhiteSpace(node.Name) ? "" : "/";
				return GetPath(node.Parent) + node.Name + "/";
			}
			if (node is IFileNode)
				return GetPath(node.Parent!) + node.Name;
			throw new InvalidCastException();
		}
	}
}