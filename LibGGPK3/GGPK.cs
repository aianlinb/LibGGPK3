using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using LibGGPK3.Records;

using SystemExtensions;
using SystemExtensions.Collections;
using SystemExtensions.Spans;
using SystemExtensions.Streams;

[module: SkipLocalsInit]
[assembly: InternalsVisibleTo("LibBundledGGPK3")]

namespace LibGGPK3;
/// <summary>
/// Class to handle the Content.ggpk file.
/// </summary>
public class GGPK : IDisposable {
	protected internal readonly Stream baseStream;
	protected readonly bool leaveOpen;
	protected internal readonly HashSet<DirectoryRecord> dirtyHashes = [];

	public long Position {
		get {
			EnsureNotDisposed();
			return baseStream.Position;
		}
	}
	public long Length {
		get {
			EnsureNotDisposed();
			return baseStream.Length;
		}
	}

	/// <summary>
	/// Version of format of this ggpk file.
	/// 3 for PC, 4 for Mac, 2 for gmae-version before 3.11.2 which has no bundle in ggpk.
	/// </summary>
	public uint Version => Record.GGPKVersion;
	/// <summary>
	/// Contains information about the ggpk file
	/// </summary>
	protected internal GGPKRecord Record { get; }
	/// <summary>
	/// Root directory of the tree structure in ggpk
	/// </summary>
	public DirectoryRecord Root { get; }

	protected FreeRecord? _FirstFreeRecord;
	/// <summary>
	/// First FreeRecord in linked-list
	/// </summary>
	public FreeRecord? FirstFreeRecord {
		get {
			if (_FirstFreeRecord is null && Record.FirstFreeRecordOffset != 0)
				_FirstFreeRecord = (FreeRecord)ReadRecord(Record.FirstFreeRecordOffset);
			return _FirstFreeRecord;
		}
		protected internal set {
			if (value is null)
				Record.FirstFreeRecordOffset = 0;
			else {
				Record.FirstFreeRecordOffset = value.Offset;
				if (value.Previous != null)
					value.Previous.Next = null;
			}
			_FirstFreeRecord = value;
		}
	}

	/// <summary>Free spaces in ggpk</summary>
	/// <remarks>Note that you shouldn't cache the results, as they may be changed or become invalid with any write operation</remarks>
	public virtual IEnumerable<FreeRecord> FreeRecords {
		get {
			var free = FirstFreeRecord;
			while (free is not null) {
				if (!free.IsInvalid) // just in case
					yield return free;
				free = free.Next;
			}
		}
	}

	protected internal List<FreeRecord>? _SortedFreeRecords;
	protected virtual internal List<FreeRecord> SortedFreeRecords => _SortedFreeRecords ??= [..FreeRecords.OrderBy(f => f.Length)];

	/// <param name="filePath">Path to Content.ggpk on disk</param>
	/// <exception cref="FileNotFoundException" />
	public GGPK(string filePath) : this(File.Open(Utils.ExpandPath(filePath), new FileStreamOptions() {
		Mode = FileMode.Open,
		Access = FileAccess.ReadWrite,
		Share = FileShare.Read,
		Options = FileOptions.RandomAccess
	})) { }

	/// <param name="stream">Stream of the Content.ggpk file</param>
	/// <param name="leaveOpen">If false, close the <paramref name="stream"/> when this instance is disposed</param>
	public GGPK(Stream stream, bool leaveOpen = false) {
		ArgumentNullException.ThrowIfNull(stream);
		if (!BitConverter.IsLittleEndian)
			ThrowHelper.Throw<NotSupportedException>("Big-endian architecture is not supported");
		baseStream = stream;
		this.leaveOpen = leaveOpen;
		try {
			Record = (GGPKRecord)ReadRecord(0);
			Root = (DirectoryRecord)ReadRecord(Record.RootDirectoryOffset);
		} catch {
			Dispose();
			throw;
		}
	}

	/// <summary>
	/// Read a record from GGPK at current stream position
	/// </summary>
	[SkipLocalsInit]
	public virtual unsafe BaseRecord ReadRecord() {
		EnsureNotDisposed();
		lock (baseStream) {
			var buffer = stackalloc uint[2];
			baseStream.ReadExactly(new(buffer, sizeof(int) + sizeof(int)));
			var length = *buffer;
			return buffer[1] switch {
				FileRecord.Tag => new FileRecord(length, this),
				DirectoryRecord.Tag => new DirectoryRecord(length, this),
				FreeRecord.Tag => new FreeRecord(length, this),
				GGPKRecord.Tag => new GGPKRecord(length, this),
				_ => throw new GGPKBrokenException(this, $"Invalid record tag at offset: {baseStream.Position - sizeof(int)}")
			};
		}
	}

