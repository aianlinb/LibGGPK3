using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace LibGGPK3.Records {
	public class DirectoryRecord : TreeNode {
		public struct Entry {
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

		public static readonly byte[] Tag = new byte[] { (byte)'P', (byte)'D', (byte)'I', (byte)'R' };

		/// <summary>
		/// Records (File/Directory) this directory contains.
		/// </summary>
		public Entry[] Entries;
		/// <summary>
		/// Offset in pack file where entries list begins. This is only here because it makes rewriting the entries list easier.
		/// </summary>
		public long EntriesBegin;

		/// <summary>
		/// Read a DirectoryRecord from GGPK
		/// </summary>
		protected unsafe internal DirectoryRecord(int length, GGPK ggpk) : base(length, ggpk) {
			var s = ggpk.FileStream;
			Offset = s.Position - 8;
			var nameLength = s.ReadInt32();
			var totalEntries = s.ReadInt32();
			s.Read(Hash, 0, 32);

			var name = new char[nameLength - 1];
			fixed (char* p = name)
				s.Read(new(p, name.Length * 2));

			Name = new(name);
			s.Seek(2, SeekOrigin.Current); // Null terminator

			EntriesBegin = s.Position;
			Entries = new Entry[totalEntries];
			for (var i = 0; i < totalEntries; i++)
				Entries[i] = new Entry((uint)s.ReadInt32(), s.ReadInt64());
		}

		protected internal DirectoryRecord(string name, GGPK ggpk) : base(default, ggpk) {
			Name = name;
			Entries = Array.Empty<Entry>();
			Length = CaculateLength();
		}

		private SortedSet<TreeNode>? _Children;
		/// <summary>
		/// Do not add/remove any elements from here
		/// </summary>
		public virtual SortedSet<TreeNode> Children {
			get {
				if (_Children == null) {
					_Children = new SortedSet<TreeNode>(NodeComparer.Instance);
					foreach (var e in Entries) {
						var b = (TreeNode)Ggpk.ReadRecord(e.Offset);
						b.Parent = this;
						_Children.Add(b);
					}
				}
				return _Children;
			}
		}

		public virtual DirectoryRecord AddDirectory(string name, Entry[]? entries = null) {
			if (this == Ggpk.Root)
				throw new InvalidOperationException("You can't add child elements to the root folder, otherwise it will break the GGPK when the game starts");
			var dir = new DirectoryRecord(name, Ggpk) {
				Parent = this
			};
			if (entries != null) {
				dir.Entries = entries;
				dir.Length = dir.CaculateLength();
			}
			dir.WriteWithNewLength();
			Children.Add(dir);
			Array.Resize(ref Entries, Entries.Length + 1);
			Entries[^1] = new Entry(dir.GetNameHash(), dir.Offset);
			MoveWithNewLength(CaculateLength());
			return dir;
		}

		/// <summary>
		/// Add a file to this directory
		/// </summary>
		/// <param name="name">Name of the FileRecord</param>
		/// <param name="content"><see langword="null"/> for no content</param>
		public virtual FileRecord AddFile(string name, ReadOnlySpan<byte> content = default) {
			if (this == Ggpk.Root)
				throw new InvalidOperationException("You can't add child elements to the root folder, otherwise it will break the GGPK when the game starts");
			var file = new FileRecord(name, Ggpk) {
				Parent = this
			};
			if (content != null) {
				if (!FileRecord.Hash256.TryComputeHash(content, file.Hash, out _))
					throw new("Unable to compute hash of the content");
				file.Length += file.DataLength = content.Length;
				file.WriteWithNewLength();
				Ggpk.FileStream.Seek(file.DataOffset, SeekOrigin.Begin);
				Ggpk.FileStream.Write(content);
			} else
				file.WriteWithNewLength();
			Children.Add(file);
			Array.Resize(ref Entries, Entries.Length + 1);
			Entries[^1] = new Entry(file.GetNameHash(), file.Offset);
			MoveWithNewLength(CaculateLength());
			return file;
		}

		/// <summary>
		/// Add a exist FileRecord to this directory
		/// </summary>
		public virtual void AddFile(FileRecord fileRecord) {
			if (this == Ggpk.Root)
				throw new InvalidOperationException("You can't add child elements to the root folder, otherwise it will break the GGPK when the game starts");
			fileRecord.Parent = this;
			Children.Add(fileRecord);
			Array.Resize(ref Entries, Entries.Length + 1);
			Entries[^1] = new Entry(fileRecord.GetNameHash(), fileRecord.Offset);
			MoveWithNewLength(CaculateLength());
		}

		public virtual void RemoveChild(TreeNode node, bool markAsFree = false) {
			Children.Remove(node);
			if (markAsFree)
				node.MarkAsFreeRecord();
			var list = Entries.ToList();
			var i = list.FindIndex(e => e.Offset == node.Offset);
			if (i >= 0) {
				list.RemoveAt(i);
				Entries = list.ToArray();
				MoveWithNewLength(CaculateLength());
			}
		}

		public override int CaculateLength() {
			return Entries.Length * 12 + Name.Length * 2 + 50; // (4 + 4 + 4 + 4 + Entries.Length + Hash.Length + (Name + "\0").Length * 2) + Entries.Length * 12
		}

		protected internal unsafe override void WriteRecordData() {
			var s = Ggpk.FileStream;
			Offset = s.Position;
			s.Write(Length);
			s.Write(Tag);
			s.Write(Name.Length + 1);
			s.Write(Entries.Length);
			s.Write(Hash);
			fixed (char* p = Name)
				s.Write(new(p, Name.Length * 2));
			s.Write((short)0); // Null terminator
			EntriesBegin = s.Position;
			foreach (var entry in Entries) {
				s.Write(entry.NameHash);
				s.Write(entry.Offset);
			}
			s.Flush();
		}

		/// <summary>
		/// Use to sort the children of directory.
		/// </summary>
		protected sealed class NodeComparer : IComparer<TreeNode> {
			public static readonly IComparer<TreeNode> Instance = OperatingSystem.IsWindows() ? new NodeComparer_Windows() : new NodeComparer();

#pragma warning disable CS8767
			public int Compare(TreeNode x, TreeNode y) {
				if (x is DirectoryRecord)
					if (y is DirectoryRecord)
						return string.Compare(x.Name, y.Name);
					else
						return -1;
				else
					if (y is DirectoryRecord)
						return 1;
					else
						return string.Compare(x.Name, y.Name);
			}

			public sealed class NodeComparer_Windows : IComparer<TreeNode> {
				[DllImport("shlwapi", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
				public static extern int StrCmpLogicalW(string x, string y);
				public int Compare(TreeNode x, TreeNode y) {
					if (x is DirectoryRecord)
						if (y is DirectoryRecord)
							return StrCmpLogicalW(x.Name, y.Name);
						else
							return -1;
					else
						if (y is DirectoryRecord)
							return 1;
						else
							return StrCmpLogicalW(x.Name, y.Name);
				}
			}
		}
	}
}