using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

using SystemExtensions;
using SystemExtensions.Collections;
using SystemExtensions.Streams;

namespace LibGGPK3.Records;

/// <summary>
/// Base class of <see cref="FileRecord"/> and <see cref="DirectoryRecord"/>, represents nodes of the file system tree in ggpk file.
/// </summary>
/// <remarks>
/// Do not extend this class directly, use <see cref="FileRecord"/> or <see cref="DirectoryRecord"/> instead.
/// </remarks>
public abstract class TreeNode(int length, GGPK ggpk) : BaseRecord(length, ggpk) {
	/// <summary>
	/// File/Directory name
	/// </summary>
	public virtual string Name { get; protected init; } = "";
	protected internal Vector256<byte> _Hash; // Should be set in derived class
	/// <summary>
	/// SHA256 hash of the file content
	/// </summary>
	public Vector256<byte> Hash => _Hash;
	/// <summary>
	/// Size of <see cref="Hash"/> in bytes == <see langword="sizeof"/>(<see cref="Vector256{T}"/>)
	/// </summary>
	protected const int SIZE_OF_HASH = 32; // sizeof(Vector256<byte>)
	/// <summary>
	/// Parent node
	/// </summary>
	public virtual DirectoryRecord? Parent { get; protected internal set; }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected FreeRecord? WriteWithNewLength(FreeRecord? specify = null) {
		return WriteWithNewLength(CaculateRecordLength(), specify);
	}
	/// <summary>
	/// Write the modified record data to ggpk file.
	/// </summary>
	/// <param name="newLength">New length of the record after modification</param>
	/// <param name="specify">The specified <see cref="FreeRecord"/> to be written, <see langword="null"/> for finding a best one automatically</param>
	/// <returns>The <see cref="FreeRecord"/> created at the original position if the record is moved, or <see langword="null"/> if replaced in place</returns>
	/// <remarks>Don't set <see cref="BaseRecord.Length"/> before calling this method, the method will update it</remarks>
	protected internal virtual FreeRecord? WriteWithNewLength(int newLength, FreeRecord? specify = null) {
		var s = Ggpk.baseStream;
		lock (s) {
			if (Offset != default && newLength == Length && specify is null) {
				s.Position = Offset;
				WriteRecordData();
				return null;
			}
			var original = Offset == default ? null : MarkAsFree();

			Length = newLength;
			specify ??= Ggpk.FindBestFreeRecord(newLength);
			if (specify is null) {
				s.Seek(0, SeekOrigin.End); // Write to the end of GGPK
				WriteRecordData();
			} else {
				if (specify == original)
					original = null;

				if (specify.Length < newLength + 16 && specify.Length != newLength)
					ThrowHelper.Throw<ArgumentException>("The length of specified FreeRecord must not be between Length and Length-16 (exclusive): " + specify.Length, nameof(specify));
				s.Position = specify.Offset;
				WriteRecordData();
				specify.Length -= newLength;
				if (specify.Length >= 16) { // Update length of FreeRecord
					s.Position = specify.Offset + newLength;
					specify.WriteRecordData();
					specify.UpdateOffset();
					specify.UpdateLength();
				} else // specify.Length == 0
					specify.RemoveFromList();
			}

			UpdateOffset();
			return original;
		}
	}

