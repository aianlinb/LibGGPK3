using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using SystemExtensions;
using SystemExtensions.Collections;
using SystemExtensions.Streams;

namespace LibGGPK3.Records {
	public abstract class TreeNode(int length, GGPK ggpk) : BaseRecord(length, ggpk) {
		protected static readonly SHA256 Hash256 = SHA256.Create();
		/// <summary>
		/// File/Directory name
		/// </summary>
		public virtual string Name { get; protected init; } = "";
		/// <summary>
		/// SHA256 hash of the file content
		/// </summary>
		public virtual ReadOnlyMemory<byte> Hash => _Hash;
		public const int LENGTH_OF_HASH = 32;
		/// <summary>
		/// SHA256 hash of the file content
		/// </summary>
		protected internal readonly byte[] _Hash = new byte[LENGTH_OF_HASH]; // Should be set in derived class
		/// <summary>
		/// Parent node
		/// </summary>
		public virtual DirectoryRecord? Parent { get; protected internal set; }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		protected LinkedListNode<FreeRecord>? WriteWithNewLength(LinkedListNode<FreeRecord>? specify = null) {
			return WriteWithNewLength(CaculateRecordLength(), specify);
		}
		/// <summary>
		/// Write the modified record data to ggpk file.
		/// </summary>
		/// <param name="newLength">New length of the record after modification</param>
		/// <param name="specify">The specified <see cref="FreeRecord"/> to be written, <see langword="null"/> for finding a best one automatically</param>
		/// <returns>The <see cref="FreeRecord"/> created at the original position if the record is moved, or <see langword="null"/> if replaced in place</returns>
		/// <remarks>Don't set <see cref="BaseRecord.Length"/> before calling this method, the method will update it</remarks>
		protected internal virtual LinkedListNode<FreeRecord>? WriteWithNewLength(int newLength, LinkedListNode<FreeRecord>? specify = null) {
			var s = Ggpk.baseStream;
			lock (s) {
				if (Offset != default && newLength == Length && specify is null) {
					s.Position = Offset;
					WriteRecordData();
					return null;
				}
				var original = Offset == default ? null : MarkAsFree();

				Length = newLength;
				specify ??= Ggpk.FindBestFreeRecord(Length);
				if (specify is null) {
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
					} else // free.Length == 0
						free.RemoveFromList(specify);
					if (free == original?.Value)
						return null;
				}

				UpdateOffset();
				return original;
			}
		}

		/// <summary>
		/// Set this record to a <see cref="FreeRecord"/>
		/// </summary>
		protected virtual LinkedListNode<FreeRecord>? MarkAsFree() {
			var s = Ggpk.baseStream;
			lock (s) {
				LinkedListNode<FreeRecord>? previous = null;
				var length = Length;
				var right = false;
				// Combine with FreeRecords nearby
				for (var fn = Ggpk.FreeRecordList.First; fn is not null; fn = fn.Next) {
					var f = fn.Value;
					if (f.Offset == Offset + length) {
						length += f.Length;
						if (previous is not null)
							previous.Value.Length += f.Length;
						f.RemoveFromList(fn);
						right = true;
					} else if (previous is null && f.Offset + f.Length == Offset) {
						f.Length += length;
						previous = fn;
					}
					if (right && previous is not null) // In most cases, there won't be contiguous FreeRecords in GGPK
						break;
				}

				if (previous is not null) {
					var fPrevious = previous.Value;
					if (fPrevious.Offset + fPrevious.Length >= s.Length) {
						// Trim if the record is at the end of the ggpk file
						fPrevious.RemoveFromList(previous);
						s.SetLength(fPrevious.Offset);
						return null;
					}
					// Update record length
					s.Position = fPrevious.Offset;
					s.Write(fPrevious.Length);
					return previous;
				} else if (Offset + length >= s.Length) {
					// Trim if the record is at the end of the ggpk file
					s.SetLength(Offset);
					return null;
				}

				// Write FreeRecord
				var free = new FreeRecord(Offset, length, 0, Ggpk);
				s.Position = Offset;
				free.WriteRecordData();
				return free.UpdateOffset();
			}
		}

