using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using System.Text;

using SystemExtensions;
using SystemExtensions.Collections;
using SystemExtensions.Spans;
using SystemExtensions.Streams;

namespace LibGGPK3.Records;
public class DirectoryRecord : TreeNode, IReadOnlyList<TreeNode> {
	/// <summary>PDIR</summary>
	public const int Tag = 0x52494450;

	/// <summary>
	/// Container of <paramref name="nameHash"/> and <paramref name="offset"/> of each entry in children
	/// </summary>
	/// <remarks>
	/// There shouldn't be any duplicate namehash in the same directory
	/// </remarks>
	/// <param name="nameHash">Murmur2 hash of lowercase entry name (can be calculated by <see cref="TreeNode.GetNameHash"/>)</param>
	/// <param name="offset">Offset in pack file where the record begins</param>
	[DebuggerDisplay($"Entry \\{{{nameof(NameHash)}={{{nameof(NameHash)}}}, {nameof(Offset)}={{{nameof(Offset)}}}\\}}")]
	[StructLayout(LayoutKind.Sequential, Size = sizeof(uint) + sizeof(long), Pack = sizeof(uint))]
	protected internal struct Entry(uint nameHash, long offset) : IComparable<uint> {
		/// <summary>
		/// Murmur2 hash of lowercase entry name (can be calculated by <see cref="TreeNode.GetNameHash"/>)
		/// </summary>
		public uint NameHash = nameHash;
		/// <summary>
		/// Offset in pack file where the record begins
		/// </summary>
		public long Offset = offset;

		public readonly int CompareTo(uint nameHash) => NameHash.CompareTo(nameHash);

		public readonly struct NameHashWrapper(uint nameHash) : IComparable<Entry> {
			public readonly uint NameHash = nameHash;
			public readonly int CompareTo(Entry other) => NameHash.CompareTo(other.NameHash);
			public override readonly int GetHashCode() => unchecked((int)NameHash);
			public static implicit operator NameHashWrapper(uint nameHash) => new(nameHash);
			public static implicit operator uint(NameHashWrapper nameHash) => nameHash;
		}
	}

	/// <summary>
	/// Entries of this directory recorded in ggpk.
	/// </summary>
	/// <remarks>They must be in order of <see cref="Entry.NameHash"/></remarks>
	protected internal Entry[] Entries;
	/// <summary>
	/// Offset in ggpk where <see cref="Entries"/> begins. This is only here because it makes rewriting the entries easier.
	/// </summary>
	protected internal long EntriesOffset;
	/// <summary>
	/// Children of this directory.
	/// </summary>
	protected internal TreeNode?[] Children;

	/// <summary>
	/// Read a DirectoryRecord from GGPK
	/// </summary>
	[SkipLocalsInit]
	protected internal unsafe DirectoryRecord(int length, GGPK ggpk) : base(length, ggpk) {
		var s = ggpk.baseStream;
		Offset = s.Position - 8;
		var nameLength = s.Read<int>() - 1; // '\0'
		var totalEntries = s.Read<int>();
		s.Read(out _Hash);
		if (Ggpk.Record.GGPKVersion == 4) {
			Span<byte> b = stackalloc byte[nameLength * sizeof(int)]; // UTF32
			s.ReadExactly(b);
			Name = Encoding.UTF32.GetString(b);
			s.Seek(sizeof(int), SeekOrigin.Current); // Null terminator
		} else {
			Name = s.ReadString(nameLength);
			s.Seek(sizeof(char), SeekOrigin.Current); // Null terminator
		}
		EntriesOffset = s.Position;
		Entries = new Entry[totalEntries];
		Children = new TreeNode?[totalEntries];
		s.Read(Entries);
	}

	/// <summary>
	/// Internal Usage
	/// </summary>
	protected internal DirectoryRecord(string name, GGPK ggpk) : base(default, ggpk) {
		Name = name;
		ThrowIfNameContainsSlash();
		Entries = [];
		Children = [];
		Length = CaculateRecordLength();
	}

	/// <summary>
	/// Child count of this directory.
	/// </summary>
	public virtual int Count => Entries.Length;

