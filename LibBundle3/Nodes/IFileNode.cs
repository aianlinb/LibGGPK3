using LibBundle3.Records;

namespace LibBundle3.Nodes {
	public interface IFileNode : ITreeNode {
		public abstract FileRecord Record { get; }
	}
}