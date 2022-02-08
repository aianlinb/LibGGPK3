using System.Collections.Generic;

namespace LibBundle3.Records {
	/// <summary>
	/// Currently unused
	/// </summary>
	public class DirectoryRecord {
		public ulong PathHash;
		public int Offset;
		public int Size;
		public int RecursiveSize;

		public readonly List<FileRecord> Children = new(); // Files only

		public DirectoryRecord(ulong pathHash, int offset, int size, int recursiveSize) {
			PathHash = pathHash;
			Offset = offset;
			Size = size;
			RecursiveSize = recursiveSize;
		}
	}
}