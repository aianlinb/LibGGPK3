using System;
using System.Runtime.CompilerServices;

namespace LibBundle3.Records {
	public class FileRecord {
		public ulong PathHash;
		public int BundleIndex;
		public int Offset;
		public int Size;

		protected internal string _Path;
		public string Path {
			get => _Path;
			set {
				_Path = value;
				PathHash = Index.FNV1a64Hash(value);
			}
		}

		public BundleRecord BundleRecord;
		public DirectoryRecord DirectoryRecord;

#pragma warning disable CS8618
		public FileRecord(ulong pathHash, int bundleIndex, int offset, int size) {
			PathHash = pathHash;
			BundleIndex = bundleIndex;
			Offset = offset;
			Size = size;
		}

		public virtual Memory<byte> Read() {
			return BundleRecord.Bundle.ReadData(Offset, Size);
		}

		/// <summary>
		/// Replace the content of the file and save the Index
		/// </summary>
		/// <param name="newContent"></param>
		public virtual void Write(ReadOnlySpan<byte> newContent) {
			var b = BundleRecord.Bundle.ReadData();
			Offset = b.Length;
			Size = newContent.Length;
			var b2 = new byte[b.Length + Size];
			Unsafe.CopyBlockUnaligned(ref b2[0], ref b[0], (uint)b.Length);
			newContent.CopyTo(b2.AsSpan().Slice(Offset, Size));
			BundleRecord.Bundle.SaveData(b2);
			BundleRecord.UncompressedSize = BundleRecord.Bundle.UncompressedSize;
			BundleRecord.Index.Save();
		}

		public virtual void Redirect(BundleRecord bundle, int offset, int size) {
			BundleRecord = bundle;
			BundleIndex = bundle.BundleIndex;
			Offset = offset;
			Size = size;
		}
	}
}