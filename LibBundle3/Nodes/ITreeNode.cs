using System;
using System.Collections.Generic;

namespace LibBundle3.Nodes {
	/// <summary>
	/// Do not implement this interface, use <see cref="IFileNode"/> and <see cref="IDirectoryNode"/> instead
	/// </summary>
	public interface ITreeNode {
		/// <summary>
		/// Parent node of this node, or null if this is the Root node
		/// </summary>
		public IDirectoryNode? Parent { get; }
		public string Name { get; }

		/// <summary>
		/// Get the absolute path (which not starts with '/') of the node in the tree, and ends with '/' if this is a directory
		/// </summary>
		public static string GetPath(ITreeNode node) {
			if (node is IDirectoryNode) {
				if (node.Parent == null)
					return string.IsNullOrEmpty(node.Name) ? string.Empty : "/";
				return @$"{GetPath(node.Parent)}{node.Name}/";
			}
			if (node is IFileNode)
				return GetPath(node.Parent!) + node.Name;
			throw new InvalidCastException("The instance of ITreeNode is netiher IDirectoryNode nor IFileNode");
		}

		/// <summary>
		/// Recursive all nodes under <paramref name="node"/> (contains self)
		/// </summary>
		public static IEnumerable<ITreeNode> RecurseTree(ITreeNode node) {
			yield return node;
			if (node is IDirectoryNode dr)
				foreach (var t in dr.Children)
					foreach (var tt in RecurseTree(node))
						yield return tt;
		}
	}
}