	/// <summary>
	/// Set this record to a <see cref="FreeRecord"/>
	/// </summary>
	protected virtual FreeRecord? MarkAsFree() {
		var s = Ggpk.baseStream;
		lock (s) {
			FreeRecord? previous = null;
			var length = Length;
			var right = false;
			// Combine with FreeRecords nearby
			foreach (var f in Ggpk._SortedFreeRecords ?? Ggpk.FreeRecords) {
				if (f.Offset == Offset + length) {
					length += f.Length;
					if (previous is not null)
						previous.Length += f.Length;
					f.RemoveFromList();
					right = true;
				} else if (previous is null && f.Offset + f.Length == Offset) {
					f.Length += length;
					previous = f;
				}
				if (right && previous is not null) // In most cases, there won't be contiguous FreeRecords in GGPK
					break;
			}

			if (previous is not null) {
				if (previous.Offset + previous.Length >= s.Length) {
					// Trim if the record is at the end of the ggpk file
					previous.RemoveFromList();
					s.SetLength(previous.Offset);
					return null;
				}
				// Update record length
				s.Position = previous.Offset;
				s.Write(previous.Length);
				previous.UpdateLength();
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
			free.UpdateOffset();
			free.UpdateLength();
			return free;
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
				s.Position = dr.Offset + (dr.Length - sizeof(DirectoryRecord.Entry) * (dr.Entries.Length - i) + sizeof(uint));
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
		var builder = new ValueList<char>(stackalloc char[128]);
		try {
			GetPath(ref builder);
			return new(builder.AsReadOnlySpan());
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
	/// <remarks>
	/// <para><see cref="GGPK.Root"/> can't be moved.</para>
	/// <para>Note that modifications made to children of <see cref="GGPK.Root"/> will be restored immediately when the game starts.</para>
	/// </remarks>
	[MemberNotNull(nameof(Parent))]
	public virtual void MoveTo(DirectoryRecord directory) {
		if (this == Ggpk.Root)
			ThrowHelper.Throw<InvalidOperationException>("You can't move the root directory");
		var i = directory.InsertNode(this);
		if (i < 0)
			directory.ThrowExist(Name);
		Parent!.RemoveEntry(NameHash);
		Parent = directory;
	}

	/// <summary>
	/// Remove this record and all children permanently from ggpk.
	/// </summary>
	/// <remarks>
	/// <para><see cref="GGPK.Root"/> can't be removed.</para>
	/// <para>Note that modifications made to children of <see cref="GGPK.Root"/> will be restored immediately when the game starts.</para>
	/// <para>Do not use any record instance of the removed nodes or its children after calling this, otherwise it may break the ggpk.</para>
	/// </remarks>
	[MemberNotNull(nameof(Parent))]
	public virtual void Remove() {
		if (this == Ggpk.Root)
			ThrowHelper.Throw<InvalidOperationException>("You can't remove the root directory");
		MarkAsFreeRecursively();
		Parent!.RemoveEntry(NameHash);
	}
	/// <summary>
	/// Internal implementation of <see cref="Remove"/>
	/// </summary>
	protected virtual void MarkAsFreeRecursively() {
		if (this is DirectoryRecord dr)
			foreach (var c in dr)
				c.MarkAsFreeRecursively();
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
	/// Recurse all nodes under <paramref name="node"/> (include self)
	/// </summary>
	public static IEnumerable<TreeNode> RecurseTree(TreeNode node) {
		yield return node;
		if (node is DirectoryRecord dr)
			foreach (var t in dr)
				foreach (var tt in RecurseTree(t))
					yield return tt;
	}

	/// <summary>
	/// Recurse all <see cref="FileRecord"/> under <paramref name="node"/> (include self)
	/// </summary>
	/// <returns>A tuple of <see cref="FileRecord"/> and its relative path to <paramref name="node"/>
	/// (or <see cref="string.Empty"/> if <paramref name="node"/> is <see cref="FileRecord"/>)</returns>
	public static IEnumerable<(FileRecord, string)> RecurseFiles(TreeNode node) {
		var builder = new StringBuilder(128);
		return RecurseFiles(node);

		IEnumerable<(FileRecord, string)> RecurseFiles(TreeNode node) {
			if (node is FileRecord f)
				yield return (f, string.Empty);
			else if (node is DirectoryRecord d)
				foreach (var t in d) {
					foreach (var c in Core(t))
						yield return c;

					IEnumerable<(FileRecord, string)> Core(TreeNode n) {
						if (n is FileRecord fr) {
							builder.Append(fr.Name);
							var result = builder.ToString();
							builder.Length -= fr.Name.Length;
							yield return (fr, result);
						} else if (n is DirectoryRecord dr) {
							builder.Append(dr.Name);
							builder.Append('/');
							foreach (var tn in dr)
								foreach (var e in Core(tn))
									yield return e;
							builder.Length -= dr.Name.Length + 1;
						}
					}
				}
		}
	}

	protected void ThrowIfNameEmptyOrContainsSlash() {
		ArgumentException.ThrowIfNullOrEmpty(Name, "name");
		if (Name.Contains('/'))
			Throw();

		[DoesNotReturn, DebuggerNonUserCode]
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
		[DllImport("shlwapi", CharSet = CharSet.Unicode)]
		private static extern int StrCmpLogicalW(string x, string y);
		private static readonly Func<string, string, int> FuncCompare = OperatingSystem.IsWindows() ? StrCmpLogicalW : string.Compare;
		*/
#pragma warning disable CS8767
		public int Compare(TreeNode x, TreeNode y) {
#pragma warning restore CS8767
			if (x is DirectoryRecord) {
				if (y is not DirectoryRecord)
					return -1;
			} else {
				if (y is DirectoryRecord)
					return 1;
			}
			return string.Compare(x.Name, y.Name, StringComparison.InvariantCulture);
		}
	}
}