	/// <summary>
	/// Read a record from GGPK with <paramref name="offset"/> in bytes
	/// </summary>
	/// <param name="offset">Record offset, null for current stream position</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public virtual BaseRecord ReadRecord(long offset) {
		lock (baseStream) {
			baseStream.Position = offset;
			return ReadRecord();
		}
	}

	/// <summary>
	/// Find the most suitable FreeRecord from <see cref="FreeRecords"/> to write a <see cref="BaseRecord"/> with length of <paramref name="length"/>,
	/// or <see langword="null"/> if no one found (in this case, write at the end of the ggpk instead).
	/// </summary>
	/// <param name="length"><see cref="BaseRecord.Length"/></param>
	/// <param name="maxOffset">Limit the <see cref="BaseRecord.Offset"/> of returned <see cref="FreeRecord"/> to less than <paramref name="maxOffset"/></param>
	protected internal virtual FreeRecord? FindBestFreeRecord(uint length, long maxOffset = long.MaxValue) {
		if (length == 0U || maxOffset <= 0)
			return null;

		FreeRecord result;
		var list = SortedFreeRecords;
		var span = CollectionsMarshal.AsSpan(list);
		var i = span.BinarySearch(new BaseRecord.LengthWrapper(length));
		if (i >= 0) { // Same length
			var i2 = i;
			result = span[i];
			do {
				if (result.Offset < maxOffset)
					goto found;
			} while (++i != span.Length && (result = span[i]).Length == length);
			while (--i2 != -1 && (result = span[i2]).Length == length) {
				if (result.Offset < maxOffset) {
					i = i2;
					goto found;
				}
			}
		} else
			i = ~i;

		if (i == span.Length)
			return null; // Not found

		result = span[i];
		// The result length must equal to or 16 larger than the required length (For the remaining FreeRecord)
		uint diff;
		while ((diff = result.Length - length) < 16U || diff > result.Length || result.Offset >= maxOffset) {
			if (++i == span.Length)
				return null; // Not found
			result = span[i];
			// Found
		}

	found:
		Debug.Assert((result.Length == length || result.Length >= length + 16U) && result.Offset < maxOffset);
		if (result.IsInvalid) { // just in case
			list.RemoveAt(i);
			return FindBestFreeRecord(length);
		}
		return result;
	}

	/// <summary>
	/// Compact the ggpk to reduce its size
	/// </summary>
	/// <param name="progress">
	/// returns the number of FreeRecords remaining to be filled.
	/// This won't be always decreasing.
	/// </param>
	public void FastCompact(CancellationToken? cancellation = null, IProgress<int>? progress = null) {
		cancellation?.ThrowIfCancellationRequested();
		lock (baseStream) {
			FreeRecordConcat();
			cancellation?.ThrowIfCancellationRequested();

			var freeList = new PriorityQueue<FreeRecord, long>((_SortedFreeRecords ?? FreeRecords).Select(f => (f, f.Offset)));
			progress?.Report(freeList.Count);
			if (freeList.Count == 0)
				return;
			cancellation?.ThrowIfCancellationRequested();

			var treeNodes = TreeNode.RecurseTree(Root).OrderBy(t => t.Length).ThenBy(t => t.Offset).ToList();
			cancellation?.ThrowIfCancellationRequested();

			FreeRecord? free;
			uint lastFreeLength = 0U;
			int i = -1;
			void SearchNext() {
				Debug.Assert(i < treeNodes.Count);
				uint len = free.Length;
				int count = len < lastFreeLength ? i + 1 : treeNodes.Count;
				i = CollectionsMarshal.AsSpan(treeNodes).SliceUnchecked(0, count).BinarySearch(new BaseRecord.LengthWrapper(len));
				i = CollectionsMarshal.AsSpan(treeNodes).BinarySearch(new BaseRecord.LengthWrapper(len));
				if (i < 0) {
					i = ~i;
					len -= 16U; // Try find TreeNode with Length <= free.Length - 16U
					while (--i >= 0 && treeNodes[i].Length > len) { }
				} else { // found exactly same length
					while (++i < treeNodes.Count && treeNodes[i].Length == len) { }
					--i;
				}
				// found == i != -1
			}

			using var renter = new ArrayPoolRenter<byte>();
			while (freeList.TryDequeue(out free, out _)) {
				if (free.IsInvalid)
					continue;
				progress?.Report(freeList.Count);

				SearchNext();
				while (i != -1) {
					if (cancellation?.IsCancellationRequested ?? false) {
						baseStream.Flush();
						cancellation.Value.ThrowIfCancellationRequested();
					}

					var treeNode = treeNodes[i];
					if (treeNode.Offset < free.Offset) {
						--i;
						continue;
					}
					lastFreeLength = free.Length;
					if (treeNode.Length != lastFreeLength && treeNode.Length > lastFreeLength - 16U) {
						--i;
						continue;
					}

					// Move the TreeNode to the FreeRecord
					FreeRecord? newFree;
					if (treeNode is FileRecord file) {
						renter.Resize(file.DataLength);
						file.Read(renter.Array);
						newFree = treeNode.WriteWithNewLength(treeNode.Length, free);
						baseStream.Position = file.DataOffset;
						baseStream.Write(renter.Array, 0, file.DataLength);
					} else
						newFree = treeNode.WriteWithNewLength(treeNode.Length, free);

					treeNodes.RemoveAt(i);

					if (newFree is not null)
						freeList.Enqueue(newFree, newFree.Offset);

					if (free.IsInvalid)
						break;
					SearchNext(); // Reset search index as free.Length might have changed
				}
				lastFreeLength = 0U; // Reset search state because we are going to use another FreeRecord
			}
			progress?.Report(freeList.Count);

			FreeRecordConcat();
		}
	}

	/// <summary>
	/// Try to fix the broken FreeRecord Linked List
	/// </summary>
	/// <remarks>
	/// Currently not used
	/// </remarks>
	public void FixFreeRecordList() {
		EnsureNotDisposed();
		lock (baseStream) {
			// Clear cache
			FirstFreeRecord = null;
			_SortedFreeRecords = null;

			// Scan entire ggpk
			FreeRecord? last = null;
			baseStream.Position = 0;
			while (baseStream.Position < baseStream.Length) {
				var record = ReadRecord();
				if (record is FreeRecord fr) {
					if (last is not null)
						last.Next = fr;
					else
						FirstFreeRecord = fr;
					last = fr;
				}
			}
			if (last is not null)
				last.Next = null;

			// Write back to ggpk
			baseStream.Position = Record.Offset + (sizeof(long) * 2 + sizeof(int));
			baseStream.Write(Record.FirstFreeRecordOffset);
			foreach (var fr in FreeRecords) {
				baseStream.Position = fr.Offset + sizeof(long);
				baseStream.Write(fr.NextFreeOffset);
			}
			baseStream.Flush();
		}
	}

	/// <summary>
	/// Merge all adjacent FreeRecords
	/// </summary>
	protected virtual void FreeRecordConcat() {
		EnsureNotDisposed();
		lock (baseStream) {
			var list = new List<FreeRecord>(_SortedFreeRecords ?? FreeRecords); // make copy
			if (list.Count <= 1)
				return;
			list.Sort(new BaseRecord.OffsetComparer()); // Sort by Offset

			FreeRecord? current = default;
			var i = 0;
			bool @continue = i < list.Count;
			while (@continue) {
				current = list[i];
				var currentLength = current.Length;
				if (current.IsInvalid || currentLength >= int.MaxValue) {
					@continue = ++i < list.Count;
					continue;
				}

				while ((@continue = ++i < list.Count) && current.Offset + currentLength == list[i].Offset) {
					uint newLen = currentLength + list[i].Length;
					if (newLen < currentLength || newLen >= int.MaxValue) // Overflow or large than int
						break;
					currentLength = newLen;
					list[i].RemoveFromList();
				}
				if (current.Length != currentLength) {
					current.UpdateLength(currentLength);
					baseStream.Position = current.Offset;
					baseStream.Write(currentLength);
				}
			}
			if (current is not null && current.Offset + current.Length >= baseStream.Length) {
				do {
					baseStream.SetLength(current.Offset);
					current.RemoveFromList();
				} while (--i >= 0 && (current = list[i]).Offset + current.Length >= baseStream.Length);
			}
			baseStream.Flush();
		}
	}

	/// <summary>
	/// Extract files under a node recursively to a <paramref name="path"/> on disk.
	/// </summary>
	/// <param name="record">Node to extract</param>
	/// <param name="path">Path to save</param>
	/// <param name="callback">
	/// Optional function to be called right after extracting each file.
	/// <para>Provides the file extracted and its full path on disk.</para>
	/// <para>Return <see langword="true"/> to cancel processing remaining files.</para>
	/// </param>
	/// <returns>Number of files extracted.</returns>
	public static int Extract(TreeNode record, string path, Func<FileRecord, string, bool>? callback = null) {
		Task? lastTask = null;
		using var renter1 = new ArrayPoolRenter<byte>();
		using var renter2 = new ArrayPoolRenter<byte>();
		var renter = renter1;

		FileRecord lastFr = null!;
		string lastPath = null!;
		int ExtractRecursive(TreeNode record, string path) {
			path = $"{path}/{record.Name}";
			if (record is FileRecord fr) {
				if (renter.Array.Length < fr.DataLength)
					renter.Resize(fr.DataLength);
				fr.Read(renter.Array);
				if (lastTask is not null) {
					lastTask.GetAwaiter().GetResult();
					if (callback?.Invoke(lastFr, lastPath) ?? false)
						return 0;
				}
				lastTask = SystemExtensions.System.IO.File.WriteAllBytesAsync(path, new(renter.Array, 0, fr.DataLength)).AsTask();
				renter = renter == renter1 ? renter2 : renter1;
				lastFr = fr;
				lastPath = path;
				return 1;
			} else {
				var count = 0;
				path = Directory.CreateDirectory(path).FullName;
				foreach (var f in (DirectoryRecord)record)
					count += ExtractRecursive(f, path);
				return count;
			}
		}

		var result = ExtractRecursive(record, Path.GetFullPath(path));
		if (lastTask is not null) {
			lastTask.GetAwaiter().GetResult();
			callback?.Invoke(lastFr, lastPath);
		}
		return result;
	}

	/// <summary>
	/// Replace files under a node recursively from a <paramref name="path"/> on disk.
	/// </summary>
	/// <param name="record">Node to replace</param>
	/// <param name="path">Path to read files to replace</param>
	/// <param name="callback">
	/// Optional function to be called right after replacing each file.
	/// <para>Provides the file replaced and its full path on disk.</para>
	/// <para>Return <see langword="true"/> to cancel processing remaining files.</para>
	/// </param>
	/// <returns>Number of files replaced.</returns>
	public static int Replace(TreeNode record, string path, Func<FileRecord, string, bool>? callback = null) {
		Task? lastTask = null;

		FileRecord lastFr = null!;
		string lastPath = null!;
		int ReplaceRecursive(TreeNode record, string path) {
			if (record is FileRecord fr) {
				if (!File.Exists(path))
					return 0;
				var b = File.ReadAllBytes(path);
				if (lastTask is not null) {
					lastTask.GetAwaiter().GetResult();
					if (callback?.Invoke(lastFr, lastPath) ?? false)
						return 0;
				}
				lastTask = Task.Run(() => fr.Write(b));
				lastFr = fr;
				lastPath = path;
				return 1;
			} else {
				if (!Directory.Exists(path))
					return 0;
				var count = 0;
				foreach (var r in (DirectoryRecord)record)
					count += ReplaceRecursive(r, $"{path}/{r.Name}");
				return count;
			}
		}

		var result = ReplaceRecursive(record, Path.GetFullPath(path));
		if (lastTask is not null) {
			lastTask.GetAwaiter().GetResult();
			callback?.Invoke(lastFr, lastPath);
		}
		return result;
	}
	/// <summary>
	/// Replace files under a node recursively from <paramref name="zipEntries"/>.
	/// </summary>
	/// <param name="root">Node to replace</param>
	/// <param name="zipEntries">Entries to read files to replace</param>
	/// <param name="callback">
	/// Optional function to be called right after replacing each file.
	/// <para>Provides the file replaced and its full path on disk,
	/// and a <see cref="bool"/> indicating whether the file is added (<see langword="false"/> for replaced).</para>
	/// <para>Return <see langword="true"/> to cancel processing remaining files.</para>
	/// </param>
	/// <param name="allowAdd">Allow adding new files to ggpk.
	/// <see langword="false"/> to throw <see cref="FileNotFoundException"/> when a file in <paramref name="zipEntries"/> is not found in ggpk.</param>"
	/// <returns>Number of files replaced.</returns>
	/// <exception cref="FileNotFoundException">When a file in <paramref name="zipEntries"/> is not found in ggpk, and <paramref name="allowAdd"/> is <see langword="false"/></exception>
	public static int Replace(DirectoryRecord root, IEnumerable<ZipArchiveEntry> zipEntries, Func<FileRecord, string, bool, bool>? callback = null, bool allowAdd = false) {
		Task<(FileRecord, string, bool)>? lastTask = null;
		using var renter1 = new ArrayPoolRenter<byte>();
		using var renter2 = new ArrayPoolRenter<byte>();
		var renter = renter1;

		var count = 0;
		foreach (var e in zipEntries) {
			if (e.FullName.EndsWith('/')) // dir
				continue;

			var len = (int)e.Length;
			if (renter.Array.Length < len)
				renter.Resize(len);
			using (var s = e.Open())
				len = s.ReadAtLeast(renter.Array, len);
			if (lastTask is not null) {
				var (fr, path, added) = lastTask.GetAwaiter().GetResult();
				if (callback?.Invoke(fr, path, added) ?? false)
					break;
			}
			var array = renter.Array;
			lastTask = Task.Run(() => {
				FileRecord? fr;
				var added = false;
				if (allowAdd)
					added = root.FindOrAddFile(e.FullName, out fr, len);
				else if (!root.TryFindNode(e.FullName, out var node) || (fr = node as FileRecord) is null)
					throw ThrowHelper.Create<FileNotFoundException>($"Could not found file in ggpk with path: {root.GetPath()}{e.FullName}");
				fr.Write(new(array, 0, len));
				++count;
				return (fr, e.FullName, added);
			});
			renter = renter == renter1 ? renter2 : renter1;
		}
		if (lastTask is not null) {
			var (fr, path, added) = lastTask.GetAwaiter().GetResult();
			callback?.Invoke(fr, path, added);
		}
		return count;
	}

	/// <summary>
	/// Renew the hashes of all directories after modification.
	/// </summary>
	/// <param name="forceRenewRoot">
	/// The Hash of <see cref="Root"/> and its children won't be renew by default, <see langword="true"/> to force renew them
	/// (this will cause the game to start patching on startup and revert all modifications to ggpk).
	/// </param>
	/// <remarks>
	/// <para>This will be automatically called when <see cref="Dispose"/>.</para>
	/// <para>Only modifications on this instance will be tracked, this method does not apply retroactively.</para>
	/// </remarks>
	public virtual void RenewHashes(bool forceRenewRoot = false) {
		EnsureNotDisposed();
		lock (baseStream) {
			dirtyHashes.Remove(null!); // Parent of Root
			var count = 0;
			while (dirtyHashes.Count != count) {
				count = dirtyHashes.Count;
				foreach (var dr in dirtyHashes.ToArray()) { // Make a copy

					if (forceRenewRoot || dr != Root && dr.Parent != Root) { // Keep the hash of Root and directories under Root original to prevent the game from starting patching
						dr.RenewHash(); // Will remove itself from dirtyHashes
						dirtyHashes.Add(dr.Parent!); // Process in next round
					}
				}
				dirtyHashes.Remove(null!);
			};
		}
	}

	/// <summary>
	/// Erase the hashes of <see cref="Root"/> and its children.
	/// </summary>
	/// <remarks>This will cause the game to start patching on startup
	/// and revert all modifications to ggpk from this library.</remarks>
	public virtual void EraseRootHash() {
		EnsureNotDisposed();
		lock (baseStream) {
			foreach (var d in Root) {
				d._Hash = default;
				d.WriteWithNewLength(d.Length);
			}
			Root._Hash = default;
			Root.WriteWithNewLength(Root.Length);
		}
	}

	public virtual void Flush() {
		RenewHashes();
		baseStream.Flush();
	}

	protected virtual void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(!baseStream.CanRead, this);

	/// <summary>
	/// Get the field of the base stream of this instance.
	/// Using this method may cause dangerous unexpected behavior.
	/// </summary>
	[EditorBrowsable(EditorBrowsableState.Advanced)]
	public ref Stream UnsafeGetStream() {
		return ref Unsafe.AsRef(in baseStream);
	}

	public virtual void Dispose() {
		GC.SuppressFinalize(this);
		if (baseStream is null || !baseStream.CanWrite)
			return;
		Flush();
		if (!leaveOpen)
			baseStream.Close();
	}
}