using System.Collections.Generic;

namespace LibBundle3.Nodes;

public interface IDirectoryNode : ITreeNode {
	/// <summary>
	/// The content will be filled by <see cref="Index.BuildTree(Index.CreateDirectoryInstance, Index.CreateFileInstance, bool)"/> and ordered by <see cref="ITreeNode.Name"/>.
	/// Do not modify them!
	/// </summary>
	public List<ITreeNode> Children { get; }
}