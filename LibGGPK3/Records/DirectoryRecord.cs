using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LibGGPK3.Records {
	public class DirectoryRecord : TreeNode {
		/// <summary>PDIR</summary>
		public const int Tag = 0x52494450;

		[StructLayout(LayoutKind.Sequential, Size = 12, Pack = 4)]
		protected internal struct Entry : IComparable<Entry> {
			/// <summary>
			/// Murmur2 hash of lowercase entry name
			/// </summary>
			public uint NameHash;
			/// <summary>
			/// Offset in pack file where the record begins
			/// </summary>
			public long Offset;

			public Entry(uint nameHash, long offset) {
				NameHash = nameHash;
				Offset = offset;
			}

			public readonly int CompareTo(Entry other) => NameHash.CompareTo(other.NameHash);

			/// <summary>
			/// Find the index of the entry with the given <paramref name="nameHash"/> in the given <paramref name="array"/>.
			/// </summary>
			/// <returns>The index of the entry found, or the ~index of the first entry with a larger namehash if not found.</returns>
			public static int BinarySearch(in Entry[] array, uint nameHash) { // From ArraySortHelper{T}.InternalBinarySearch
				var lo = 0;
				var hi = array.Length - 1;
				while (lo <= hi) {
					var i = lo + ((hi - lo) >> 1);
					var order = array[i].NameHash.CompareTo(nameHash);

					if (order == 0)
						return i;
					if (order < 0)
						lo = i + 1;
					else
						hi = i - 1;
				}
				return ~lo;
			}
		}

		/// <summary>
		/// Records (File/Directory) this directory contains.
		/// </summary>
		/// <remarks>They must be sorted by <see cref="Entry.NameHash"/></remarks>
		protected internal Entry[] Entries;
		/// <summary>
		/// Offset in pack file where <see cref="Entries"/> begins. This is only here because it makes rewriting the entries easier.
		/// </summary>
		protected internal long EntriesBegin { get; protected set; }

		/// <summary>
		/// Read a DirectoryRecord from GGPK
		/// </summary>
		protected unsafe internal DirectoryRecord(int length, GGPK ggpk) : base(length, ggpk) {
			var s = ggpk.baseStream;
			Offset = s.Position - 8;
			var nameLength = s.Read<int>() - 1; // '\0'
			var totalEntries = s.Read<int>();
			s.ReadExactly(_Hash, 0, /*_Hash.Length*/32);
			if (Ggpk.GgpkRecord.GGPKVersion == 4) {
				nameLength *= sizeof(int); // UTF32
				Span<byte> b = stackalloc byte[nameLength];
				s.ReadExactly(b);
				Name = Encoding.UTF32.GetString(b);
				s.Seek(sizeof(int), SeekOrigin.Current); // Null terminator
			} else {
				Name = s.ReadUTF16String(nameLength);
				s.Seek(sizeof(char), SeekOrigin.Current); // Null terminator
			}
			EntriesBegin = s.Position;
			Entries = new Entry[totalEntries];
			fixed (Entry* p = Entries)
				s.ReadExactly(new(p, totalEntries * sizeof(Entry)));
		}

		protected internal DirectoryRecord(string name, GGPK ggpk) : base(default, ggpk) {
			Name = name;
			Entries = Array.Empty<Entry>();
			Length = CaculateRecordLength();
		}

		protected internal Dictionary<uint, TreeNode>? _Children;
		[MemberNotNull(nameof(_Children))]
		public virtual IEnumerable<TreeNode> Children {
			get {
				if (_Children?.Count == Entries.Length)
					return _Children.Values;

				// [MemberNotNull(nameof(_Children))] Not working here
				IEnumerable<TreeNode> GetChildren() {
					if (_Children == null) {
						_Children = new(Entries.Length);
						foreach (var e in Entries) {
							var node = (TreeNode)Ggpk.ReadRecord(e.Offset);
							node.Parent = this;
							node._NameHash = e.NameHash;
							_Children[e.NameHash] = node;
							yield return node;
						}
					} else {
						foreach (var e in Entries) {
							if (!_Children.TryGetValue(e.NameHash, out var node)) {
								node = (TreeNode)Ggpk.ReadRecord(e.Offset);
								node.Parent = this;
								_Children[e.NameHash] = node;
							}
							yield return node;
						}
					}
				}
#pragma warning disable CS8774 // _Children was set in GetChildren()
				return GetChildren();
#pragma warning restore CS8774
			}
		}

		/// <summary>
		/// Get child with the given namehash
		/// </summary>
		/// <param name="nameHash">namehash calculated from <see cref="TreeNode.GetNameHash"/> or <see cref="TreeNode.NameHash"/></param>
		[MemberNotNull(nameof(_Children))]
		public TreeNode? this[uint nameHash] {
			get {
				_Children ??= new(Entries.Length);
				if (!_Children.TryGetValue(nameHash, out var node)) {
					var i = Entry.BinarySearch(Entries, nameHash);
					if (i < 0)
						return null;
					node = (TreeNode)Ggpk.ReadRecord(Entries[i].Offset);
					node.Parent = this;
					_Children[nameHash] = node;
				}
				return node;
			}
		}
		[MemberNotNull(nameof(_Children))]
#pragma warning disable CS8774 // _Children was set in this[uint nameHash]
		public TreeNode? this[string name] => this[GetNameHash(name)];
#pragma warning restore CS8774

		/// <summary>
		/// Add a directory to this directory (which can't be <see cref="GGPK.Root"/>)
		/// </summary>
		/// <param name="name">Name of the directory</param>
		/// <remarks>Experimental function, may produce unexpected errors</remarks>
		public virtual DirectoryRecord AddDirectory(string name) {
			var dir = new DirectoryRecord(name, Ggpk) { Parent = this };
			AddNode(dir);
			return dir;
		}

		/// <summary>
		/// Add a file to this directory (which can't be <see cref="GGPK.Root"/>)
		/// </summary>
		/// <param name="name">Name of the file</param>
		/// <param name="content">Content of the file</param>
		/// <remarks>Experimental function, may produce unexpected errors</remarks>
		public virtual FileRecord AddFile(string name, ReadOnlySpan<byte> content = default) {
			var file = new FileRecord(name, Ggpk) { Parent = this };
			if (!content.IsEmpty) {
				file.Length += file.DataLength = content.Length;
				if (!FileRecord.Hash256.TryComputeHash(content, file._Hash, out _))
					throw new("Unable to compute hash of the content");
				AddNode(file);
				Ggpk.baseStream.Position = file.DataOffset;
				Ggpk.baseStream.Write(content);
			} else
				AddNode(file);
			return file;
		}

		/// <summary>
		/// Internal implementation of <see cref="AddDirectory"/> and <see cref="AddFile"/>
		/// </summary>
		protected virtual void AddNode(TreeNode node) {
			if (InsertEntry(new(node.NameHash, default)) < 0) // Entry.Offset will be set in node.WriteWithNewLength(int) which calls TreeNode.UpdateOffset()
				throw new InvalidOperationException("A file/directory with the same is already exist: " + node.GetPath());
			(_Children ??= new(Entries.Length)).Add(node.NameHash, node);
			WriteWithNewLength(CaculateRecordLength());
			node.WriteWithNewLength(node.Length);
		}

		/// <returns>
		/// The index of the entry is inserted, or ~index of the existing entry with the same namehash if failed to insert.
		/// </returns>
		protected internal virtual int InsertEntry(in Entry entry) {
			if (this == Ggpk.Root)
				throw new InvalidOperationException("You can't change child elements of the root folder, otherwise it will break the GGPK when the game starts");
			var i = ~Entry.BinarySearch(Entries, entry.NameHash);
			if (i < 0) // Exist
				return i;

			// Array.Insert(ref Entries, i, entry);
			var tmp = Entries;
			Entries = new Entry[tmp.Length + 1];
			Entries[i] = entry;
			if (i > 0)
				Array.Copy(tmp, 0, Entries, 0, i);
			if (i < Entries.Length)
				Array.Copy(tmp, i, Entries, i + 1, Entries.Length - i - 1);
			WriteWithNewLength(CaculateRecordLength());
			return i;
		}

		/// <summary>
		/// Remove the child node with the given namehash
		/// </summary>
		/// <param name="nameHash"><see cref="TreeNode.NameHash"/> or namehash calculated from <see cref="TreeNode.GetNameHash"/></param>
		/// <returns>The index of the entry is removed, or ~index of the first entry with a larger namehash if not found</returns>
		protected internal virtual int RemoveEntry(uint nameHash) {
			if (this == Ggpk.Root)
				throw new InvalidOperationException("You can't change child elements of the root folder, otherwise it will break the GGPK when the game starts");
			var i = Entry.BinarySearch(Entries, nameHash);
			if (i < 0) // Not found
				return i;
			_Children?.Remove(nameHash);

			// Array.RemoveAt(ref Entries, i);
			var tmp = Entries;
			Entries = new Entry[tmp.Length - 1];
			if (i > 0)
				Array.Copy(tmp, 0, Entries, 0, i);
			if (i < Entries.Length)
				Array.Copy(tmp, i + 1, Entries, i, Entries.Length - i);
			WriteWithNewLength(CaculateRecordLength());
			return i;
		}

		/// <summary>
		/// Caculate the length of the record should be in ggpk file
		/// </summary>
		protected unsafe override int CaculateRecordLength() {
			return Entries.Length * sizeof(Entry) + (Name.Length + 1) * (Ggpk.GgpkRecord.GGPKVersion == 4 ? 4 : 2) + 48; // 4 + 4 + 4 + 4 + Hash.Length + (Name + "\0").Length * 2 + Entries.Length * 12
		}

		/// <summary>
		/// Write the record to ggpk file to its current position
		/// </summary>
		protected internal unsafe override void WriteRecordData() {
			var s = Ggpk.baseStream;
			Offset = s.Position;
			s.Write(Length);
			s.Write(Tag);
			s.Write(Name.Length + 1);
			s.Write(Entries.Length);
			s.Write(_Hash, 0, /*_Hash.Length*/32); // Keep the hash original to prevent the game start patching
			if (Ggpk.GgpkRecord.GGPKVersion == 4) {
				var b = Encoding.UTF32.GetBytes(Name);
				s.Write(b, 0, b.Length);
				s.Write(0); // 4 bytes null terminator
			} else {
				fixed (char* p = Name)
					s.Write(new(p, Name.Length * 2));
				s.Write((short)0); // 2 bytes null terminator
			}
			EntriesBegin = s.Position;
			fixed (Entry* p = Entries)
				s.Write(new(p, sizeof(Entry) * Entries.Length));
		}
	}
}