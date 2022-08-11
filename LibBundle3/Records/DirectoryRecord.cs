using System.Runtime.InteropServices;

namespace LibBundle3.Records {
	/// <summary>
	/// Currently unused
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Size = 20, Pack = 4)]
	internal struct DirectoryRecord {
		public ulong PathHash;
		public int Offset;
		public int Size;
		public int RecursiveSize;

		public DirectoryRecord(ulong pathHash, int offset, int size, int recursiveSize) {
			PathHash = pathHash;
			Offset = offset;
			Size = size;
			RecursiveSize = recursiveSize;
		}
	}
}