using LibBundle3.Records;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace LibBundle3 {
	public class Bundle : IDisposable {
		[Serializable]
		[StructLayout(LayoutKind.Sequential, Size = 60, Pack = 4)]
		protected struct Header {
			public int uncompressed_size;
			public int compressed_size;
			public int head_size = 48; // chunk_count * 4 + 48
			public Oodle.Compressor compressor = Oodle.Compressor.Leviathan; // Leviathan == 13
			public int unknown = 1; // 1
			public long size_decompressed; // == uncompressed_size
			public long size_compressed; // == compressed_size
			public int chunk_count;
			public int chunk_size = 262144; // 256KB == 262144
			public int unknown3 = 0; // 0
			public int unknown4 = 0; // 0
			public int unknown5 = 0; // 0
			public int unknown6 = 0; // 0

			public Header() { }

			public readonly int GetLastChunkSize() {
				return uncompressed_size - (chunk_size * (chunk_count - 1));
			}
		}

		public virtual BundleRecord? Record { get; }

		public virtual int UncompressedSize {
			get => metadata.uncompressed_size;
			/// <see cref="Index.CreateBundle"/>
			internal set => metadata.uncompressed_size = value;
		}
		public virtual int CompressedSize => metadata.compressed_size;

		protected readonly Stream baseStream;
		protected readonly bool leaveOpen; // If false, close the baseStream when dispose
		protected Header metadata;
		protected int[] compressed_chunk_sizes;
		protected byte[]? cachedContent;
		protected bool[]? cacheTable;

		/// <param name="filePath">Path of the bundle file on disk</param>
		/// <param name="record">Record of this bundle file</param>
		/// <exception cref="FileNotFoundException" />
		public Bundle(string filePath, BundleRecord? record = null) : this(File.Open(Extensions.ExpandPath(filePath), FileMode.Open, FileAccess.ReadWrite, FileShare.Read), false, record) { }

		/// <param name="stream">Stream of the bundle</param>
		/// <param name="leaveOpen">If false, close the <paramref name="stream"/> after this instance has been disposed</param>
		/// <param name="record">Record of this bundle file</param>
		public unsafe Bundle(Stream stream, bool leaveOpen = false, BundleRecord? record = null) {
			baseStream = stream ?? throw new ArgumentNullException(nameof(stream));
			this.leaveOpen = leaveOpen;
			Record = record;
			stream.Position = 0;
			fixed (Header *p = &metadata)
				stream.ReadExactly(new(p, sizeof(Header)));
			if (record != null)
				record.UncompressedSize = metadata.uncompressed_size;
			compressed_chunk_sizes = new int[metadata.chunk_count];
			fixed (int* p2 = compressed_chunk_sizes)
				stream.ReadExactly(new(p2, metadata.chunk_count * sizeof(int)));
		}

		/// <summary>
		/// Used by <see cref="Index.CreateBundle"/>
		/// </summary>
		/// <param name="stream">Stream of the bundle to write (which will be cleared)</param>
		/// <param name="record">Record of the bundle</param>
		protected internal unsafe Bundle(Stream stream, BundleRecord? record) {
			baseStream = stream ?? throw new ArgumentNullException(nameof(stream));
			Record = record;
			compressed_chunk_sizes = Array.Empty<int>();
			stream.Position = 0;
			metadata = new();
			fixed (Header* h = &metadata)
				stream.Write(new(h, sizeof(Header)));
			stream.SetLength(sizeof(Header));
			stream.Flush();
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
			if (offset + length > metadata.uncompressed_size) // Negative offset and length are checked in ReadChunksUncached(int, int)
				throw new ArgumentOutOfRangeException(nameof(length));
			if (length == 0)
				return ArraySegment<byte>.Empty;
			var start = offset / metadata.chunk_size;
			var count = (offset + length - 1) / metadata.chunk_size - start + 1;
			var b = ReadChunksUncached(start, count);
			return new(b, offset % metadata.chunk_size, length);
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
			if (offset + length > metadata.uncompressed_size) // Negative offset and length are checked in ReadChunks(int, int)
				throw new ArgumentOutOfRangeException(nameof(length));
			if (length == 0)
				return ReadOnlyMemory<byte>.Empty;
			var start = offset / metadata.chunk_size;
			var count = (offset + length - 1) / metadata.chunk_size - start + 1;
			ReadChunks(start, count);
			return new(cachedContent, offset, length);
		}

		/// <summary>
		/// Read data from compressed chunk(with size of <see cref="Header.chunk_size"/>)s and combine them to a <see cref="byte"/>[] without caching
		/// </summary>
		/// <param name="range">Range of the index of chunks to read</param>
		protected byte[] ReadChunksUncached(Range range) {
			var (start, count) = range.GetOffsetAndLength(metadata.chunk_count);
			return ReadChunksUncached(start, count);
		}
		/// <summary>
		/// Read data from compressed chunk(with size of <see cref="Header.chunk_size"/>)s start from the chunk with index of <paramref name="start"/> and combine them to a <see cref="byte"/>[] without caching
		/// </summary>
		/// <param name="start">Index of the beginning chunk</param>
		/// <param name="count">Number of chunks to read</param>
		/// <exception cref="ArgumentOutOfRangeException"></exception>
		protected virtual unsafe byte[] ReadChunksUncached(int start, int count = 1) {
			EnsureNotDisposed();
			if (start < 0 || start > metadata.chunk_count)
				throw new ArgumentOutOfRangeException(nameof(start));
			if (count < 0 || start + count > metadata.chunk_count)
				throw new ArgumentOutOfRangeException(nameof(count));
			if (count == 0)
				return Array.Empty<byte>();
			var result = GC.AllocateUninitializedArray<byte>(metadata.chunk_size * count);
			baseStream.Position = (sizeof(int) * 3) + metadata.head_size + compressed_chunk_sizes.Take(start).Sum();

			var last = metadata.chunk_count - 1;
			count = start + count;
			var compressed = ArrayPool<byte>.Shared.Rent(metadata.chunk_size + 64);
			try {
				fixed (byte* ptr = result, tmp = compressed) {
					var p = ptr;
					for (var i = start; i < count; ++i) {
						baseStream.ReadExactly(new(tmp, compressed_chunk_sizes[i]));
						Oodle.OodleLZ_Decompress(tmp, compressed_chunk_sizes[i], p, i == last ? metadata.GetLastChunkSize() : metadata.chunk_size, 0);
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
		protected void ReadChunks(Range range) {
			var (start, count) = range.GetOffsetAndLength(metadata.chunk_count);
			ReadChunks(start, count);
		}
		/// <summary>
		/// Read data from compressed chunk(with size of <see cref="Header.chunk_size"/>)s start from the chunk with index of <paramref name="start"/> to <see cref="cachedContent"/>
		/// (use cached data if exists)
		/// </summary>
		/// <param name="start">Index of the beginning chunk</param>
		/// <param name="count">Number of chunks to read</param>
		/// <exception cref="ArgumentOutOfRangeException"></exception>
		protected virtual unsafe void ReadChunks(int start, int count = 1) {
			EnsureNotDisposed();
			if (start < 0 || start > metadata.chunk_count)
				throw new ArgumentOutOfRangeException(nameof(start));
			if (count < 0 || start + count > metadata.chunk_count)
				throw new ArgumentOutOfRangeException(nameof(count));
			if (count == 0)
				return;
			cachedContent ??= GC.AllocateUninitializedArray<byte>(metadata.uncompressed_size);
			cacheTable ??= new bool[metadata.chunk_count];
			baseStream.Position = (sizeof(int) * 3) + metadata.head_size + compressed_chunk_sizes.Take(start).Sum();

			var last = metadata.chunk_count - 1;
			count = start + count;
			var compressed = ArrayPool<byte>.Shared.Rent(metadata.chunk_size + 64);
			try {
				fixed (byte* ptr = cachedContent, tmp = compressed) {
					var p = ptr + start * metadata.chunk_size;
					for (var i = start; i < count; ++i) {
						if (cacheTable[i]) {
							baseStream.Seek(compressed_chunk_sizes[i], SeekOrigin.Current);
						} else {
							baseStream.ReadExactly(new(tmp, compressed_chunk_sizes[i]));
							Oodle.OodleLZ_Decompress(tmp, compressed_chunk_sizes[i], p, i == last ? metadata.GetLastChunkSize() : metadata.chunk_size, 0);
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
		public virtual unsafe void Save(ReadOnlySpan<byte> newContent, Oodle.CompressionLevel compressionLevel = Oodle.CompressionLevel.Normal) {
			EnsureNotDisposed();
			RemoveCache();
			try {
				metadata.size_decompressed = metadata.uncompressed_size = newContent.Length;
				metadata.chunk_count = metadata.uncompressed_size / metadata.chunk_size;
				if (metadata.uncompressed_size % metadata.chunk_size != 0)
					++metadata.chunk_count;
				metadata.head_size = metadata.chunk_count * sizeof(int) + (sizeof(Header) - sizeof(int) * 3);
				baseStream.Position = (sizeof(int) * 3) + metadata.head_size;
				compressed_chunk_sizes = new int[metadata.chunk_count];
				metadata.compressed_size = 0;
				var compressed = ArrayPool<byte>.Shared.Rent(metadata.chunk_size + 64);
				try {
					fixed (byte* ptr = newContent, tmp = compressed) {
						var p = ptr;
						var last = metadata.chunk_count - 1;
						int l;
						for (var i = 0; i < last; ++i) {
							l = (int)Oodle.OodleLZ_Compress(metadata.compressor, p, metadata.chunk_size, tmp, compressionLevel);
							p += metadata.chunk_size;
							metadata.compressed_size += compressed_chunk_sizes[i] = l;
							baseStream.Write(new(tmp, l));
						}
						l = (int)Oodle.OodleLZ_Compress(metadata.compressor, p, metadata.GetLastChunkSize(), tmp, compressionLevel);
						metadata.compressed_size += compressed_chunk_sizes[last] = l;
						//p += metadata.GetLastChunkSize();
						baseStream.Write(new(tmp, l));
					}
				} finally {
					ArrayPool<byte>.Shared.Return(compressed);
				}
				metadata.size_compressed = metadata.compressed_size;

				baseStream.Position = 0;
				fixed (Header* h = &metadata)
					baseStream.Write(new(h, sizeof(Header)));
				fixed (int* p = compressed_chunk_sizes)
					baseStream.Write(new(p, compressed_chunk_sizes.Length * sizeof(int)));

				baseStream.SetLength((sizeof(int) * 3) + metadata.head_size + metadata.compressed_size);
				baseStream.Flush();
				if (Record != null)
					Record.UncompressedSize = metadata.uncompressed_size;
			} catch {
				Dispose(); // metadata (maybe with baseStream) is broken here
				throw;
			}
		}
		
		protected virtual void EnsureNotDisposed() {
			if (compressed_chunk_sizes == null)
				throw new ObjectDisposedException(nameof(Bundle));
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

		~Bundle() => Dispose();
	}
}