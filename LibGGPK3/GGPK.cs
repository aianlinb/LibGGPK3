using LibGGPK3.Records;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LibGGPK3 {
	public class GGPK : IDisposable {
		protected internal Stream GGPKStream;
		protected bool LeaveOpen;
		public GGPKRecord GgpkRecord { get; }
		public DirectoryRecord Root { get; }
		protected LinkedList<FreeRecord>? _FreeRecords;
		public LinkedList<FreeRecord> FreeRecords {
			get {
				if (_FreeRecords == null) {
					var list = new LinkedList<FreeRecord>();
					var offsets = new HashSet<long>();
					var NextFreeOffset = GgpkRecord.FirstFreeRecordOffset;
					while (NextFreeOffset > 0) {
						if (!offsets.Add(NextFreeOffset))
							throw new("FreeRecordList causes an infinite loops, your GGPK file is broken!");
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
		public GGPK(string filePath) : this(File.Open(Extensions.ExpandPath(filePath), FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) {
		}

		/// <param name="stream">Stream of Content.ggpk</param>
		/// <param name="leaveOpen">If false, close the <paramref name="stream"/> after this instance has been disposed</param>
		public GGPK(Stream stream, bool leaveOpen = false) {
			LeaveOpen = leaveOpen;
			GGPKStream = stream;
			GgpkRecord = (GGPKRecord)ReadRecord(0);
			Root = (DirectoryRecord)ReadRecord(GgpkRecord.RootDirectoryOffset);
		}

		/// <summary>
		/// Read a record from GGPK at current stream position
		/// </summary>
		[SkipLocalsInit]
		public unsafe virtual BaseRecord ReadRecord() {
			var buffer = stackalloc byte[8];
			GGPKStream.Read(new(buffer, 8));
			var length = *(int*)buffer;
			return ((uint*)buffer)[1] switch {
				FileRecord.Tag => new FileRecord(length, this),
				DirectoryRecord.Tag => new DirectoryRecord(length, this),
				FreeRecord.Tag => new FreeRecord(length, this),
				GGPKRecord.Tag => new GGPKRecord(length, this),
				_ => throw new Exception("Invalid record tag at offset: " + (GGPKStream.Position - 4))
			};
		}

		/// <summary>
		/// Read a record from GGPK with <paramref name="offset"/> in bytes
		/// </summary>
		/// <param name="offset">Record offset, null for current stream position</param>
		public virtual BaseRecord ReadRecord(long offset) {
			GGPKStream.Seek(offset, SeekOrigin.Begin);
			return ReadRecord();
		}

		/// <summary>
		/// Find the record with a <paramref name="path"/>
		/// </summary>
		/// <param name="path">Relative path in GGPK under <paramref name="root"/></param>
		/// <param name="root">Where to start searching, null for ROOT directory in GGPK</param>
		/// <returns>null if not found</returns>
		public virtual TreeNode? FindNode(string path, DirectoryRecord? root = null) {
			root ??= Root;
			var SplittedPath = path.Split('/', '\\');
			foreach (var name in SplittedPath) {
				if (name == "")
					continue;
				var next = root[TreeNode.GetNameHash(name)];
				if (next is not DirectoryRecord dr)
					return next;
				root = dr;
			}
			return root;
		}

		/// <summary>
		/// Find the best FreeRecord from <see cref="GGPK.FreeRecords"/> to write a Record with length of <paramref name="length"/>
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
				var treeNodes = RecursiveTree(Root).ToList();
				cancellation?.ThrowIfCancellationRequested();
				treeNodes.Sort(Comparer<TreeNode>.Create((x, y) => y.Length.CompareTo(x.Length)));

				while (freeList.TryDequeue(out var free, out _)) {
					progress?.Report(freeList.Count);
					cancellation?.ThrowIfCancellationRequested();
					var freeNode = FreeRecords.Find(free);
					for (var i = treeNodes.Count - 1; i >= 0; --i) {
						var treeNode = treeNodes[i];
						if (treeNode.Length > free.Length)
							break;
						if (treeNode.Offset < free.Offset)
							continue;
						if (treeNode.Length != free.Length && treeNode.Length > free.Length - 16)
							continue;

						treeNodes.RemoveAt(i);
						if (treeNode is FileRecord file) {
							var fileContent = file.ReadFileContent();
							var newFree = file.MoveWithNewLength(file.Length, freeNode)?.Value;
							GGPKStream.Seek(file.DataOffset, SeekOrigin.Begin);
							GGPKStream.Write(fileContent);
							if (newFree != null && newFree != free)
								freeList.Enqueue(newFree, newFree.Offset);
						} else {
							var newFree = treeNode.MoveWithNewLength(treeNode.Length, freeNode)?.Value;
							if (newFree != null && newFree != free)
								freeList.Enqueue(newFree, newFree.Offset);
						}
					}
				}
				progress?.Report(freeList.Count);
			});
		}

		/// <summary>
		/// Full defragment the ggpk to remove all FreeRecords to reduce its size to the smallest possible size and save it to <paramref name="pathToSave"/>
		/// </summary>
		/// <param name="progress">returns the number of Records remaining to be written.</param>
		public virtual async Task FullCompactAsync(string pathToSave, CancellationToken? cancellation = null, IProgress<int>? progress = null, IList<TreeNode>? nodes = null) {
			nodes ??= await Task.Run(() => RecursiveTree(Root).ToList()).ConfigureAwait(false);
			var lengths = GgpkRecord.Length + nodes!.Sum(n => (long)n.Length);
			if (new DriveInfo(pathToSave[0..1]).AvailableFreeSpace < lengths)
				throw new IOException("Not enough disk space, " + lengths + " Bytes required");
			var f = File.Create(pathToSave);
			try {
				f.SetLength(lengths);
				await FullCompactAsync(f, cancellation, progress, nodes);
			} finally {
				f.Close();
			}
		}

		/// <summary>
		/// Full defragment the ggpk to remove all FreeRecords to reduce its size to the smallest possible size and save it to <paramref name="streamToSave"/>
		/// </summary>
		/// <param name="progress">returns the number of Records remaining to be written.</param>
		/// <param name="nodes">nodes to save</param>
		public virtual Task FullCompactAsync(Stream streamToSave, CancellationToken? cancellation = null, IProgress<int>? progress = null, IList<TreeNode>? nodes = null) {
			return Task.Run(() => {
				var oldStream = GGPKStream;
				try {
					cancellation?.ThrowIfCancellationRequested();
					GGPKStream.Flush();
					if (nodes != null) {
						var root = false;
						foreach (var node in nodes) {
							if (node.Ggpk != this)
								throw new ArgumentException("One of the provided record not belongs to this GGPK instance", nameof(nodes));
							if (node == Root)
								root = true;
						}
						if (!root)
							throw new ArgumentException("The provided nodes contains no Root node", nameof(nodes));
					} else
						nodes = RecursiveTree(Root).ToList();

					var count = nodes.Count;
					progress?.Report(count + 1);
					cancellation?.ThrowIfCancellationRequested();
					GGPKStream = streamToSave;
					GGPKStream.Seek(0, SeekOrigin.Begin);

					// Update Offsets in DirectoryRecords
					var offset = (long)GgpkRecord.Length; // 28
					foreach (var node in nodes) {
						if (node.Parent is DirectoryRecord dr) {
							for (int i = 0; i < dr.Entries.Length; ++i)
								if (dr.Entries[i].NameHash == node.NameHash) {
									dr.Entries[i].Offset = offset;
									break;
								}
						} else if (node == Root)
							Root.Offset = offset;
						else
							throw new NullReferenceException("node.Parent is null\nnode.Name: " + node.Name + "node.Offset: " + node.Offset);
						offset += node.Length;
					}

					// Write GGPKRecord
					GgpkRecord.RootDirectoryOffset = Root.Offset;
					GgpkRecord.FirstFreeRecordOffset = 0;
					GgpkRecord.WriteRecordData();
					progress?.Report(count);

					// Write other records
					var buffer = new byte[104857600];
					foreach (var node in nodes) {
						cancellation?.ThrowIfCancellationRequested();
						if (node is FileRecord fr) {
							if (fr.Length > buffer.Length)
								buffer = new byte[fr.Length];
							oldStream.Seek(fr.DataOffset, SeekOrigin.Begin);
							for (var l = 0; l < fr.DataLength;)
								l += oldStream.Read(buffer, l, fr.DataLength - l);
							fr.WriteRecordData();
							GGPKStream.Write(new(buffer, 0, fr.DataLength));
						} else
							node.WriteRecordData();
						progress?.Report(--count);
					}

					GGPKStream.Flush();
					if (!LeaveOpen)
						oldStream.Close();
				} catch (OperationCanceledException) {
					GGPKStream.Close();
					GGPKStream = oldStream;
					throw;
				}
			});
		}

		/// <summary>
		/// Try to fix the broken FreeRecord Linked List
		/// </summary>
		public virtual void FixFreeRecordList() {
			var s = GGPKStream;
			s.Seek(0, SeekOrigin.Begin);
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
			s.Seek(GgpkRecord.Offset + 20, SeekOrigin.Begin);
			s.Write(GgpkRecord.FirstFreeRecordOffset);
			foreach (var fr in list) {
				s.Seek(fr.Offset + 8, SeekOrigin.Begin);
				s.Write(fr.NextFreeOffset);
			}
			s.Flush();
		}

		/// <summary>
		/// Merge all adjacent FreeRecords
		/// </summary>
		protected virtual void FreeRecordConcat() {
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
					GGPKStream.Seek(current.Offset, SeekOrigin.Begin);
					GGPKStream.Write(current.Length);
				}
			}
			if (current != null && current.Offset + current.Length >= GGPKStream.Length) {
				GGPKStream.Flush();
				GGPKStream.SetLength(current.Offset);
				current.RemoveFromList();
			}
		}

		/// <summary>
		/// Recursive all nodes under <paramref name="node"/>
		/// </summary>
		/// <param name="node"></param>
		/// <returns></returns>
		public virtual IEnumerable<TreeNode> RecursiveTree(TreeNode node) {
			yield return node;
			if (node is DirectoryRecord dr)
				foreach (var t in dr.Children)
					foreach (var tt in RecursiveTree(t))
						yield return tt;
		}

		/// <summary>
		/// Export file/directory synchronously
		/// </summary>
		/// <param name="record">File/Directory Record to export</param>
		/// <param name="path">Path to save</param>
		/// <returns>Number of files exported</returns>
		public static int Extract(TreeNode record, string path) {
			if (record is FileRecord fr) {
				File.WriteAllBytes(path, fr.ReadFileContent());
				return 1;
			} else {
				var count = 0;
				Directory.CreateDirectory(path);
				foreach (var f in ((DirectoryRecord)record).Children)
					count += Extract(f, path + "\\" + f.Name);
				return count;
			}
		}

		/// <summary>
		/// Replace file/directory synchronously
		/// </summary>
		/// <param name="record">File/Directory Record to replace</param>
		/// <param name="path">Path to file to import</param>
		/// <returns>Number of files replaced</returns>
		public static int Replace(TreeNode record, string path) {
			if (record is FileRecord fr) {
				if (File.Exists(path)) {
					fr.ReplaceContent(File.ReadAllBytes(path));
					return 1;
				}
				return 0;
			} else {
				var count = 0;
				foreach (var f in ((DirectoryRecord)record).Children)
					count += Replace(f, path + "\\" + f.Name);
				return count;
			}
		}

		public virtual void Dispose() {
			GC.SuppressFinalize(this);
			if (!LeaveOpen)
				GGPKStream?.Close();
		}

		~GGPK() {
			Dispose();
		}
	}
}