using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

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
	public virtual string Path { get; protected internal set; /* For Index.ParsePaths */ }

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
		if (bundle is not null)
			return bundle.Read(Offset, Size);
		if (BundleRecord.TryGetBundle(out bundle, out var ex))
			using (bundle) // TODO: BundlePool implementation
				return bundle.ReadWithoutCache(Offset, Size);
		ex?.ThrowKeepStackTrace();
		throw new FileNotFoundException("Failed to get bundle: " + BundleRecord.Path);
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
		if (bundle is not null)
			return bundle.Read(Offset + offset, length);
		if (BundleRecord.TryGetBundle(out bundle, out var ex))
			using (bundle) // TODO: BundlePool implementation
				return bundle.ReadWithoutCache(Offset + offset, length);
		ex?.ThrowKeepStackTrace();
		throw new FileNotFoundException("Failed to get bundle: " + BundleRecord.Path);
	}

	/// <summary>
	/// Replace the content of the file.
	/// This call <see cref="Index.Save"/> automatically.
	/// </summary>
	/// <remarks>
	/// Do not use this function when writing multiple files in batches,
	/// use <see cref="Index.Replace(IEnumerable{FileRecord}, Index.GetDataHandler, bool)"/> instead.
	/// </remarks>
	[SkipLocalsInit]
	public virtual void Write(scoped ReadOnlySpan<byte> newContent) {
		using (var bundle = BundleRecord.Index.GetBundleToWrite(out var size)) {
			var len = size + newContent.Length;
			byte[]? rented = null;
			try {
				Span<byte> b = len <= 4096 ? stackalloc byte[len] : (rented = ArrayPool<byte>.Shared.Rent(len)).AsSpan(0, len);
				bundle.ReadWithoutCache(0, size).AsSpan().CopyTo(b);
				newContent.CopyTo(b[size..]);
				bundle.Save(b);
			} finally {
				if (rented is not null)
					ArrayPool<byte>.Shared.Return(rented);
			}
			Redirect(bundle.Record!, size, newContent.Length);
		}
		BundleRecord.Index.Save();
	}

	/// <summary>
	/// Redirect the <see cref="FileRecord"/> to another section in specified bundle.
	/// Must call <see cref="Index.Save"/> to save changes after editing all files you want.
	/// </summary>
	public virtual void Redirect(BundleRecord bundle, int offset, int size) {
		if (bundle.Index != BundleRecord.Index)
			ThrowHelper.Throw<InvalidOperationException>("Attempt to redirect the file to a bundle in another index");
		if (BundleRecord != bundle) {
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