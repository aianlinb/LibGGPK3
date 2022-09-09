using System.Collections.Generic;

namespace LibBundle3.Nodes {
	public interface IDirectoryNode : ITreeNode {
		public IList<ITreeNode> Children { get; }
	}
}