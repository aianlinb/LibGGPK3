using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
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
	/// Children of this directory.
	/// </summary>
	protected TreeNode?[] Children;

	/// <summary>
	/// Read a DirectoryRecord from GGPK
	/// </summary>
	[SkipLocalsInit]
	protected internal unsafe DirectoryRecord(uint length, GGPK ggpk) : base(length, ggpk) {
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
		Entries = new Entry[totalEntries];
		Children = new TreeNode?[totalEntries];
		s.Read(Entries);
	}

	/// <summary>
	/// Internal Usage
	/// </summary>
	protected internal DirectoryRecord(string name, GGPK ggpk) : base(default, ggpk) {
		Name = name;
		ThrowIfNameEmptyOrContainsSlash();
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
	/// Find a <see cref="TreeNode"/> with a <paramref name="path"/> relative to this directory.
	/// </summary>
	/// <param name="path">Relative path (with forward slash, but not starting or ending with slash) in ggpk under this directory</param>
	/// <param name="node">The node found, or <see langword="null"/> when not found, or <see langword="this"/> if <paramref name="path"/> is empty</param>
	/// <returns>Whether found a node.</returns>
	public bool TryFindNode(scoped ReadOnlySpan<char> path, [NotNullWhen(true)] out TreeNode? node) {
		if (path.IsEmpty) {
			node = this;
			return true;
		}

		var dir = this;
		var et = path.Split('/');
		while (et.MoveNext()) {
			var next = dir[et.Current];
			dir = (next as DirectoryRecord)!;
			if (dir is null)
				if (et.MoveNext()) {
					node = null;
					return false;
				} else {
					node = next;
					return node is not null;
				}
		}
		node = dir;
		return true;
	}
	/// <summary>
	/// Find a <see cref="DirectoryRecord"/> with a <paramref name="path"/> relative to this directory, or create it if not found.
	/// </summary>
	/// <param name="path">Relative path (with forward slashes, but not starting with slash) in ggpk under this directory</param>
	/// <param name="record">The node found</param>
	/// <returns><see langword="true"/> if added a new directory, <see langword="false"/> if found.</returns>
	public bool FindOrAddDirectory(scoped ReadOnlySpan<char> path, out DirectoryRecord record) {
		path = path.TrimEnd('/');
		if (path.IsEmpty) {
			record = this;
			return false;
		}

		var dir = this;
		var added = false;
		foreach (var name in path.Split('/'))
			if (dir.AddDirectory(new(name), out dir)) // Add directory if not found
				added = true;
		record = dir;
		return added;
	}
	/// <summary>
	/// Find a <see cref="FileRecord"/> with a <paramref name="path"/> relative to this directory, or create it if not found.
	/// </summary>
	/// <param name="path">Relative path (with forward slashes, but not starting or ending with slash) in ggpk under this directory</param>
	/// <param name="record">The node found</param>
	/// <param name="preallocatedSize">
	/// Content size in bytes of the new created file which will be passed to <see cref="FileRecord.Write(ReadOnlySpan{byte}, Vector256{byte}?)"/> of <paramref name="record"/> later by the caller.
	/// Not used when the file already exists.
	/// </param>
	/// <returns><see langword="true"/> if added a new file, <see langword="false"/> if found.</returns>
	public bool FindOrAddFile(scoped ReadOnlySpan<char> path, out FileRecord record, int preallocatedSize = 0) {
		ArgumentOutOfRangeException.ThrowIfNegative(preallocatedSize);
		if (path.IsEmpty || path.EndsWith('/'))
			ThrowHelper.Throw<ArgumentException>("File name cannot be empty", nameof(path));

		var dir = this;
		var i = path.LastIndexOf('/');
		if (i >= 0) {
			dir.FindOrAddDirectory(path[..i], out dir);
			path = path.Slice(i + 1);
		}
		return dir.AddFile(new(path), out record, preallocatedSize);
	}

	/// <summary>
	/// Add a directory to this directory, or returns the existing one with the same name.
	/// </summary>
	/// <param name="name">Name of the directory</param>
	/// <param name="record">The <see cref="DirectoryRecord"/> added/existed</param>
	/// <see langword="true"/> if the directory is added successfully, <see langword="false"/> if one with the same name already exists.
	/// <remarks>
	/// If a node exists with the same name but is not a <see cref="DirectoryRecord"/>, throws <see cref="DuplicateNameException"/>.
	/// <para>Note that modifications made to children of <see cref="GGPK.Root"/> will be restored immediately when the game starts.</para>
	/// </remarks>
	/// <exception cref="DuplicateNameException"/>
	public virtual bool AddDirectory(string name, out DirectoryRecord record) {
		record = new DirectoryRecord(name, Ggpk) { Parent = this };
		var i = InsertNode(record);
		if (i < 0) { // Exist
			record = (this[Entries[~i].NameHash] as DirectoryRecord)!;
			if (record is null)
				ThrowExist(name);
			return false;
		}
		record.WriteWithNewLength(record.Length);
		return true;
	}
	/// <summary>
	/// Add a file to this directory, or returns the existing one with the same name.
	/// </summary>
	/// <param name="name">Name of the file</param>
	/// <param name="record">The <see cref="FileRecord"/> added/existed</param>
	/// <param name="preallocatedSize">
	/// Content size in bytes of the new created file which will be passed to <see cref="FileRecord.Write(ReadOnlySpan{byte}, Vector256{byte}?)"/> of <paramref name="record"/> later by the caller.
	/// Not used when the file already exists.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if the file is added successfully, <see langword="false"/> if one with the same name already exists.
	/// </returns>
	/// <remarks>
	/// If a node exists with the same name but is not a <see cref="FileRecord"/>, throws <see cref="DuplicateNameException"/>.
	/// <para>Note that modifications made to children of <see cref="GGPK.Root"/> will be restored immediately when the game starts.</para>
	/// </remarks>
	/// <exception cref="DuplicateNameException"/>
	public virtual bool AddFile(string name, out FileRecord record, int preallocatedSize = 0) {
		ArgumentOutOfRangeException.ThrowIfNegative(preallocatedSize);
		record = new FileRecord(name, Ggpk) {
			Parent = this,
			DataLength = preallocatedSize
		};
		record.Length += (uint)preallocatedSize;
		var i = InsertNode(record);
		if (i < 0) { // Exist
			record = (this[Entries[~i].NameHash] as FileRecord)!;
			if (record is null)
				ThrowExist(name);
			return false;
		}
		record.WriteWithNewLength(record.Length);
		return true;
	}
	/// <exception cref="DuplicateNameException"/>
	[DoesNotReturn, DebuggerNonUserCode]
	internal void ThrowExist(string name) {
		throw new DuplicateNameException($"A file/directory with the same name already exists: {GetPath()}{name}");
	}

	/// <summary>
	/// Insert a <paramref name="node"/> to this directory.
	/// </summary>
	/// <returns>
	/// The index of the entry that was inserted, or ~index (always negative) of the existing entry with the same namehash.
	/// </returns>
	/// <remarks>
	/// Note that modifications made to children of <see cref="GGPK.Root"/> will be restored immediately when the game starts.
	/// </remarks>
	protected internal virtual int InsertNode(TreeNode node) {
		var i = InsertEntry(new(node.NameHash, node.Offset));
		if (i >= 0) // Inserted
			Children[i] = node;
		return i;
	}
	/// <summary>
	/// Internal implementation of <see cref="InsertNode"/>.
	/// </summary>
	/// <returns>
	/// The index of the entry that was inserted, or ~index (always negative) of the existing entry with the same namehash.
	/// </returns>
	/// <remarks>
	/// Note that modifications made to children of <see cref="GGPK.Root"/> will be restored immediately when the game starts.
	/// </remarks>
	protected virtual int InsertEntry(in Entry entry) {
		var i = ~Entries.AsSpan().BinarySearch((Entry.NameHashWrapper)entry.NameHash);
		if (i < 0) // Exist
			return i;
		Entries = Entries.Insert(i, entry);
		Children = Children.Insert(i, null);
		WriteWithNewLength();
		lock (Ggpk.baseStream)
			Ggpk.dirtyHashes.Add(this);
		return i;
	}

	/// <summary>
	/// Remove the child node with the given namehash
	/// </summary>
	/// <param name="nameHash"><see cref="TreeNode.NameHash"/> or namehash calculated from <see cref="TreeNode.GetNameHash"/></param>
	/// <returns>The index of the entry is removed, or ~index of the first entry with a larger namehash if not found</returns>
	/// <remarks>
	/// Note that modifications made to children of <see cref="GGPK.Root"/> will be restored immediately when the game starts.
	/// </remarks>
	protected internal virtual int RemoveEntry(uint nameHash) {
		var i = Entries.AsSpan().BinarySearch((Entry.NameHashWrapper)nameHash);
		if (i < 0) // Not found
			return i;
		Entries = Entries.RemoveAt(i);
		Children = Children.RemoveAt(i);
		WriteWithNewLength();
		lock (Ggpk.baseStream)
			Ggpk.dirtyHashes.Add(this);
		return i;
	}

	/// <summary>
	/// Caculate the length of the record should be in ggpk file
	/// </summary>
	protected override unsafe uint CaculateRecordLength() {
		return sizeof(int) * 4U + SIZE_OF_HASH
			+ (Ggpk.Record.GGPKVersion == 4 ? (uint)sizeof(int) : sizeof(char))
				* ((uint)Name.Length + 1U)
			+ (uint)sizeof(Entry) * (uint)Entries.Length;
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
		s.Write(Entries);
	}

	/// <summary>
	/// Recalculate <see cref="TreeNode.Hash"/> of the directory, or replace it with the given <paramref name="hash"/>.
	/// </summary>
	protected internal unsafe void RenewHash(Vector256<byte>? hash = null) {
		if (hash.HasValue)
			_Hash = hash.Value;
		else {
			var combination = stackalloc Vector256<byte>[Entries.Length];
			var i = 0;
			foreach (var n in this)
				combination[i++] = n.Hash;
			if (!SHA256.TryHashData(new ReadOnlySpan<byte>((byte*)combination, checked((int)SIZE_OF_HASH * Entries.Length)), _Hash.AsSpan(), out _))
				ThrowHelper.Throw<UnreachableException>("Unable to compute hash of the content"); // Hash.Length < LENGTH_OF_HASH (== sizeof(Vector256<byte>) == 32)
		}

		var s = Ggpk.baseStream;
		lock (s) {
			s.Position = Offset + sizeof(int) * 4L;
			s.Write(_Hash);
			Ggpk.dirtyHashes.Remove(this);
		}
	}
}