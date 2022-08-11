using LibBundle3.Records;
using System.IO;

namespace LibBundle3.Nodes {
	public class FileNode : BaseNode {
		public readonly FileRecord Record;
		
		protected internal FileNode(FileRecord record, DirectoryNode parent) : base(Path.GetFileName(record.Path), parent) {
			Record = record;
		}
		
		public override string GetPath() => Parent!.GetPath() + Name; // == Record.Path
	}
}