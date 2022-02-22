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
		protected bool LeaveOpen;
		protected internal Stream GGPKStream;
		public readonly GGPKRecord GgpkRecord;
		public readonly DirectoryRecord Root;
		protected LinkedList<FreeRecord>? _FreeRecords;
		public LinkedList<FreeRecord> FreeRecords => _FreeRecords ??= BuildFreeRecordList();

		protected LinkedList<FreeRecord> BuildFreeRecordList() {
			var list = new LinkedList<FreeRecord>();
			var offsets = new HashSet<long>();
			var NextFreeOffset = GgpkRecord.FirstFreeRecordOffset;
			while (NextFreeOffset > 0) {
				if (!offsets.Add(NextFreeOffset))
					throw new("FreeRecordList causes an infinite loops, your GGPK is broken!");
				var current = (FreeRecord)ReadRecord(NextFreeOffset);
				list.AddLast(current);
				NextFreeOffset = current.NextFreeOffset;
			}
			return list;
		}

		/// <param name="filePath">Path to Content.ggpk</param>
		public GGPK(string filePath) : this(File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) {
		}

		/// <param name="stream">Stream of Content.ggpk</param>
		/// <param name="leaveOpen">If false, close the <paramref name="stream"/> after this instance has been disposed</param>
		public GGPK(Stream stream, bool leaveOpen = true) {
			LeaveOpen = leaveOpen;
			GGPKStream = stream;

			// Read ROOT Directory Record
			if (ReadRecord() is not GGPKRecord gr)
				throw new("The first record is not GGPKRecord");

			GgpkRecord = gr;
			Root = (DirectoryRecord)ReadRecord(GgpkRecord.RootDirectoryOffset);
		}

		/// <summary>
		/// Read a record from GGPK at <paramref name="offset"/>
		/// </summary>
		/// <param name="offset">Record offset, null for current stream position</param>
		[SkipLocalsInit]
		public unsafe virtual BaseRecord ReadRecord(long? offset = null) {
			if (offset.HasValue)
				GGPKStream.Seek(offset.Value, SeekOrigin.Begin);

			var buffer = stackalloc byte[8];
			GGPKStream.Read(new(buffer, 8));
			var length = *(int*)buffer;
			return ((int*)buffer)[1] switch {
				1162627398 => new FileRecord(length, this), // FILE
				1380533328 => new DirectoryRecord(length, this), // PDIR
				1162170950 => new FreeRecord(length, this), // FREE
				1263552327 => new GGPKRecord(length, this), // GGPK
				_ => throw new Exception("Invalid record tag at offset: " + (GGPKStream.Position - 4))
			};
		}

		/// <summary>
		/// Find the record with a <paramref name="path"/>
		/// </summary>
		/// <param name="path">Path in GGPK under <paramref name="parent"/></param>
		/// <param name="parent">Where to start searching, null for ROOT directory in GGPK</param>
		/// <returns>null if not found</returns>
		public virtual TreeNode? FindNode(string path, DirectoryRecord? parent = null) {
			parent ??= Root;
			var SplittedPath = path.Split('/', '\\');
			foreach (var name in SplittedPath) {
				if (name == "")
					continue;

				var next = parent.Children.FirstOrDefault(t => t.Name == name);
				if (next is not DirectoryRecord dr)
					return next;

				parent = dr;
			}
			return parent;
		}

		public virtual LinkedListNode<FreeRecord>? FindBestFreeRecord(int length, out int remainingSpace) {
			LinkedListNode<FreeRecord>? bestNode = null; // Find the FreeRecord with most suitable size
			var currentNode = FreeRecords.First!;
			remainingSpace = int.MaxValue;
			do {
				if (currentNode.Value.Length == length) {
					bestNode = currentNode;
					remainingSpace = 0;
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

		/// <param name="progress">returns the number of FreeRecords remaining to be filled.
		/// This won't be always decreasing</param>
		/// <returns>total length of remaining FreeRecords</returns>
		public virtual Task FastCompactAsync(CancellationToken? cancellation = null, IProgress<int>? progress = null) {
			return Task.Run(() => {
				cancellation?.ThrowIfCancellationRequested();
				FreeRecordConcat();
				cancellation?.ThrowIfCancellationRequested();

				var freeList = new PriorityQueue<FreeRecord, long>(FreeRecords.Select(f => (f, f.Offset)));
				progress?.Report(freeList.Count);
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

		public virtual Task FullCompactAsync(string pathToSave, CancellationToken? cancellation = null, IProgress<int>? progress = null) {
			var f = File.Create(pathToSave);
			return FullCompactAsync(f, cancellation, progress).ContinueWith(t => f.Close());
		}

		public virtual Task FullCompactAsync(Stream streamToSave, CancellationToken? cancellation = null, IProgress<int>? progress = null) {
			return Task.Run(() => {
				var oldStream = GGPKStream;
				try {
					cancellation?.ThrowIfCancellationRequested();
					GGPKStream.Flush();
					var nodes = RecursiveTree(Root).ToList();
					var count = nodes.Count;
					progress?.Report(count + 1);
					cancellation?.ThrowIfCancellationRequested();
					GGPKStream = streamToSave;
					GGPKStream.Seek(0, SeekOrigin.Begin);

					// Write GGPKRecord
					GgpkRecord.RootDirectoryOffset = 28;
					GgpkRecord.FirstFreeRecordOffset = 0;
					GgpkRecord.WriteRecordData();

					// Update Offsets in DirectoryRecords
					var offset = GgpkRecord.Length + Root.Length;
					foreach (var node in nodes) {
						if (node.Parent is DirectoryRecord dr)
							for (int i = 0; i < dr.Entries.Length; i++)
								if (dr.Entries[i].Offset == node.Offset)
									dr.Entries[i].Offset = offset;
						offset += node.Length;
					}
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
							node.WriteRecordData();
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

		protected virtual void FreeRecordConcat() {
			var list = FreeRecords.ToList();
			list.Sort(Comparer<FreeRecord>.Create((x, y) => x.Offset.CompareTo(y.Offset)));
			var cont = true;
			FreeRecord? current = default;
			for (var i = 0; cont;) {
				var changed = false;
				current = list[i];
				while ((cont = ++i < list.Count) && current.Offset + current.Length == list[i].Offset) {
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
				GGPKStream.Close();
		}

		~GGPK() {
			Dispose();
		}
	}
}