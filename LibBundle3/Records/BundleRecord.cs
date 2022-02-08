using System.Collections.Generic;

namespace LibBundle3.Records {
	public class BundleRecord {
		public string Path; // without extension
		public int UncompressedSize;

		public int BundleIndex;
		public readonly List<FileRecord> Files = new();
		public Index Index;

		protected Bundle? _Bundle;
		public virtual Bundle Bundle {
			get {
				_Bundle ??= Index.FuncReadBundle(this);
				return _Bundle;
			}
		}

		public BundleRecord(string path, int uncompressedSize, Index index) {
			Path = path;
			UncompressedSize = uncompressedSize;
			Index = index;
		}
	}
}