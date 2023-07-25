using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace LibGGPK3.Records {
	public abstract class TreeNode : BaseRecord {
		private static readonly byte[] HashOfEmpty = Convert.FromHexString("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");

		/// <summary>
		/// File/Directory name
		/// </summary>
		public string Name { get; protected set; } = "";
		/* 
		public string Name {
			get => _Name;
			set {
				_NameHash = null;
				_Name = value;
			}
		}
		*/
		/// <summary>
		/// SHA256 hash of the file content
		/// </summary>
		public ReadOnlySpan<byte> Hash => _Hash;
		/// <summary>
		/// SHA256 hash of the file content
		/// </summary>
		protected internal byte[] _Hash = (byte[])HashOfEmpty.Clone();
		/// <summary>
		/// Parent node
		/// </summary>
		public DirectoryRecord? Parent { get; protected internal set; }

		protected TreeNode(int length, GGPK ggpk) : base(length, ggpk) {
		}

		/// <summary>
		/// This won't update the offset in <see cref="DirectoryRecord.Entries"/> of <see cref="Parent"/>
		/// </summary>
		/// <param name="specify">The length of specified FreeRecord must not be between Length and Length-16 (exclusive)</param>
		protected internal virtual void WriteWithNewLength(LinkedListNode<FreeRecord>? specify = null) {
			var s = Ggpk.GGPKStream;
			specify ??= Ggpk.FindBestFreeRecord(Length);
			if (specify == null) {
				s.Seek(0, SeekOrigin.End); // Write to the end of GGPK
				WriteRecordData();
			} else {
				var free = specify.Value;
				if (free.Length < Length + 16 && free.Length != Length)
					throw new ArgumentException("The length of specified FreeRecord must not be between Length and Length-16 (exclusive): " + free.Length, nameof(specify));
				s.Seek(free.Offset, SeekOrigin.Begin);
				WriteRecordData();
				free.Length -= Length;
				if (free.Length >= 16) { // Update length of FreeRecord
					s.Seek(free.Offset + Length, SeekOrigin.Begin);
					free.WriteRecordData();
					free.UpdateOffset();
				} else
					free.RemoveFromList(specify);
			}
		}

		protected internal virtual LinkedListNode<FreeRecord>? MoveWithNewLength(int newLength, LinkedListNode<FreeRecord>? specify = null) {
			if (newLength == Length && specify == null)
				return null;
			var free = MarkAsFreeRecord();
			Length = newLength;
			WriteWithNewLength(specify);
			UpdateOffset();
			return free;
		}

		/// <summary>
		/// Set the record to a FreeRecord
		/// </summary>
		protected virtual LinkedListNode<FreeRecord>? MarkAsFreeRecord() {
			var s = Ggpk.GGPKStream;
			s.Seek(Offset, SeekOrigin.Begin);
			LinkedListNode<FreeRecord>? rtn = null;
			var length = Length;
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
			if (rtn != null) {
				var rtnv = rtn.Value;
				if (rtnv.Offset + rtnv.Length >= s.Length) {
					rtnv.RemoveFromList(rtn);
					s.Flush();
					s.SetLength(rtnv.Offset);
					return null;
				}
				s.Seek(rtnv.Offset, SeekOrigin.Begin);
				s.Write(rtnv.Length);
				return rtn;
			}
			if (Offset + length >= s.Length) {
				s.Flush();
				s.SetLength(Offset);
				return null;
			}

			var free = new FreeRecord(Offset, length, 0, Ggpk);
			return free.UpdateOffset();
		}

		/// <summary>
		/// Update the offset of this record in <see cref="Parent"/>.<see cref="DirectoryRecord.Entries"/>
		/// </summary>
		protected virtual void UpdateOffset() {
			if (Parent is DirectoryRecord dr) {
				for (int i = 0; i < dr.Entries.Length; ++i)
					if (dr.Entries[i].NameHash == NameHash) {
						dr.Entries[i].Offset = Offset;
						Ggpk.GGPKStream.Seek(dr.EntriesBegin + i * 12 + 4, SeekOrigin.Begin);
						Ggpk.GGPKStream.Write(Offset);
						return;
					}
				throw new(GetPath() + " update offset faild: " + Offset);
			} else if (this == Ggpk.Root) {
				Ggpk.GgpkRecord.RootDirectoryOffset = Offset;
				Ggpk.GGPKStream.Seek(Ggpk.GgpkRecord.Offset + 12, SeekOrigin.Begin);
				Ggpk.GGPKStream.Write(Offset);
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

		protected uint? _NameHash;
		/// <summary>
		/// Get the murmur hash of name of this File/Directory
		/// </summary>
		public virtual uint NameHash => _NameHash ??= GetNameHash(Name);

		public static uint GetNameHash(string name) => MurmurHash2Unsafe.Hash(name.ToLower(), 0);

		/// <summary>
		/// Use to sort the children of directory.
		/// </summary>
		public sealed class NodeComparer : IComparer<TreeNode> {
			public static readonly NodeComparer Instance = new();
			private NodeComparer() { }

			[DllImport("shlwapi", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
			private static extern int StrCmpLogicalW(string x, string y);
			private static readonly Func<string, string, int> FuncCompare = OperatingSystem.IsWindows() ? StrCmpLogicalW : string.Compare;
#pragma warning disable CS8767
			public int Compare(TreeNode x, TreeNode y) {
				if (x is DirectoryRecord)
					if (y is DirectoryRecord)
						return FuncCompare(x.Name, y.Name);
					else
						return -1;
				else
					if (y is DirectoryRecord)
					return 1;
				else
					return FuncCompare(x.Name, y.Name);
			}
		}
	}
}