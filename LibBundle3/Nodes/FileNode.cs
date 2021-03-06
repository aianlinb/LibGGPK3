using LibBundle3.Records;
using System.IO;

namespace LibBundle3.Nodes {
	public class FileNode : BaseNode {
		public readonly FileRecord Record;
		public FileNode(FileRecord record) : base(Path.GetFileName(record.Path)) {
			Record = record;
		}
	}
}