	public virtual TreeNode this[int index] => Children[index] ??= ReadNode(Entries[index]);
	protected virtual TreeNode ReadNode(Entry entry) {
		var node = (TreeNode)Ggpk.ReadRecord(entry.Offset);
		node.Parent = this;
		node._NameHash = entry.NameHash;
		return node;
	}
	public struct Enumerator(DirectoryRecord directory) : IEnumerator<TreeNode> {
		private int index = -1;

		public readonly TreeNode Current => directory[index];
		readonly object IEnumerator.Current => Current;

		public bool MoveNext() => (uint)++index < (uint)directory.Count;
		public void Reset() => index = -1;
		public readonly void Dispose() { }
	}
	public virtual Enumerator GetEnumerator() => new(this);
	IEnumerator<TreeNode> IEnumerable<TreeNode>.GetEnumerator() => GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <summary>
	/// Get child with the given <paramref name="nameHash"/> which can be gotten from <see cref="TreeNode.GetNameHash"/> or <see cref="TreeNode.NameHash"/>.
	/// </summary>
	public TreeNode? this[uint nameHash] {
		get {
			var i = Entries.AsSpan().BinarySearch((Entry.NameHashWrapper)nameHash);
			if (i < 0)
				return null;
			return this[i];
		}
	}
	/// <summary>
	/// Get child with the given <paramref name="name"/>.
	/// </summary>
	public TreeNode? this[ReadOnlySpan<char> name] => this[GetNameHash(name)];

	/// <summary>
	/// Add a directory to this directory (which can't be <see cref="GGPK.Root"/>)
	/// </summary>
	/// <param name="name">Name of the directory</param>
	/// <param name="dontThrowIfExist">
	/// <see langword="true"/> to return the existing <see cref="DirectoryRecord"/> if one with the same name already exists.<br />
	/// <see langword="false"/> or if the existing one isn't <see cref="DirectoryRecord"/>, throws <see cref="InvalidOperationException"/>.
	/// </param>
	/// <returns>The <see cref="DirectoryRecord"/> added/existed</returns>
	/// <remarks>
	/// Modifications made to children of <see cref="GGPK.Root"/> will be restored immediately when the game starts.
	/// </remarks>
	/// <exception cref="DuplicateNameException"/>
	public virtual DirectoryRecord AddDirectory(string name, bool dontThrowIfExist = false) {
		var dir = new DirectoryRecord(name, Ggpk) { Parent = this };
		var i = AddNode(dir);
		if (i < 0 && (!dontThrowIfExist || (dir = this[Entries[~i].NameHash] as DirectoryRecord) is null))
			ThrowExist(name);
		return dir;
	}
	/// <summary>
	/// Add a file to this directory.
	/// </summary>
	/// <param name="name">Name of the file</param>
	/// <param name="preallocatedSize">
	/// Size in bytes of the file content which will be passed to <see cref="FileRecord.Write"/> later by the caller.
	/// Not used when one with the same name already exists.
	/// </param>
	/// <param name="dontThrowIfExist">
	/// Whether to return the existing <see cref="FileRecord"/> with the same name instead of throwing an exception.
	/// <para>Note that this still throws if the existing node isn't <see cref="FileRecord"/>.</para>
	/// </param>
	/// <returns>The <see cref="FileRecord"/> added/existed</returns>
	/// <remarks>
	/// Modifications made to children of <see cref="GGPK.Root"/> will be restored immediately when the game starts.
	/// </remarks>
	/// <exception cref="DuplicateNameException"/>
	public virtual FileRecord AddFile(string name, int preallocatedSize = 0, bool dontThrowIfExist = false) {
		var file = new FileRecord(name, Ggpk) {
			Parent = this,
			DataLength = preallocatedSize
		};
		file.Length += preallocatedSize;
		var i = AddNode(file);
		if (i < 0 && (!dontThrowIfExist || (file = this[Entries[~i].NameHash] as FileRecord) is null))
			ThrowExist(name);
		return file;
	}
	/// <exception cref="DuplicateNameException"/>
	[DoesNotReturn, DebuggerNonUserCode]
	internal void ThrowExist(string name) {
		throw new DuplicateNameException($"A file/directory with the same name already exists: {GetPath()}{name}");
	}

