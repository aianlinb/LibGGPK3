using System.Collections.Generic;

namespace LibBundle3.Nodes {
	public class DirectoryNode : IDirectoryNode {
		public virtual string Name { get; }

		public virtual DirectoryNode? Parent { get; }
		ITreeNode? ITreeNode.Parent => Parent;

		public virtual IList<ITreeNode> Children => _Children ??= new();
		protected List<ITreeNode>? _Children;

		/// <summary>
		/// In <see cref="Children"/> starting from this index is not FileNode but DirectoryNode, or -1 if no DirectoryNode
		/// </summary>
		public virtual int DirectoryStart { get; set; }
		
		protected DirectoryNode(string name, DirectoryNode? parent) {
			Name = name;
			Parent = parent;
		}

		protected internal static IDirectoryNode CreateInstance(string name, IDirectoryNode? parent) {
			return new DirectoryNode(name, parent as DirectoryNode);
		}
	}
}