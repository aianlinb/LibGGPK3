using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
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
	/// <summary>
	/// Contains information about the ggpk file
	/// </summary>
	public GGPKRecord Record { get; }
	/// <summary>
	/// Root directory of the tree structure in ggpk
	/// </summary>
	public DirectoryRecord Root { get; }

	/// <summary>
	/// Free spaces in ggpk (don't modify)
	/// </summary>
	public IReadOnlyCollection<FreeRecord> FreeRecords => FreeRecordList;
	protected LinkedList<FreeRecord>? _FreeRecords;
	protected internal LinkedList<FreeRecord> FreeRecordList {
		get {
			EnsureNotDisposed();
			if (_FreeRecords is null) {
				var list = new LinkedList<FreeRecord>();
				var offsets = new HashSet<long>(); // Check for infinite loops
				var NextFreeOffset = Record.FirstFreeRecordOffset;
				while (NextFreeOffset > 0) {
					if (!offsets.Add(NextFreeOffset))
						ThrowHelper.Throw<GGPKBrokenException>(this, "FreeRecordList causes an infinite loops");
					var current = (FreeRecord)ReadRecord(NextFreeOffset);
					list.AddLast(current);
					NextFreeOffset = current.NextFreeOffset;
				}
				_FreeRecords = list;
			}
			return _FreeRecords;
		}
	}

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
		Record = (GGPKRecord)ReadRecord(0);
		Root = (DirectoryRecord)ReadRecord(Record.RootDirectoryOffset);
	}

	/// <summary>
	/// Read a record from GGPK at current stream position
	/// </summary>
	[SkipLocalsInit]
	public virtual unsafe BaseRecord ReadRecord() {
		lock (baseStream) {
			EnsureNotDisposed();
			var buffer = stackalloc int[2];
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
	/// Find the best FreeRecord from <see cref="FreeRecordList"/> to write a Record with length of <paramref name="length"/>
	/// </summary>
	protected internal virtual LinkedListNode<FreeRecord>? FindBestFreeRecord(int length) {
		LinkedListNode<FreeRecord>? bestNode = null; // Find the FreeRecord with most suitable size
		var currentNode = FreeRecordList.First!;
		var remainingSpace = int.MaxValue;
		do {
			if (currentNode.Value.Length == length) {
				bestNode = currentNode;
				//remainingSpace = 0
				break;
			}
			var tmpSpace = currentNode.Value.Length - length;
			if (tmpSpace < remainingSpace && tmpSpace >= 16) {
				bestNode = currentNode;
				remainingSpace = tmpSpace;
			}
			currentNode = currentNode.Next;
		} while (currentNode is not null);
		return bestNode;
	}

	/// <summary>
	/// Compact the ggpk to reduce its size
	/// </summary>
	/// <param name="progress">returns the number of FreeRecords remaining to be filled.
	/// This won't be always decreasing</param>
	public virtual void FastCompact(CancellationToken? cancellation = null, IProgress<int>? progress = null) {
		lock (baseStream) {
			cancellation?.ThrowIfCancellationRequested();
			FreeRecordConcat();

			var freeList = new PriorityQueue<FreeRecord, long>(FreeRecordList.Select(f => (f, f.Offset)));
			progress?.Report(freeList.Count);
			if (freeList.Count == 0)
				return;
			cancellation?.ThrowIfCancellationRequested();
			var treeNodes = TreeNode.RecurseTree(Root).ToList();
			cancellation?.ThrowIfCancellationRequested();
			treeNodes.Sort(Comparer<TreeNode>.Create((x, y) => y.Length.CompareTo(x.Length)));

			while (freeList.TryDequeue(out var free, out _)) {
				progress?.Report(freeList.Count);
				var freeNode = FreeRecordList.Find(free);
				for (var i = treeNodes.Count - 1; i >= 0; --i) {
					cancellation?.ThrowIfCancellationRequested();
					var treeNode = treeNodes[i];
					if (treeNode.Length > free.Length)
						break;
					if (treeNode.Offset < free.Offset)
						continue;
					if (treeNode.Length > free.Length - 16 && treeNode.Length != free.Length)
						continue;

					treeNodes.RemoveAt(i);
					if (treeNode is FileRecord file) {
						var fileContent = file.Read();
						var newFree = file.WriteWithNewLength(file.Length, freeNode)?.Value;
						baseStream.Position = file.DataOffset;
						baseStream.Write(fileContent, 0, fileContent.Length);
						if (newFree is not null && newFree != free)
							freeList.Enqueue(newFree, newFree.Offset);
					} else {
						var newFree = treeNode.WriteWithNewLength(treeNode.Length, freeNode)?.Value;
						if (newFree is not null && newFree != free)
							freeList.Enqueue(newFree, newFree.Offset);
					}
				}
			}
			progress?.Report(freeList.Count);
			baseStream.Flush();
		}
	}

	/// <summary>
	/// Try to fix the broken FreeRecord Linked List
	/// </summary>
	/// <remarks>
	/// Currently not used
	/// </remarks>
	protected virtual void FixFreeRecordList() {
		EnsureNotDisposed();
		lock (baseStream) {
			baseStream.Position = 0;
			Record.FirstFreeRecordOffset = 0;
			var list = new LinkedList<FreeRecord>();
			FreeRecord? last = null;
			while (baseStream.Position < baseStream.Length) {
				var record = ReadRecord();
				if (record is FreeRecord fr) {
					if (last is not null)
						last.NextFreeOffset = fr.Offset;
					else
						Record.FirstFreeRecordOffset = fr.Offset;
					last = fr;
					list.AddLast(last);
				}
			}
			if (last is not null)
				last.NextFreeOffset = 0;
			baseStream.Position = Record.Offset + 20;
			baseStream.Write(Record.FirstFreeRecordOffset);
			foreach (var fr in list) {
				baseStream.Position = fr.Offset + 8;
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
		if (FreeRecordList.Count == 0)
			return;
		lock (baseStream) {
			var list = FreeRecordList.ToList();
			list.Sort(Comparer<FreeRecord>.Create((x, y) => x.Offset.CompareTo(y.Offset)));
			var @continue = true;
			FreeRecord? current = default;
			for (var i = 0; @continue;) {
				var changed = false;
				current = list[i];
				while ((@continue = ++i < list.Count) && current.Offset + current.Length == list[i].Offset) {
					current.Length += list[i].Length;
					list[i].RemoveFromList();
					changed = true;
				}
				if (changed) {
					baseStream.Position = current.Offset;
					baseStream.Write(current.Length);
				}
			}
			if (current is not null && current.Offset + current.Length >= baseStream.Length) {
				baseStream.SetLength(current.Offset);
				current.RemoveFromList();
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
			dirtyHashes.Remove(null!);
			if (!forceRenewRoot)
				dirtyHashes.Remove(Root); // Keep the hash of ROOT original to prevent the game from starting patching
			foreach (var dr in dirtyHashes)
				if (forceRenewRoot || dr.Parent != Root) // Keep the hash of directories under ROOT original to prevent the game from starting patching
					dr.RenewHash();
			dirtyHashes.Clear();
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

	protected virtual void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(!baseStream.CanRead, this);

	public virtual void Dispose() {
		GC.SuppressFinalize(this);
		RenewHashes();
		if (!leaveOpen)
			baseStream.Close();
		if (_FreeRecords is not null) {
			_FreeRecords.Clear();
			_FreeRecords = null;
		}
	}
}