using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

using SystemExtensions;
using SystemExtensions.Streams;

namespace LibBundle3.Records;
public class FileRecord {
	/// <summary>
	/// Hash of <see cref="Path"/> which can be caculated from <see cref="Index.NameHash(ReadOnlySpan{char})"/>
	/// </summary>
	public virtual ulong PathHash { get; }
	/// <summary>
	/// Bundle which contains this file
	/// </summary>
	public virtual BundleRecord BundleRecord { get; protected set; }
	/// <summary>
	/// Offset of the file content in data of <see cref="BundleRecord"/>
	/// </summary>
	public virtual int Offset { get; protected set; }
	/// <summary>
	/// Size of the file content in bytes
	/// </summary>
	public virtual int Size { get; protected set; }

	/// <summary>
	/// Full path of the file in <see cref="Index"/>
	/// </summary>
	/// <remarks>
	/// This will be <see langword="null"/> if the <see cref="Index.ParsePaths"/> has never been called.
	/// </remarks>
	public virtual string Path { get; protected internal set; /* For Index.ParsePaths */}

#pragma warning disable CS8618
	protected internal FileRecord(ulong pathHash, BundleRecord bundleRecord, int offset, int size) {
		PathHash = pathHash;
		BundleRecord = bundleRecord;
		Offset = offset;
		Size = size;
	}

	/// <summary>
	/// Read the content of the file.
	/// </summary>
	/// <param name="bundle">If specified, read from this bundle instance instead of creating a new one</param>
	/// <remarks>
	/// When reading multiple files in batches, use <see cref="Index.Extract(IEnumerable{FileRecord}, Index.FileHandler)"/> instead for better performance.
	/// </remarks>
	public virtual ReadOnlyMemory<byte> Read(Bundle? bundle = null) {
		if (bundle is not null)
			return bundle.Read(Offset, Size);
		// TODO: Bundle cache implementation
		bundle = BundleRecord.Index._BundleToWrite;
		if (bundle?.Record == BundleRecord) // The bundle being written
			return bundle.ReadWithoutCache(Offset, Size);
		if (BundleRecord.TryGetBundle(out bundle, out var ex))
			using (bundle)
				return bundle.ReadWithoutCache(Offset, Size);
		ex?.ThrowKeepStackTrace();
		throw new FileNotFoundException("Failed to get bundle: " + BundleRecord.Path);
	}

	/// <summary>
	/// Read a part of the content of the file.
	/// </summary>
	/// <param name="range">The range of the content to read</param>
	/// <param name="bundle">If specified, read from this bundle instance instead of creating a new one</param>
	/// <remarks>
	/// When reading multiple files in batches, use <see cref="Index.Extract(IEnumerable{FileRecord}, Index.FileHandler)"/> instead for better performance.
	/// </remarks>
	public virtual ReadOnlyMemory<byte> Read(Range range, Bundle? bundle = null) {
		var (offset, length) = range.GetOffsetAndLength(Size);
		if (bundle is not null)
			return bundle.Read(Offset + offset, length);
		bundle = BundleRecord.Index._BundleToWrite;
		if (bundle?.Record == BundleRecord)
			return bundle.ReadWithoutCache(Offset + offset, length);
		if (BundleRecord.TryGetBundle(out bundle, out var ex))
			using (bundle) // TODO: Bundle cache implementation
				return bundle.ReadWithoutCache(Offset + offset, length);
		ex?.ThrowKeepStackTrace();
		throw new FileNotFoundException("Failed to get bundle: " + BundleRecord.Path);
	}

