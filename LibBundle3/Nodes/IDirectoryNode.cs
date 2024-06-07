using System.Collections.Generic;

namespace LibBundle3.Nodes;
public interface IDirectoryNode : ITreeNode {
	public abstract IList<ITreeNode> Children { get; }
}