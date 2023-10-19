using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace LibGGPK3.Records {
	public abstract class TreeNode : BaseRecord {
		private static readonly byte[] HashOfEmpty = Convert.FromHexString("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");
		/// <summary>
		/// File/Directory name
		/// </summary>
		public virtual string Name { get; protected init; } = "";
		/// <summary>
		/// SHA256 hash of the file content
		/// </summary>
		public virtual ReadOnlyMemory<byte> Hash => _Hash;
		/// <summary>
		/// SHA256 hash of the file content
		/// </summary>
		protected internal byte[] _Hash = (byte[])HashOfEmpty.Clone();
		/// <summary>
		/// Parent node
		/// </summary>
		public virtual DirectoryRecord? Parent { get; protected internal set; }

		protected TreeNode(int length, GGPK ggpk) : base(length, ggpk) { }

		/// <summary>
		/// Write the modified record data to ggpk file.
		/// </summary>
		/// <param name="newLength">New length of the record after modification</param>
		/// <param name="specify">The specified <see cref="FreeRecord"/> to be written, <see langword="null"/> for finding a best one automatically</param>
		/// <returns>The <see cref="FreeRecord"/> created at the original position if the record is moved, or <see langword="null"/> if replaced in place</returns>
		/// <remarks>Don't set <see cref="BaseRecord.Length"/> before calling this method</remarks>
		public virtual LinkedListNode<FreeRecord>? WriteWithNewLength(int newLength, LinkedListNode<FreeRecord>? specify = null) {
			if (Offset != 0 && newLength == Length && specify == null) {
				Ggpk.baseStream.Position = Offset;
				WriteRecordData();
				return null;
			}
			var original = Offset == 0 ? null : MarkAsFreeRecord();

			Length = newLength;
			var s = Ggpk.baseStream;
			specify ??= Ggpk.FindBestFreeRecord(Length);
			if (specify == null) {
				s.Seek(0, SeekOrigin.End); // Write to the end of GGPK
				WriteRecordData();
			} else {
				var free = specify.Value;
				if (free.Length < Length + 16 && free.Length != Length)
					throw new ArgumentException("The length of specified FreeRecord must not be between Length and Length-16 (exclusive): " + free.Length, nameof(specify));
				s.Position = free.Offset;
				WriteRecordData();
				free.Length -= Length;
				if (free.Length >= 16) { // Update length of FreeRecord
					s.Position = free.Offset + Length;
					free.WriteRecordData();
					free.UpdateOffset();
				} else
					free.RemoveFromList(specify);
				if (free == original?.Value)
					return null;
			}

			UpdateOffset();
			return original;
		}

		/// <summary>
		/// Set the record to a FreeRecord
		/// </summary>
		protected virtual LinkedListNode<FreeRecord>? MarkAsFreeRecord() {
			var s = Ggpk.baseStream;
			s.Position = Offset;
			LinkedListNode<FreeRecord>? rtn = null;
			var length = Length;

			// Combine with FreeRecords nearby
			for (var fn = Ggpk.FreeRecords.First; fn != null; fn = fn.Next) {
				var f = fn.Value;
				if (f.Offset == Offset + length) {
					length += f.Length;
					if (rtn != null)
						rtn.Value.Length += f.Length;
					f.RemoveFromList(fn);
				} else if (f.Offset + f.Length == Offset) {
					f.Length += length;
					rtn = fn;
				}
			}

			// Trim if the record is at the end of the ggpk file
			if (rtn != null) {
				var rtnv = rtn.Value;
				if (rtnv.Offset + rtnv.Length >= s.Length) {
					rtnv.RemoveFromList(rtn);
					s.Flush();
					s.SetLength(rtnv.Offset);
					return null;
				}
				s.Position = rtnv.Offset;
				s.Write(rtnv.Length);
				return rtn;
			}
			if (Offset + length >= s.Length) {
				s.Flush();
				s.SetLength(Offset);
				return null;
			}

			// Write FreeRecord
			var free = new FreeRecord(Offset, length, 0, Ggpk);
			Ggpk.baseStream.Position = Offset;
			free.WriteRecordData();
			return free.UpdateOffset();
		}

		/// <summary>
		/// Update the offset of this record in <see cref="Parent"/>.<see cref="DirectoryRecord.Entries"/>
		/// </summary>
		protected virtual unsafe void UpdateOffset() {
			if (Parent is DirectoryRecord dr) {
				var i = DirectoryRecord.Entry.BinarySearch(dr.Entries, NameHash);
				if (i < 0)
					throw new($"{GetPath()} update offset failed: NameHash={NameHash}, Offset={Offset}");
				dr.Entries[i].Offset = Offset;
				Ggpk.baseStream.Position = dr.EntriesBegin + sizeof(DirectoryRecord.Entry) * i + sizeof(uint);
				Ggpk.baseStream.Write(Offset);
			} else if (this == Ggpk.Root) {
				Ggpk.GgpkRecord.RootDirectoryOffset = Offset;
				Ggpk.baseStream.Position = Ggpk.GgpkRecord.Offset + (sizeof(int) + sizeof(long));
				Ggpk.baseStream.Write(Offset);
			} else
				throw new NullReferenceException(nameof(Parent));
		}

		/// <summary>
		/// Caculate the length of the record should be in ggpk file
		/// </summary>
		protected abstract int CaculateRecordLength();
		/// <summary>
		/// Get the full path in GGPK of this File/Directory
		/// </summary>
		public virtual string GetPath() {
			return this is FileRecord ? (Parent?.GetPath() ?? "") + Name : (Parent?.GetPath() ?? "") + Name + "/";
		}

		protected internal uint? _NameHash;
		/// <summary>
		/// Get the murmur hash of name of this File/Directory
		/// </summary>
		public virtual uint NameHash => _NameHash ??= GetNameHash(Name);

		/// <summary>
		/// Move the node from <see cref="Parent"/> to <paramref name="directory"/> (which can't be <see cref="GGPK.Root"/>)
		/// </summary>
		/// <param name="directory">The new parent node to move to (which can't be <see cref="GGPK.Root"/>)</param>
		/// <exception cref="InvalidOperationException">Thrown when this instance or <see cref="Parent"/> is <see cref="GGPK.Root"/></exception>
		public virtual void MoveTo(DirectoryRecord directory) {
			if (Parent == Ggpk.Root || this == Ggpk.Root /*|| directory == Ggpk.Root (Will be checked in next "if")*/)
				throw new InvalidOperationException("You can't change child elements of the root folder, otherwise it will break the GGPK when the game starts");
			if (directory.InsertEntry(new(NameHash, Offset)) < 0)
				throw new InvalidOperationException($"A file/directory with name: {Name} is already exist in: {directory.GetPath()}");
			Parent!.RemoveEntry(NameHash);
			Parent = directory;
			directory._Children ??= new(directory.Entries.Length);
			directory._Children.Add(NameHash, this);
			directory.WriteWithNewLength(CaculateRecordLength());
		}

		/// <summary>
		/// Remove this record and all children permanently from ggpk.
		/// </summary>
		/// <remarks>
		/// Do not use any record instance of removed node (This node and all children of it) after calling this.
		/// Otherwise it may break ggpk.
		/// </remarks>
		public virtual void Remove() {
			if (Parent == null || Parent == Ggpk.Root || this == Ggpk.Root)
				throw new InvalidOperationException("You can't change child elements of the root folder, otherwise it will break the GGPK when the game starts");
			RemoveRecursively();
			Parent.RemoveEntry(NameHash);
		}
		/// <summary>
		/// Internal implementation of <see cref="Remove"/>
		/// </summary>
		protected virtual void RemoveRecursively() {
			if (this is DirectoryRecord dr)
				foreach (var c in dr.Children)
					c.RemoveRecursively();
			MarkAsFreeRecord();
		}

		[SkipLocalsInit]
		public static unsafe uint GetNameHash(ReadOnlySpan<char> name) {
			var count = name.Length;
			var p = stackalloc char[count];
			count = name.ToLowerInvariant(new(p, count));
			return MurmurHash2(new(p, count * sizeof(char)), 0);
		}

		protected static unsafe uint MurmurHash2(ReadOnlySpan<byte> data, uint seed = 0xC58F1A7B) {
			const uint m = 0x5BD1E995;
			const int r = 24;

			unchecked {
				int length = data.Length;
				uint h = seed ^ (uint)length;
				int numberOfLoops = length >> 2; // div 4

				fixed (byte* pd = data) {
					uint* p = (uint*)pd;
					while (numberOfLoops-- != 0) {
						uint k = *p++;
						k *= m;
						k = (k ^ (k >> r)) * m;
						h = (h * m) ^ k;
					}

					int remainingBytes = length & 0b11; // mod 4
					if (remainingBytes != 0) {
						int offset = (4 - remainingBytes) << 3; // mul 8 (bit * 8 = byte)
						h ^= *p & (0xFFFFFFFFU >> offset);
						h *= m;
					}
				}

				h = (h ^ (h >> 13)) * m;
				h ^= h >> 15;
				return h;
			}
		}

		/// <summary>
		/// Recursive all nodes under <paramref name="node"/> (contains self)
		/// </summary>
		public static IEnumerable<TreeNode> RecurseTree(TreeNode node) {
			yield return node;
			if (node is DirectoryRecord dr)
				foreach (var t in dr.Children)
					foreach (var tt in RecurseTree(t))
						yield return tt;
		}
		
		/// <summary>
		/// Use to sort the children of directory.
		/// </summary>
		public sealed class NodeComparer : IComparer<TreeNode> {
			public static readonly NodeComparer Instance = new();
			private NodeComparer() { }
			/* Too Slow
			[DllImport("shlwapi", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
			private static extern int StrCmpLogicalW(string x, string y);
			private static readonly Func<string, string, int> FuncCompare = OperatingSystem.IsWindows() ? StrCmpLogicalW : string.Compare;
			*/
#pragma warning disable CS8767
			public int Compare(TreeNode x, TreeNode y) {
				if (x is DirectoryRecord) {
					if (y is FileRecord)
						return -1;
				} else {
					if (y is DirectoryRecord)
						return 1;
				}
				return string.Compare(x.Name, y.Name, StringComparison.InvariantCulture);
			}
		}
	}
}