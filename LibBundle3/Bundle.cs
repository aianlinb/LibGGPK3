using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using LibBundle3.Records;

using SystemExtensions;
using SystemExtensions.Streams;

namespace LibBundle3;
/// <remarks>
/// Read/Save are not thread-safe.
/// </remarks>
public class Bundle : IDisposable {
	/// <summary>
	/// Metadata of a bundle file which is stored at the beginning of the file in 60 bytes
	/// </summary>
	[Serializable]
	[StructLayout(LayoutKind.Sequential, Size = 60, Pack = 4)]
	protected struct Header {
		public int uncompressed_size;
		public int compressed_size;
		public int head_size = 48; // chunk_count * 4 + 48
		public Oodle.Compressor compressor = Oodle.Compressor.Leviathan; // Leviathan == 13
		public int unknown = 1; // 1
		public long uncompressed_size_long; // == uncompressed_size
		public long compressed_size_long; // == compressed_size
		public int chunk_count;
		public int chunk_size = 256 * 1024; // 256KB == 262144
		public int unknown3 = 0; // 0
		public int unknown4 = 0; // 0
		public int unknown5 = 0; // 0
		public int unknown6 = 0; // 0

		/// <summary>
		/// Initialize a <see cref="Header"/> instance with default values of a empty bundle (Not same as <see langword="default"/>)
		/// </summary>
		public Header() { }

		/// <returns>Size of decompressed Chunks[Chunks.Length - 1] in bytes</returns>
		public readonly int GetLastChunkSize() {
			return uncompressed_size - (chunk_size * (chunk_count - 1));
		}
	}

	/// <summary>
	/// Record of the <see cref="Bundle"/> instance, not <see langword="null"/> when this instance is created by <see cref="Index"/>
	/// </summary>
	public virtual BundleRecord? Record { get; }

	/// <summary>
	/// Size of the uncompressed content in bytes, synced with <see cref="BundleRecord.UncompressedSize"/> of <see cref="Record"/>
	/// </summary>
	public virtual int UncompressedSize {
		get => metadata.uncompressed_size;
		internal set => metadata.uncompressed_size = value; // See Index.CreateBundle
	}
	/// <summary>
	/// Size of the compressed content in bytes
	/// </summary>
	public virtual int CompressedSize => metadata.compressed_size;

	protected readonly Stream baseStream;
	/// <summary>
	/// If false, close the <see cref="baseStream"/> when <see cref="Dispose"/>
	/// </summary>
	protected readonly bool leaveOpen;
	protected Header metadata;
	/// <summary>
	/// Sizes of each compressed chunk in bytes
	/// </summary>
	protected int[] compressed_chunk_sizes;
	/// <summary>
	/// Cached data of the full decompressed content, use <see cref="cacheTable"/> to determine the initialization of each chunk
	/// </summary>
	protected byte[]? cachedContent;
	/// <summary>
	/// Indicate whether the corresponding chunk of <see cref="cachedContent"/> is initialized
	/// </summary>
	protected bool[]? cacheTable;

	/// <param name="filePath">Path of the bundle file on disk</param>
	/// <param name="record">Record of this bundle file</param>
	/// <exception cref="FileNotFoundException" />
	public Bundle(string filePath, BundleRecord? record = null) :
		this(File.Open(Utils.ExpandPath(filePath), FileMode.Open, FileAccess.ReadWrite, FileShare.Read), false, record) { }

	/// <param name="stream">Stream of the bundle file</param>
	/// <param name="leaveOpen">If false, close the <paramref name="stream"/> when this instance is disposed</param>
	/// <param name="record">Record of this bundle file</param>
	public unsafe Bundle(Stream stream, bool leaveOpen = false, BundleRecord? record = null) {
		ArgumentNullException.ThrowIfNull(stream);
		if (!BitConverter.IsLittleEndian)
			ThrowHelper.Throw<NotSupportedException>("Big-endian architecture is not supported");
		baseStream = stream;
		this.leaveOpen = leaveOpen;
		Record = record;
		stream.Position = 0;
		stream.Read(out metadata);
		if (record is not null)
			record.UncompressedSize = metadata.uncompressed_size;
		stream.Read(compressed_chunk_sizes = GC.AllocateUninitializedArray<int>(metadata.chunk_count));
	}

