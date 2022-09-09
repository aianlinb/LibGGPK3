using LibBundle3.Records;

namespace LibBundle3.Nodes {
	public interface IFileNode : ITreeNode {
		public FileRecord Record { get; }
	}
}