	/// <summary>
	/// Replace the content of the file.
	/// </summary>
	/// <param name="saveIndex">
	/// Whether to call <see cref="Index.Save"/> automatically after writing.
	/// This causes performance penalties when writing multiple files.
	/// </param>
	/// <remarks>
	/// You must call <see cref="Index.Save"/> (unless <paramref name="saveIndex"/>) to save changes after editing all files you want.
	/// </remarks>
	public virtual void Write(scoped ReadOnlySpan<byte> newContent, bool saveIndex = false) {
		var index = BundleRecord.Index;
		lock (index) {
			var b = index._BundleToWrite;
			var ms = index._BundleStreamToWrite;
			if (b is null) {
				index._BundleToWrite = b = index.GetBundleToWrite(out var originalSize);
				if (!index.WR_BundleStreamToWrite.TryGetTarget(out index._BundleStreamToWrite))
					index._BundleStreamToWrite = new(originalSize + newContent.Length);
				ms = index._BundleStreamToWrite;
				ms.Write(index._BundleToWrite.ReadWithoutCache(0, originalSize)); // Read original data of bundle
			}

			Redirect(b.Record!, (int)ms!.Length, newContent.Length);
			ms.Write(newContent);

			if (ms.Length >= index.MaxBundleSize) {
				b.Save(new(ms.GetBuffer(), 0, (int)ms.Length));
				b.Dispose();
				index._BundleToWrite = null;
				ms.SetLength(0);
				index._BundleStreamToWrite = null;
			}
		}
		if (saveIndex)
			index.Save();
	}
	/// <inheritdoc cref="Write(ReadOnlySpan{byte}, bool)"/>
	/// <param name="writer">
	/// <see langword="delegate"/> that provide a <see cref="Span{T}"/> with <paramref name="newSize"/> length
	/// to let you write the new content of the file
	/// </param>
	/// <param name="newSize">Size in bytes of the new content</param>
#if NET9_0_OR_GREATER
	public virtual void Write(Action<Span<byte>> writer, int newSize, bool saveIndex = false) {
#else
	public virtual void Write(WriteAction writer, int newSize, bool saveIndex = false) {
#endif
		var index = BundleRecord.Index;
		lock (index) {
			var b = index._BundleToWrite;
			var ms = index._BundleStreamToWrite;
			if (b is null) {
				index._BundleToWrite = b = index.GetBundleToWrite(out var originalSize);
				if (!index.WR_BundleStreamToWrite.TryGetTarget(out index._BundleStreamToWrite)) {
					index._BundleStreamToWrite = new(originalSize + newSize);
					index.WR_BundleStreamToWrite.SetTarget(index._BundleStreamToWrite);
				}
				ms = index._BundleStreamToWrite;
				ms.Write(index._BundleToWrite.ReadWithoutCache(0, originalSize)); // Read original data of bundle
			}

			{
				var ibw = ms!.AsIBufferWriter();
				writer(ibw.GetSpan(newSize)[..newSize]);
				Redirect(b.Record!, (int)ms!.Length, newSize);
				ibw.Advance(newSize);
			}

			if (ms.Length >= index.MaxBundleSize) {
				b.Save(new(ms.GetBuffer(), 0, (int)ms.Length));
				b.Dispose();
				index._BundleToWrite = null;
				ms.SetLength(0);
				index._BundleStreamToWrite = null;
			}
		}
		if (saveIndex)
			index.Save();
	}
#if !NET9_0_OR_GREATER
	public delegate void WriteAction(scoped Span<byte> buffer);
#endif

	/// <summary>
	/// Redirect the <see cref="FileRecord"/> to another section in specified bundle.
	/// Must call <see cref="Index.Save"/> to save changes after editing all files you want.
	/// </summary>
	public virtual void Redirect(BundleRecord bundle, int offset, int size) {
		if (BundleRecord != bundle) {
			if (bundle.Index != BundleRecord.Index)
				ThrowHelper.Throw<InvalidOperationException>("Attempt to redirect the file to a bundle in another index");
			BundleRecord._Files.Remove(this);
			BundleRecord = bundle;
			bundle._Files.Add(this);
		}
		Offset = offset;
		Size = size;
	}

	/// <summary>
	/// Size of the content when <see cref="Serialize"/> to <see cref="Index"/>
	/// </summary>
	protected internal const int RecordLength = sizeof(ulong) + sizeof(int) * 3;
	/// <summary>
	/// Function to serialize the record to <see cref="Index"/>
	/// </summary>
	protected internal virtual void Serialize(Stream stream) {
		stream.Write(PathHash);
		stream.Write(BundleRecord.BundleIndex);
		stream.Write(Offset);
		stream.Write(Size);
	}
}