using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace LibBundle3.Records {
	public class BundleRecord {
		protected string _Path; // without extension
		public virtual string Path => _Path + ".bundle.bin";
		public virtual int UncompressedSize { get; protected internal set; }

		public readonly Index Index;
		public readonly int BundleIndex;
		protected internal Bundle? _Bundle;
		
		protected internal readonly List<FileRecord> _Files = new();
		public ReadOnlyCollection<FileRecord> Files => new(_Files);
		public virtual Bundle Bundle {
			get {
				_Bundle ??= Index.FuncReadBundle(this);
				return _Bundle;
			}
		}

		protected internal BundleRecord(string path, int uncompressedSize, Index index, int bundleIndex) {
			_Path = path;
			UncompressedSize = uncompressedSize;
			Index = index;
			BundleIndex = bundleIndex;
		}
		
		protected internal virtual void Save(BinaryWriter writer) {
			var path = Encoding.UTF8.GetBytes(_Path);
			writer.Write(path.Length);
			writer.Write(path, 0, path.Length);
			writer.Write(UncompressedSize);
		}
	}
}