	/// <summary>
	/// Internal used by <see cref="Index.CreateBundle"/>
	/// </summary>
	/// <param name="stream">Stream of the bundle to write (which will be cleared)</param>
	/// <param name="record">Record of the bundle</param>
	protected internal unsafe Bundle(Stream stream, BundleRecord? record) {
		ArgumentNullException.ThrowIfNull(stream);
		baseStream = stream;
		Record = record;
		compressed_chunk_sizes = [];
		stream.Position = 0;
		metadata = new();
		stream.Write(in metadata);
		stream.SetLength(stream.Position);
	}

	/// <summary>
	/// Read the whole data of the bundle without caching
	/// </summary>
	public byte[] ReadWithoutCache() {
		return ReadChunksUncached(0, metadata.chunk_count); // == ReadWithoutCache(..metadata.uncompressed_size)
	}
	/// <summary>
	/// Read the data with the given <paramref name="range"/> without caching
	/// </summary>
	public ArraySegment<byte> ReadWithoutCache(Range range) {
		var (offset, length) = range.GetOffsetAndLength(metadata.uncompressed_size);
		return ReadWithoutCache(offset, length);
	}
	/// <summary>
	/// Read the data with the given <paramref name="offset"/> and <paramref name="length"/> without caching
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException"></exception>s
	public ArraySegment<byte> ReadWithoutCache(int offset, int length) {
		// Negative values are checked in ReadChunksUncached(int, int)
		ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + length, metadata.uncompressed_size);
		if (length == 0)
			return ArraySegment<byte>.Empty;
		var start = offset / metadata.chunk_size;
		var count = (offset + length - 1) / metadata.chunk_size - start + 1;
		return new(ReadChunksUncached(start, count), offset % metadata.chunk_size, length);
	}

	/// <summary>
	/// Read the whole data of the bundle (use cached data if exists)
	/// </summary>
	/// <remarks>Use <see cref="ReadWithoutCache()"/> instead if you'll read only once</remarks>
	public ReadOnlyMemory<byte> Read() {
		ReadChunks(0, metadata.chunk_count); // == Read(..metadata.uncompressed_size)
		return cachedContent;
	}
	/// <summary>
	/// Read the data with the given <paramref name="range"/> (use cached data if exists)
	/// </summary>
	/// <param name="range">Range of the data to read</param>
	/// <remarks>Use <see cref="ReadWithoutCache(Range)"/> instead if you'll read only once</remarks>
	public ReadOnlyMemory<byte> Read(Range range) {
		var (offset, length) = range.GetOffsetAndLength(metadata.uncompressed_size);
		return Read(offset, length);
	}
	/// <summary>
	/// Read the data with the given <paramref name="offset"/> and <paramref name="length"/> (use cached data if exists)
	/// </summary>
	/// <remarks>Use <see cref="ReadWithoutCache(int, int)"/> instead if you'll read only once</remarks>
	/// <exception cref="ArgumentOutOfRangeException"></exception>
	public ReadOnlyMemory<byte> Read(int offset, int length) {
		// Negative offset and length are checked in ReadChunks(int, int)
		ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + length, metadata.uncompressed_size);
		if (length == 0)
			return ReadOnlyMemory<byte>.Empty;
		var start = offset / metadata.chunk_size;
		var count = (offset + length - 1) / metadata.chunk_size - start + 1;
		ReadChunks(start, count);
		return new(cachedContent, offset, length);
	}

	/// <summary>
	/// Read data from compressed chunk(with size of <see cref="Header.chunk_size"/>)s
	/// and combine them to a <see cref="byte"/>[] without caching
	/// </summary>
	/// <param name="range">Range of the index of chunks to read</param>
	protected byte[] ReadChunksUncached(Range range) {
		var (start, count) = range.GetOffsetAndLength(metadata.chunk_count);
		return ReadChunksUncached(start, count);
	}
	/// <summary>
	/// Read data from compressed chunk(with size <see cref="Header.chunk_size"/>)s
	/// start from index = <paramref name="start"/>and combine them to a <see cref="byte"/>[] without caching
	/// </summary>
	/// <param name="start">Index of the beginning chunk</param>
	/// <param name="count">Number of chunks to read</param>
	/// <exception cref="ArgumentOutOfRangeException"></exception>
	protected virtual unsafe byte[] ReadChunksUncached(int start, int count = 1) {
		EnsureNotDisposed();
		ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)start, (uint)metadata.chunk_count);
		ArgumentOutOfRangeException.ThrowIfNegative(count);
		if (count == 0)
			return [];
		ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)(start + count), (uint)metadata.chunk_count);
		Oodle.Initialize(new() { ChunkSize = metadata.chunk_size, Compressor = metadata.compressor, EnableCompressing = false });
		var result = GC.AllocateUninitializedArray<byte>(start + count == metadata.chunk_count
			? metadata.uncompressed_size - start * count : metadata.chunk_size * count);
		baseStream.Position = (sizeof(int) * 3) + metadata.head_size + compressed_chunk_sizes.Take(start).Sum();

		var last = metadata.chunk_count - 1;
		count = start + count;
		var compressed = ArrayPool<byte>.Shared.Rent(Oodle.GetCompressedBufferSize());
		try {
			fixed (byte* ptr = result, tmp = compressed) {
				var p = ptr;
				for (var i = start; i < count; ++i) {
					baseStream.ReadExactly(new(tmp, compressed_chunk_sizes[i]));
					if (i == last) {
						last = metadata.GetLastChunkSize();
						if (Oodle.Decompress(tmp, compressed_chunk_sizes[i], p, last) != last)
							throw new("Failed to decompress last chunk with index: " + i);
					} else {
						if (Oodle.Decompress(tmp, compressed_chunk_sizes[i], p) != metadata.chunk_size)
							throw new("Failed to decompress chunk with index: " + i);
					}
					p += metadata.chunk_size;
				}
			}
			return result;
		} finally {
			ArrayPool<byte>.Shared.Return(compressed);
		}
	}

	/// <summary>
	/// Read data from compressed chunk(with size of <see cref="Header.chunk_size"/>)s to <see cref="cachedContent"/>
	/// </summary>
	/// <param name="range">Range of the index of chunks to read</param>
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	protected void ReadChunks(Range range) {
		var (start, count) = range.GetOffsetAndLength(metadata.chunk_count);
		ReadChunks(start, count);
	}
	/// <summary>
	/// Read data from compressed chunk(with size of <see cref="Header.chunk_size"/>)s
	/// start from index = <paramref name="start"/> to <see cref="cachedContent"/> (use cached if exists)
	/// </summary>
	/// <param name="start">Index of the beginning chunk</param>
	/// <param name="count">Number of chunks to read</param>
	/// <exception cref="ArgumentOutOfRangeException"></exception>
	protected virtual unsafe void ReadChunks(int start, int count = 1) {
		EnsureNotDisposed();
		ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)start, (uint)metadata.chunk_count);
		ArgumentOutOfRangeException.ThrowIfNegative(count);
		if (count == 0)
			return;
		ArgumentOutOfRangeException.ThrowIfGreaterThan(start + count, metadata.chunk_count);
		Oodle.Initialize(new() { ChunkSize = metadata.chunk_size, Compressor = metadata.compressor, EnableCompressing = false });
		cachedContent ??= GC.AllocateUninitializedArray<byte>(metadata.uncompressed_size);
		cacheTable ??= new bool[metadata.chunk_count];
		baseStream.Position = (sizeof(int) * 3) + metadata.head_size + compressed_chunk_sizes.Take(start).Sum();

		var last = metadata.chunk_count - 1;
		count = start + count;
		var compressed = ArrayPool<byte>.Shared.Rent(Oodle.GetCompressedBufferSize());
		try {
			fixed (byte* ptr = cachedContent, tmp = compressed) {
				var p = ptr + start * metadata.chunk_size;
				for (var i = start; i < count; ++i) {
					if (cacheTable[i]) {
						baseStream.Seek(compressed_chunk_sizes[i], SeekOrigin.Current);
					} else {
						baseStream.ReadExactly(new(tmp, compressed_chunk_sizes[i]));
						if (i == last) {
							last = metadata.GetLastChunkSize();
							if (Oodle.Decompress(tmp, compressed_chunk_sizes[i], p, last) != last)
								throw new("Failed to decompress last chunk with index: " + i);
						} else {
							if (Oodle.Decompress(tmp, compressed_chunk_sizes[i], p) != metadata.chunk_size)
								throw new("Failed to decompress chunk with index: " + i);
						}
						cacheTable[i] = true;
					}
					p += metadata.chunk_size;
				}
			}
		} finally {
			ArrayPool<byte>.Shared.Return(compressed);
		}
	}

	/// <summary>
	/// Remove all the cached data of this instance
	/// </summary>
	public virtual void RemoveCache() {
		cachedContent = null;
		cacheTable = null;
	}

	/// <summary>
	/// Save the bundle with new contents
	/// </summary>
	public virtual unsafe void Save(scoped ReadOnlySpan<byte> newContent, Oodle.CompressionLevel compressionLevel = Oodle.CompressionLevel.Normal) {
		EnsureNotDisposed();
		RemoveCache();

		Oodle.Initialize(new() { ChunkSize = metadata.chunk_size, Compressor = metadata.compressor, CompressionLevel = compressionLevel, EnableCompressing = true });
		metadata.uncompressed_size_long = metadata.uncompressed_size = newContent.Length;
		metadata.chunk_count = metadata.uncompressed_size / metadata.chunk_size;
		if (metadata.uncompressed_size > metadata.chunk_count * metadata.chunk_size)
			++metadata.chunk_count;
		metadata.head_size = metadata.chunk_count * sizeof(int) + (sizeof(Header) - sizeof(int) * 3);
		baseStream.Position = (sizeof(int) * 3) + metadata.head_size;
		compressed_chunk_sizes = GC.AllocateUninitializedArray<int>(metadata.chunk_count);
		metadata.compressed_size = 0;
		var compressed = ArrayPool<byte>.Shared.Rent(Oodle.GetCompressedBufferSize());
		try {
			fixed (byte* ptr = newContent, tmp = compressed) {
				var p = ptr;
				var last = metadata.chunk_count - 1;
				int l;
				for (var i = 0; i < last; ++i) {
					l = (int)Oodle.Compress(p, tmp);
					compressed_chunk_sizes[i] = l;
					metadata.compressed_size += l;
					p += metadata.chunk_size;
					baseStream.Write(new(tmp, l));
				}
				l = (int)Oodle.Compress(p, metadata.GetLastChunkSize(), tmp);
				compressed_chunk_sizes[last] = l;
				metadata.compressed_size += l;
				baseStream.Write(new(tmp, l));
			}
		} finally {
			ArrayPool<byte>.Shared.Return(compressed);
		}
		metadata.compressed_size_long = metadata.compressed_size;

		baseStream.Position = 0;
		baseStream.Write(in metadata);
		baseStream.Write(compressed_chunk_sizes);

		baseStream.SetLength((sizeof(int) * 3) + metadata.head_size + metadata.compressed_size);
		baseStream.Flush();
		if (Record is not null)
			Record.UncompressedSize = metadata.uncompressed_size;
	}

	protected virtual void EnsureNotDisposed() {
		ObjectDisposedException.ThrowIf(compressed_chunk_sizes is null, this);
	}

	public virtual void Dispose() {
		GC.SuppressFinalize(this);
		compressed_chunk_sizes = null!;
		RemoveCache();
		try {
			if (!leaveOpen)
				baseStream?.Close();
		} catch { /*Closing closed stream*/ }
	}
}