using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace LibBundle3.Records {
	public class FileRecord {
		public virtual ulong PathHash { get; }
		public virtual BundleRecord BundleRecord { get; protected set; }
		public virtual int Offset { get; protected set; }
		public virtual int Size { get; protected set; }

		public virtual string Path { get; protected internal set; }

#pragma warning disable CS8618
		protected internal FileRecord(ulong pathHash, BundleRecord bundleRecord, int offset, int size) {
			PathHash = pathHash;
			BundleRecord = bundleRecord;
			Offset = offset;
			Size = size;
		}

		/// <summary>
		/// Read the content of the file from a <paramref name="bundle"/> instance.
		/// </summary>
		/// <param name="bundle">If specified, read from this bundle instance instead of creating a new one</param>
		/// <remarks>
		/// Do not use this function when reading multiple files in batches,
		/// use <see cref="Index.Extract(IEnumerable{FileRecord})"/> instead.
		/// </remarks>
		public virtual ReadOnlyMemory<byte> Read(Bundle? bundle = null) {
			if (bundle != null)
				return bundle.Read(Offset, Size);
			if (BundleRecord.TryGetBundle(out bundle, out var ex))
				using (bundle) // TODO: BundlePool implementation
					return bundle.ReadWithoutCache(Offset, Size);
			throw ex ?? new("Failed to get bundle: " + BundleRecord.Path);
		}

		/// <summary>
		/// Read a part of the content of the file from a <paramref name="bundle"/> instance.
		/// </summary>
		/// <param name="range">The range of the content to read</param>
		/// <param name="bundle">If specified, read from this bundle instance instead of creating a new one</param>
		/// <remarks>
		/// Do not use this function when reading multiple files in batches,
		/// use <see cref="Index.Extract(IEnumerable{FileRecord})"/> instead.
		/// </remarks>
		public virtual ReadOnlyMemory<byte> Read(Range range, Bundle? bundle = null) {
			var (offset, length) = range.GetOffsetAndLength(Size);
			if (bundle != null)
				return bundle.Read(Offset + offset, length);
			if (BundleRecord.TryGetBundle(out bundle, out var ex))
				using (bundle) // TODO: BundlePool implementation
					return bundle.ReadWithoutCache(Offset + offset, length);
			throw ex ?? new("Failed to get bundle: " + BundleRecord.Path);
		}

		/// <summary>
		/// Replace the content of the file.
		/// This call <see cref="Index.Save"/> automatically.
		/// </summary>
		/// <remarks>
		/// Do not use this function when writing multiple files in batches,
		/// use <see cref="Index.Replace(IEnumerable{FileRecord}, Index.GetDataHandler)"/> instead.
		/// </remarks>
		public virtual void Write(ReadOnlySpan<byte> newContent) {
			using var bundle = BundleRecord.Index.GetBundleToWrite(out var size);
			var len = size + newContent.Length;
			byte[]? rented = null;
			try {
				Span<byte> b = len <= 4096 ? stackalloc byte[len] : (rented = ArrayPool<byte>.Shared.Rent(len)).AsSpan(..len);
				bundle.ReadWithoutCache(0, size).AsSpan().CopyTo(b);
				newContent.CopyTo(b[size..]);
				bundle.Save(b);
			} finally {
				if (rented != null)
					ArrayPool<byte>.Shared.Return(rented);
			}
			Redirect(bundle.Record!, size, newContent.Length);
			BundleRecord.Index.Save();
		}

		/// <summary>
		/// Redirect the <see cref="FileRecord"/> to another section in specified bundle.
		/// Must call <see cref="Index.Save"/> to save changes after editing all files you want.
		/// </summary>
		public virtual void Redirect(BundleRecord bundle, int offset, int size) {
			if (bundle.Index != BundleRecord.Index)
				throw new InvalidOperationException("Attempt to redirect the file to a bundle in another index");
			if (BundleRecord != bundle) {
				BundleRecord._Files.Remove(this);
				BundleRecord = bundle;
				bundle._Files.Add(this);
			}
			Offset = offset;
			Size = size;
		}

		protected internal const int RecordLength = sizeof(ulong) + sizeof(int) * 3;
		protected internal virtual void Serialize(Stream stream) {
			stream.Write(PathHash);
			stream.Write(BundleRecord.BundleIndex);
			stream.Write(Offset);
			stream.Write(Size);
		}
	}
}