using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace LibBundle3.Records {
	public class FileRecord {
		public readonly ulong PathHash;
		public BundleRecord BundleRecord { get; protected set; }
		public int Offset { get; protected set; }
		public int Size { get; protected set; }

		public string Path { get; protected internal set; }

#pragma warning disable CS8618
		protected internal FileRecord(ulong pathHash, BundleRecord bundleRecord, int offset, int size) {
			PathHash = pathHash;
			BundleRecord = bundleRecord;
			Offset = offset;
			Size = size;
		}

		/// <summary>
		/// Read the content of the file
		/// </summary>
		public virtual Memory<byte> Read() {
			return BundleRecord.Bundle.ReadData(Offset, Size);
		}

		/// <summary>
		/// Replace the content of the file and save the <see cref="Index"/>
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
			BundleRecord.Index.Save();
		}

		/// <summary>
		/// Redirect the <see cref="FileRecord"/> to another section in specified bundle
		/// </summary>
		public virtual void Redirect(BundleRecord bundle, int offset, int size) {
			BundleRecord = bundle;
			Offset = offset;
			Size = size;
		}

		protected internal virtual void Save(BinaryWriter writer) {
			writer.Write(PathHash);
			writer.Write(BundleRecord.BundleIndex);
			writer.Write(Offset);
			writer.Write(Size);
		}
	}
}