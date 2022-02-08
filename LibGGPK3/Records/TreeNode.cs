using System;
using System.Collections.Generic;
using System.IO;

namespace LibGGPK3.Records {
	public abstract class TreeNode : BaseRecord {
		private static readonly byte[] HashOfEmpty = Convert.FromHexString("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");
		/// <summary>
		/// File/Directory name
		/// </summary>
		public string Name = "";
		/// <summary>
		/// SHA256 hash of the file content
		/// </summary>
		public byte[] Hash = (byte[])HashOfEmpty.Clone();
		/// <summary>
		/// Parent node
		/// </summary>
		public TreeNode? Parent;

		protected TreeNode(int length, GGPK ggpk) : base(length, ggpk) {
		}

		/// <summary>
		/// This won't update the offset in <see cref="DirectoryRecord.Entries"/> of <see cref="Parent"/>
		/// </summary>
		/// <param name="specify">The length of specified FreeRecord must not be between Length and Length-16 (exclusive)</param>
		protected internal virtual void WriteWithNewLength(LinkedListNode<FreeRecord>? specify = null) {
			var s = Ggpk.FileStream;
			specify ??= Ggpk.FindBestFreeRecord(Length, out _);
			if (specify == null) {
				s.Seek(0, SeekOrigin.End); // Write to the end of GGPK
				WriteRecordData();
			} else {
				var free = specify.Value;
				if (free.Length < Length + 16 && free.Length != Length)
					throw new ArgumentException("The length of specified FreeRecord must not be between Length and Length-16 (exclusive): " + free.Length, nameof(specify));
				free.Length -= Length;
				s.Seek(free.Offset + free.Length, SeekOrigin.Begin); // Write to the end of the FreeRecord
				WriteRecordData();
				if (free.Length >= 16) { // Update length of FreeRecord
					s.Seek(free.Offset, SeekOrigin.Begin);
					s.Write(free.Length);
				} else
					free.RemoveFromList(specify);
			}
		}

		public virtual LinkedListNode<FreeRecord>? MoveWithNewLength(int newLength, LinkedListNode<FreeRecord>? specify = null) {
			if (newLength == Length && specify == null)
				return null;
			var oldOffset = Offset;
			var free = MarkAsFreeRecord();
			Length = newLength;
			WriteWithNewLength(specify);
			UpdateOffset(oldOffset);
			Ggpk.FileStream.Flush();
			return free;
		}

		/// <summary>
		/// Set the record to a FreeRecord
		/// </summary>
		public virtual LinkedListNode<FreeRecord>? MarkAsFreeRecord() {
			var s = Ggpk.FileStream;
			s.Flush();
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
					s.SetLength(rtnv.Offset);
					return null;
				}
				s.Seek(rtnv.Offset, SeekOrigin.Begin);
				s.Write(rtnv.Length);
				return rtn;
			}
			if (Offset + length >= s.Length) {
				s.SetLength(Offset);
				return null;
			}

			var free = new FreeRecord(Offset, length, 0, Ggpk);
			var lastFree = Ggpk.FreeRecords.Last?.Value;
			if (lastFree == null) { // No FreeRecord
				Ggpk.GgpkRecord.FirstFreeRecordOffset = Offset;
				s.Seek(Ggpk.GgpkRecord.Offset + 20, SeekOrigin.Begin);
				s.Write(Offset);
			} else {
				lastFree.NextFreeOffset = Offset;
				s.Seek(lastFree.Offset + 8, SeekOrigin.Begin);
				s.Write(Offset);
			}
			return Ggpk.FreeRecords.AddLast(free);
		}

		/// <summary>
		/// Update the offset of this record in <see cref="Parent"/>.<see cref="DirectoryRecord.Entries"/>
		/// </summary>
		/// <param name="oldOffset">The original offset to be update</param>
		protected virtual void UpdateOffset(long oldOffset) {
			if (oldOffset == Offset)
				return;
			if (Parent is DirectoryRecord dr) {
				for (int i = 0; i < dr.Entries.Length; i++) {
					if (dr.Entries[i].Offset == oldOffset) {
						dr.Entries[i].Offset = Offset;
						Ggpk.FileStream.Seek(dr.EntriesBegin + i * 12 + 4, SeekOrigin.Begin);
						Ggpk.FileStream.Write(Offset);
						return;
					}
				}
				throw new(GetPath() + " update offset faild: " + oldOffset.ToString() + " => " + Offset.ToString());
			} else if (this == Ggpk.Root) {
				Ggpk.GgpkRecord.RootDirectoryOffset = Offset;
				Ggpk.FileStream.Seek(Ggpk.GgpkRecord.Offset + 12, SeekOrigin.Begin);
				Ggpk.FileStream.Write(Offset);
			} else
				throw new NullReferenceException(nameof(Parent));
		}

		public abstract int CaculateLength();

		/// <exception cref="NullReferenceException">thrown when <see cref="Parent"/> is null</exception>
		public virtual void Remove() {
			((DirectoryRecord)Parent!).RemoveChild(this, true);
		}

		/// <summary>
		/// Get the full path in GGPK of this File/Directory
		/// </summary>
		public virtual string GetPath() {
			return this is FileRecord ? (Parent?.GetPath() ?? "") + Name : (Parent?.GetPath() ?? "") + Name + "/";
		}

		/// <summary>
		/// Get the murmur hash of name of this File/Directory
		/// </summary>
		public virtual uint GetNameHash() {
			return MurmurHash2Unsafe.Hash(Name.ToLower(), 0);
		}
	}
}