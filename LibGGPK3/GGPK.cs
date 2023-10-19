using LibGGPK3.Records;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LibGGPK3 {
	public class GGPK : IDisposable {
		protected internal readonly Stream baseStream;
		protected bool leaveOpen;
		public GGPKRecord GgpkRecord { get; }
		public DirectoryRecord Root { get; }
		protected LinkedList<FreeRecord>? _FreeRecords;
		public LinkedList<FreeRecord> FreeRecords {
			get {
				EnsureNotDisposed();
				if (_FreeRecords == null) {
					var list = new LinkedList<FreeRecord>();
					var offsets = new HashSet<long>();
					var NextFreeOffset = GgpkRecord.FirstFreeRecordOffset;
					while (NextFreeOffset > 0) {
						if (!offsets.Add(NextFreeOffset))
							throw new("FreeRecordList causes an infinite loops, the GGPK file is broken!");
						var current = (FreeRecord)ReadRecord(NextFreeOffset);
						list.AddLast(current);
						NextFreeOffset = current.NextFreeOffset;
					}
					_FreeRecords = list;
				}
				return _FreeRecords;
			}
		}

		/// <param name="filePath">Path to Content.ggpk</param>
		/// <exception cref="FileNotFoundException" />
		public GGPK(string filePath) : this(File.Open(Extensions.ExpandPath(filePath), new FileStreamOptions() {
			Mode = FileMode.Open,
			Access = FileAccess.ReadWrite,
			Share = FileShare.Read,
			Options = FileOptions.RandomAccess
		})) { }

		/// <param name="stream">Stream of Content.ggpk</param>
		/// <param name="leaveOpen">If false, close the <paramref name="stream"/> after this instance has been disposed</param>
		public GGPK(Stream stream, bool leaveOpen = false) {
			this.leaveOpen = leaveOpen;
			baseStream = stream ?? throw new ArgumentNullException(nameof(stream));
			GgpkRecord = (GGPKRecord)ReadRecord(0);
			Root = (DirectoryRecord)ReadRecord(GgpkRecord.RootDirectoryOffset);
		}

		/// <summary>
		/// Read a record from GGPK at current stream position
		/// </summary>
		[SkipLocalsInit]
		public unsafe virtual BaseRecord ReadRecord() {
			EnsureNotDisposed();
			var buffer = stackalloc int[2];
			baseStream.Read(new(buffer, sizeof(int) + sizeof(int)));
			var length = *buffer;
			return buffer[1] switch {
				FileRecord.Tag => new FileRecord(length, this),
				DirectoryRecord.Tag => new DirectoryRecord(length, this),
				FreeRecord.Tag => new FreeRecord(length, this),
				GGPKRecord.Tag => new GGPKRecord(length, this),
				_ => throw new Exception($"Invalid record tag at offset: {baseStream.Position - sizeof(int)}\r\nThe Content.ggpk may be broken")
			};
		}

		/// <summary>
		/// Read a record from GGPK with <paramref name="offset"/> in bytes
		/// </summary>
		/// <param name="offset">Record offset, null for current stream position</param>
		public virtual BaseRecord ReadRecord(long offset) {
			baseStream.Position = offset;
			return ReadRecord();
		}

		/// <summary>
		/// Find the record with a <paramref name="path"/>
		/// </summary>
		/// <param name="path">Relative path (with forward slash) in GGPK (which not start or end with slash) under <paramref name="root"/></param>
		/// <param name="node">The node found, or null when not found, or <paramref name="root"/> if <paramref name="path"/> is empty</param>
		/// <param name="root">Node to start searching, or null for <see cref="Root"/></param>
		/// <returns>Whether found a node</returns>
		public virtual bool TryFindNode(string path, [NotNullWhen(true)] out TreeNode? node, DirectoryRecord? root = null) {
			EnsureNotDisposed();
			root ??= Root;
			if (path == string.Empty) {
				node = root;
				return true;
			}
			var splittedPath = path.Split('/'); // TODO: .Net 8 ReadOnlySpan<char>.Split()
			foreach (var name in splittedPath) {
				var next = root[name];
				if (next is not DirectoryRecord dr)
					return (node = next) != null;
				root = dr;
			}
			node = root;
			return true;
		}

		/// <summary>
		/// Find the record with a <paramref name="path"/>, or create it if not found
		/// </summary>
		/// <param name="path">Relative path (with forward slash) in GGPK (which not start or end with slash) under <paramref name="root"/></param>
		/// <param name="root">Node to start searching, or null for <see cref="Root"/></param>
		/// <returns>The node found</returns>
		public virtual DirectoryRecord FindOrCreateDirectory(string path, DirectoryRecord? root = null) {
			EnsureNotDisposed();
			root ??= Root;
			if (path == string.Empty)
				return root;
			var splittedPath = path.Split('/'); // TODO: .Net 8 ReadOnlySpan<char>.Split()
			foreach (var name in splittedPath) {
				var next = root[name];
				if (next is DirectoryRecord dr)
					root = dr;
				else if (next == null)
					root = root.AddDirectory(name);
				else if (next is FileRecord fr)
					throw new("Cannot create directory \"" + fr.GetPath() + "\" because there is a file with the same name");
				else
					throw new("Unknown TreeNode type:" + next.ToString());
			}
			return root;
		}

		/// <summary>
		/// Find the best FreeRecord from <see cref="FreeRecords"/> to write a Record with length of <paramref name="length"/>
		/// </summary>
		protected internal virtual LinkedListNode<FreeRecord>? FindBestFreeRecord(int length) {
			LinkedListNode<FreeRecord>? bestNode = null; // Find the FreeRecord with most suitable size
			var currentNode = FreeRecords.First!;
			var remainingSpace = int.MaxValue;
			do {
				if (currentNode.Value.Length == length) {
					bestNode = currentNode;
					//remainingSpace = 0;
					break;
				}
				var tmpSpace = currentNode.Value.Length - length;
				if (tmpSpace < remainingSpace && tmpSpace >= 16) {
					bestNode = currentNode;
					remainingSpace = tmpSpace;
				}
			} while ((currentNode = currentNode.Next) != null);
			return bestNode;
		}

		/// <summary>
		/// Compact the ggpk to reduce its size
		/// </summary>
		/// <param name="progress">returns the number of FreeRecords remaining to be filled.
		/// This won't be always decreasing</param>
		public virtual Task FastCompactAsync(CancellationToken? cancellation = null, IProgress<int>? progress = null) {
			return Task.Run(() => {
				cancellation?.ThrowIfCancellationRequested();
				FreeRecordConcat();
				cancellation?.ThrowIfCancellationRequested();

				var freeList = new PriorityQueue<FreeRecord, long>(FreeRecords.Select(f => (f, f.Offset)));
				progress?.Report(freeList.Count);
				if (freeList.Count == 0)
					return;
				cancellation?.ThrowIfCancellationRequested();
				var treeNodes = TreeNode.RecurseTree(Root).ToList();
				treeNodes.Sort(Comparer<TreeNode>.Create((x, y) => y.Length.CompareTo(x.Length)));
				cancellation?.ThrowIfCancellationRequested();

				while (freeList.TryDequeue(out var free, out _)) {
					progress?.Report(freeList.Count);
					var freeNode = FreeRecords.Find(free);
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
							if (newFree != null && newFree != free)
								freeList.Enqueue(newFree, newFree.Offset);
						} else {
							var newFree = treeNode.WriteWithNewLength(treeNode.Length, freeNode)?.Value;
							if (newFree != null && newFree != free)
								freeList.Enqueue(newFree, newFree.Offset);
						}
					}
				}
				progress?.Report(freeList.Count);
			});
		}

		/// <summary>
		/// Try to fix the broken FreeRecord Linked List
		/// </summary>
		public virtual void FixFreeRecordList() {
			EnsureNotDisposed();
			var s = baseStream;
			baseStream.Position = 0;
			GgpkRecord.FirstFreeRecordOffset = 0;
			var list = new LinkedList<FreeRecord>();
			FreeRecord? last = null;
			while (s.Position < s.Length) {
				var record = ReadRecord();
				if (record is FreeRecord fr) {
					if (last != null)
						last.NextFreeOffset = fr.Offset;
					else
						GgpkRecord.FirstFreeRecordOffset = fr.Offset;
					last = fr;
					list.AddLast(last);
				}
			}
			if (last != null)
				last.NextFreeOffset = 0;
			baseStream.Position = GgpkRecord.Offset + 20;
			s.Write(GgpkRecord.FirstFreeRecordOffset);
			foreach (var fr in list) {
				baseStream.Position = fr.Offset + 8;
				s.Write(fr.NextFreeOffset);
			}
			s.Flush();
		}

		/// <summary>
		/// Merge all adjacent FreeRecords
		/// </summary>
		protected virtual void FreeRecordConcat() {
			EnsureNotDisposed();
			if (FreeRecords.Count == 0)
				return;
			var list = FreeRecords.ToList();
			list.Sort(Comparer<FreeRecord>.Create((x, y) => x.Offset.CompareTo(y.Offset)));
			var continu = true;
			FreeRecord? current = default;
			for (var i = 0; continu;) {
				var changed = false;
				current = list[i];
				while ((continu = ++i < list.Count) && current.Offset + current.Length == list[i].Offset) {
					current.Length += list[i].Length;
					list[i].RemoveFromList();
					changed = true;
				}
				if (changed) {
					baseStream.Position = current.Offset;
					baseStream.Write(current.Length);
				}
			}
			if (current != null && current.Offset + current.Length >= baseStream.Length) {
				baseStream.Flush();
				baseStream.SetLength(current.Offset);
				current.RemoveFromList();
			}
		}

		/// <summary>
		/// Extract files under a node
		/// </summary>
		/// <param name="record">Node to extract</param>
		/// <param name="path">Path to save</param>
		/// <returns>Number of files extracted</returns>
		public static int Extract(TreeNode record, string path) {
			path = $"{path}/{record.Name}";
			if (record is FileRecord fr) {
				File.WriteAllBytes(path, fr.Read());
				return 1;
			} else {
				var count = 0;
				Directory.CreateDirectory(path);
				foreach (var f in ((DirectoryRecord)record).Children)
					count += Extract(f, path);
				return count;
			}
		}

		/// <summary>
		/// Replace files under a node
		/// </summary>
		/// <param name="record">Node to replace</param>
		/// <param name="path">Path to read files to replace</param>
		/// <returns>Number of files replaced</returns>
		public static int Replace(TreeNode record, string path) {
			if (record is FileRecord fr) {
				if (!File.Exists(path))
					return 0;
				fr.Write(File.ReadAllBytes(path));
				return 1;
			} else {
				if (!Directory.Exists(path))
					return 0;
				return ((DirectoryRecord)record).Children.Sum(r => Replace(r, $"{path}/{r.Name}"));
			}
		}

		protected virtual void EnsureNotDisposed() {
			if (!baseStream.CanRead)
				throw new ObjectDisposedException(nameof(GGPK));
		}

		public virtual void Dispose() {
			GC.SuppressFinalize(this);
			try {
				if (!leaveOpen)
					baseStream?.Close();
			} catch { /*Closing closed stream*/ }
			_FreeRecords?.Clear();
			_FreeRecords = null;
		}

		~GGPK() {
			Dispose();
		}
	}
}