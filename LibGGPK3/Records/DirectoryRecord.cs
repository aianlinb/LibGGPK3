using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace LibGGPK3.Records {
	public class DirectoryRecord : TreeNode {
		/// <summary>PDIR</summary>
		public const uint Tag = 0x52494450;

		[StructLayout(LayoutKind.Sequential, Size = 12, Pack = 1)]
		protected internal struct Entry {
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
		}

		/// <summary>
		/// Records (File/Directory) this directory contains.
		/// </summary>
		protected internal Entry[] Entries;
		/// <summary>
		/// Offset in pack file where entries list begins. This is only here because it makes rewriting the entries list easier.
		/// </summary>
		protected internal long EntriesBegin { get; protected set; }

		/// <summary>
		/// Read a DirectoryRecord from GGPK
		/// </summary>
		protected unsafe internal DirectoryRecord(int length, GGPK ggpk) : base(length, ggpk) {
			var s = ggpk.GGPKStream;
			Offset = s.Position - 8;
			var nameLength = s.ReadInt32() - 1;
			var totalEntries = s.ReadInt32();
			s.Read(_Hash, 0, 32);
			if (Ggpk.GgpkRecord.GGPKVersion == 4) {
				var b = new byte[nameLength * 4];
				s.Read(b, 0, b.Length);
				Name = Encoding.UTF32.GetString(b);
				s.Seek(4, SeekOrigin.Current); // Null terminator
			} else {
				Name = s.ReadUnicodeString(nameLength);
				s.Seek(2, SeekOrigin.Current); // Null terminator
			}
			EntriesBegin = s.Position;
			Entries = new Entry[totalEntries];
			fixed (Entry* p = Entries)
				s.Read(new(p, totalEntries * 12));
		}

		protected internal DirectoryRecord(string name, GGPK ggpk) : base(default, ggpk) {
			Name = name;
			Entries = Array.Empty<Entry>();
			Length = CaculateRecordLength();
		}

		protected Dictionary<uint, TreeNode>? _Children;
		public virtual IEnumerable<TreeNode> Children {
			get {
				if (_Children?.Count == Entries.Length)
					return _Children.Values;
				return GetChildren();
			}
		}
		protected virtual IEnumerable<TreeNode> GetChildren() {
			if (_Children == null) {
				_Children = new(Entries.Length);
				foreach (var e in Entries) {
					var node = (TreeNode)Ggpk.ReadRecord(e.Offset);
					node.Parent = this;
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

		/// <summary>
		/// Get child with the given namehash
		/// </summary>
		/// <param name="NameHash">namehash calculated from <see cref="TreeNode.GetNameHash"/></param>
		public TreeNode? this[uint NameHash] {
			get {
				_Children ??= new(Entries.Length);
				if (!_Children.TryGetValue(NameHash, out var node))
					foreach (var e in Entries)
						if (e.NameHash == NameHash) {
							node = (TreeNode)Ggpk.ReadRecord(e.Offset);
							node.Parent = this;
							_Children[NameHash] = node;
							break;
						}
				return node;
			}
		}

		/// <summary>
		/// Add a directory to this directory
		/// </summary>
		/// <param name="name">Name of the directory</param>
		public virtual DirectoryRecord AddDirectory(string name) {
			if (this == Ggpk.Root)
				throw new InvalidOperationException("You can't add child elements to the root folder, otherwise it will break the GGPK when the game starts");
			var dir = new DirectoryRecord(name, Ggpk) {
				Parent = this
			};
			dir.WriteWithNewLength();
			Array.Resize(ref Entries, Entries.Length + 1);
			Entries[^1] = new Entry(dir.NameHash, dir.Offset);
			_Children ??= new(Entries.Length);
			_Children.Add(dir.NameHash, dir);
			MoveWithNewLength(CaculateRecordLength());
			return dir;
		}

		/// <summary>
		/// Add a file to this directory
		/// </summary>
		/// <param name="name">Name of the file</param>
		/// <param name="content"><see langword="null"/> for no content</param>
		public virtual FileRecord AddFile(string name, ReadOnlySpan<byte> content = default) {
			if (this == Ggpk.Root)
				throw new InvalidOperationException("You can't add child elements to the root folder, otherwise it will break the GGPK when the game starts");
			var file = new FileRecord(name, Ggpk) {
				Parent = this
			};
			if (content != null) {
				if (!FileRecord.Hash256.TryComputeHash(content, file._Hash, out _))
					throw new("Unable to compute hash of the content");
				file.Length += file.DataLength = content.Length;
				file.WriteWithNewLength();
				Ggpk.GGPKStream.Seek(file.DataOffset, SeekOrigin.Begin);
				Ggpk.GGPKStream.Write(content);
			} else
				file.WriteWithNewLength();
			Array.Resize(ref Entries, Entries.Length + 1);
			Entries[^1] = new Entry(file.NameHash, file.Offset);
			_Children ??= new(Entries.Length);
			_Children.Add(file.NameHash, file);
			MoveWithNewLength(CaculateRecordLength());
			return file;
		}

		/// <summary>
		/// Add an exist <see cref="TreeNode"/> to this directory,
		/// <paramref name="node"/> must not be <see cref="GGPK.Root"/> which breaks ggpk
		/// </summary>
		public virtual void AddNode(TreeNode node) {
			if (this == Ggpk.Root)
				throw new InvalidOperationException("You can't add child elements to the root folder, otherwise it will break the GGPK when the game starts");
			node.Parent = this;
			Array.Resize(ref Entries, Entries.Length + 1);
			Entries[^1] = new Entry(node.NameHash, node.Offset);
			_Children ??= new(Entries.Length);
			_Children.Add(node.NameHash, node);
			MoveWithNewLength(CaculateRecordLength());
		}

		/// <summary>
		/// Remove the child node with the given namehash
		/// </summary>
		/// <param name="nameHash">namehash calculated from <see cref="TreeNode.GetNameHash"/></param>
		public virtual unsafe void RemoveChild(uint nameHash) {
			_Children?.Remove(nameHash);
			for (var i = 0; i < Entries.Length ; ++i) {
				if (Entries[i].NameHash == nameHash) {
					var tmp = Entries;
					Entries = new Entry[tmp.Length - 1];
					fixed (Entry* old = tmp, e = Entries) {
						var offset = i * sizeof(Entry);
						Unsafe.CopyBlockUnaligned(e, old, (uint)offset);
						Unsafe.CopyBlockUnaligned(e + offset, old + offset + sizeof(Entry), (uint)((tmp.Length - i) * sizeof(Entry)));
					}
					MoveWithNewLength(CaculateRecordLength());
					break;
				}
			}
		}

		protected override int CaculateRecordLength() {
			return Entries.Length * 12 + (Name.Length + 1) * (Ggpk.GgpkRecord.GGPKVersion == 4 ? 4 : 2) + 48; // 4 + 4 + 4 + 4 + Hash.Length + (Name + "\0").Length * 2 + Entries.Length * 12
		}

		protected internal unsafe override void WriteRecordData() {
			var s = Ggpk.GGPKStream;
			Offset = s.Position;
			s.Write(Length);
			s.Write(Tag);
			s.Write(Name.Length + 1);
			s.Write(Entries.Length);
			s.Write(Hash);
			if (Ggpk.GgpkRecord.GGPKVersion == 4) {
				s.Write(Encoding.UTF32.GetBytes(Name));
				s.Write(0); // Null terminator
			} else {
				fixed (char* p = Name)
					s.Write(new(p, Name.Length * 2));
				s.Write((short)0); // Null terminator
			}
			EntriesBegin = s.Position;
			fixed (Entry* p = Entries)
				s.Write(new(p, Entries.Length * 12));
		}
	}
}