		/// <summary>
		/// Update the offset of this record in <see cref="Parent"/>.<see cref="DirectoryRecord.Entries"/>
		/// </summary>
		protected virtual unsafe void UpdateOffset() {
			var s = Ggpk.baseStream;
			lock (s) {
				if (Parent is DirectoryRecord dr) {
					var i = dr.Entries.AsSpan().BinarySearch((DirectoryRecord.Entry.NameHashWrapper)NameHash);
					if (i < 0)
						ThrowHelper.Throw<KeyNotFoundException>($"{GetPath()} update offset failed: NameHash={NameHash}, Offset={Offset}");
					dr.Entries[i].Offset = Offset;
					s.Position = dr.EntriesBegin + sizeof(DirectoryRecord.Entry) * i + sizeof(uint);
					s.Write(Offset);
				} else if (this == Ggpk.Root) {
					Ggpk.Record.RootDirectoryOffset = Offset;
					s.Position = Ggpk.Record.Offset + (sizeof(int) + sizeof(long));
					s.Write(Offset);
				} else
					ThrowHelper.Throw<NullReferenceException>(nameof(Parent));
			}
		}

		/// <summary>
		/// Caculate the length of the record should be in ggpk file
		/// </summary>
		protected abstract int CaculateRecordLength();
		/// <summary>
		/// Get the full path in GGPK of this File/Directory
		/// </summary>
		public string GetPath() {
			var builder = new ValueList<char>(stackalloc char[256]);
			try {
				GetPath(ref builder);
				return builder.AsSpan().ToString();
			} finally {
				builder.Dispose();
			}
		}
		private void GetPath(scoped ref ValueList<char> builder) {
			if (Parent is null) // Root
				return;
			Parent.GetPath(ref builder);
			builder.AddRange(Name.AsSpan());
			if (this is DirectoryRecord)
				builder.Add('/');
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
		/// <exception cref="InvalidOperationException">Thrown when <see langword="this"/> instance or <see cref="Parent"/> or <paramref name="directory"/> is <see cref="GGPK.Root"/></exception>
		[MemberNotNull(nameof(Parent))]
		public virtual void MoveTo(DirectoryRecord directory) {
			if (Parent == Ggpk.Root || this == Ggpk.Root /*|| directory == Ggpk.Root (will be checked in next "if")*/)
				ThrowHelper.Throw<InvalidOperationException>("You can't change child elements of the root folder, otherwise it will break the GGPK when the game starts");
			if (directory.InsertEntry(new(NameHash, Offset)) < 0)
				ThrowHelper.Throw<InvalidOperationException>($"A file/directory with name: {Name} is already exist in: {directory.GetPath()}");
			Parent!.RemoveEntry(NameHash);
			Parent = directory;
			directory._Children ??= new(directory.Entries.Length);
			directory._Children.Add(NameHash, this);
			directory.WriteWithNewLength();
		}

		/// <summary>
		/// Remove this record and all children permanently from ggpk.
		/// </summary>
		/// <remarks>
		/// Do not use any record instance of removed node (This node and all children of it) after calling this.
		/// Otherwise it may break ggpk.
		/// </remarks>
		[MemberNotNull(nameof(Parent))]
		public virtual void Remove() {
			if (Parent is null /*(this == Ggpk.Root)*/ || Parent == Ggpk.Root)
				ThrowHelper.Throw<InvalidOperationException>("You can't change child elements of the root folder, otherwise it will break the GGPK when the game starts");
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
			MarkAsFree();
		}

		[SkipLocalsInit]
		public static unsafe uint GetNameHash(ReadOnlySpan<char> name) {
			var p = stackalloc char[name.Length];
			return MurmurHash2(new(p, name.ToLowerInvariant(new(p, name.Length)) * sizeof(char)), 0);
		}

		protected static unsafe uint MurmurHash2(ReadOnlySpan<byte> data, uint seed = 0xC58F1A7Bu) {
			const uint m = 0x5BD1E995u;
			const int r = 24;

			unchecked {
				seed ^= (uint)data.Length;

				ref uint p = ref Unsafe.As<byte, uint>(ref MemoryMarshal.GetReference(data));
				if (data.Length >= sizeof(uint)) {
					ref uint pEnd = ref Unsafe.Add(ref p, data.Length / sizeof(uint));
					do {
						uint k = p * m;
						seed = (seed * m) ^ ((k ^ (k >> r)) * m);
						p = ref Unsafe.Add(ref p, 1);
					} while (Unsafe.IsAddressLessThan(ref p, ref pEnd));
				}

				int remainingBytes = data.Length % sizeof(uint);
				if (remainingBytes != 0)
					seed = (seed ^ (p & (uint.MaxValue >> ((sizeof(uint) - remainingBytes) * 8)))) * m;

				seed = (seed ^ (seed >> 13)) * m;
				return seed ^ (seed >> 15);
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

		[System.Diagnostics.DebuggerNonUserCode]
		protected void ThrowIfNameContainsSlash() {
			if (Name.Contains('/'))
				Throw();

			[DoesNotReturn, System.Diagnostics.DebuggerNonUserCode]
			static void Throw() {
				throw new ArgumentException("Name cannot contain '/'", "name");
			}
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