	/// <summary>
	/// Internal implementation of <see cref="AddDirectory"/> and <see cref="AddFile"/>
	/// </summary>
	/// <returns>
	/// The index of the entry is inserted, or ~index of the existing entry with the same namehash if failed to insert.
	/// </returns>
	/// <remarks>
	/// Modifications made to children of <see cref="GGPK.Root"/> will be restored immediately when the game starts.
	/// </remarks>
	protected virtual int AddNode(TreeNode node) {
		var i = InsertEntry(new(node.NameHash, -1)); // Entry.Offset will be set in `node.WriteWithNewLength(node.Length)` which calls TreeNode.UpdateOffset()
		if (i < 0)
			return i;
		Children[i] = node;
		WriteWithNewLength();
		node.WriteWithNewLength(node.Length);
		return i;
	}

	/// <returns>
	/// The index of the entry is inserted, or ~index of the existing entry with the same namehash if failed to insert.
	/// </returns>
	/// <remarks>
	/// Modifications made to children of <see cref="GGPK.Root"/> will be restored immediately when the game starts.
	/// </remarks>
	protected internal virtual int InsertEntry(in Entry entry) {
		var i = ~Entries.AsSpan().BinarySearch((Entry.NameHashWrapper)entry.NameHash);
		if (i < 0) // Exist
			return i;
		Entries = Entries.Insert(i, entry);
		Children = Children.Insert(i, null);
		WriteWithNewLength();
		return i;
	}

	/// <summary>
	/// Remove the child node with the given namehash
	/// </summary>
	/// <param name="nameHash"><see cref="TreeNode.NameHash"/> or namehash calculated from <see cref="TreeNode.GetNameHash"/></param>
	/// <returns>The index of the entry is removed, or ~index of the first entry with a larger namehash if not found</returns>
	/// <remarks>
	/// Modifications made to children of <see cref="GGPK.Root"/> will be restored immediately when the game starts.
	/// </remarks>
	protected internal virtual int RemoveEntry(uint nameHash) {
		var i = Entries.AsSpan().BinarySearch((Entry.NameHashWrapper)nameHash);
		if (i < 0) // Not found
			return i;
		Entries = Entries.RemoveAt(i);
		Children = Children.RemoveAt(i);
		WriteWithNewLength();
		return i;
	}

	/// <summary>
	/// Caculate the length of the record should be in ggpk file
	/// </summary>
	protected override unsafe int CaculateRecordLength() {
		return sizeof(int) * 4 + LENGTH_OF_HASH + (Ggpk.Record.GGPKVersion == 4 ? sizeof(int) : sizeof(char)) * (Name.Length + 1) + sizeof(Entry) * Entries.Length;
	}

	/// <summary>
	/// Write the record to ggpk file to its current position
	/// </summary>
	/// <remarks>
	/// <see langword="lock"/> the <see cref="GGPK.baseStream"/> while calling this method
	/// </remarks>
	[SkipLocalsInit]
	protected internal override unsafe void WriteRecordData() {
		// All method (WriteWithNewLength only) calling this will lock the stream
		var s = Ggpk.baseStream;
		Offset = s.Position;
		s.Write(Length);
		s.Write(Tag);
		s.Write(Name.Length + 1);
		s.Write(Entries.Length);
		s.Write(Hash);
		if (Ggpk.Record.GGPKVersion == 4) {
			Span<byte> span = stackalloc byte[Name.Length * sizeof(int)];
			s.Write(span[..Encoding.UTF32.GetBytes(Name, span)]);
			s.Write(0); // Null terminator
		} else {
			s.Write(Name);
			s.Write<short>(0); // Null terminator
		}
		EntriesOffset = s.Position;
		s.Write(Entries);
	}

	/// <summary>
	/// Recalculate <see cref="TreeNode.Hash"/> of the directory
	/// </summary>
	protected internal unsafe void RenewHash() {
		var combination = stackalloc Vector256<byte>[Entries.Length];
		var i = 0;
		foreach (var n in this)
			combination[i++] = n.Hash;
		if (!SHA256.TryHashData(new ReadOnlySpan<byte>((byte*)combination, LENGTH_OF_HASH * Entries.Length), _Hash.AsSpan(), out _))
			ThrowHelper.Throw<UnreachableException>("Unable to compute hash of the content"); // Hash.Length < LENGTH_OF_HASH (== sizeof(Vector256<byte>) == 32)
		var s = Ggpk.baseStream;
		lock (s) {
			s.Position = Offset + sizeof(int) * 4L;
			s.Write(Hash);
		}
	}
}