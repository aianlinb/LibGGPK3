using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SystemExtensions.Collections;

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
		/// Get the absolute path of the node in the tree, and ends with '/' if this is <see cref="IDirectoryNode"/>
		/// </summary>
		[SkipLocalsInit]
		public static string GetPath(ITreeNode node) {
			var builder = new ValueList<char>(stackalloc char[256]);
			node.GetPath(ref builder);
			using (builder)
				return builder.AsSpan().ToString();
		}
		private void GetPath(scoped ref ValueList<char> builder) {
			Parent?.GetPath(ref builder);
			builder.AddRange(Name.AsSpan());
			if (this is IDirectoryNode)
				builder.Add('/');
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