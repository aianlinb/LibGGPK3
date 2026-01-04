using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

using SystemExtensions.Streams;

namespace LibGGPK3.Records;
/// <summary>
/// A free record represents space in the pack file that has been marked as deleted. It's much cheaper to just
/// mark areas as free and append data to a suitable location than it is to rebuild the entire pack file just
/// to remove a piece of data.
/// </summary>
public class FreeRecord : BaseRecord {
	/// <summary>FREE</summary>
	public const int Tag = 0x45455246;

	/// <summary>
	/// Offset of next <see cref="FreeRecord"/> in the linked-list
	/// </summary>
	public long NextFreeOffset { get; protected set; }

	public FreeRecord? Previous { get; protected set; }

	protected FreeRecord? _Next;
	public virtual FreeRecord? Next {
		get {
			if (_Next is null && NextFreeOffset != 0) {
				_Next = (FreeRecord)Ggpk.ReadRecord(NextFreeOffset);
				_Next.Previous = this;
			}
			return _Next;
		}
		protected internal set {
			if (_Next != null)
				_Next.Previous = null;
			if (value is null)
				NextFreeOffset = 0;
			else {
				NextFreeOffset = value.Offset;
				if (value.Previous != null)
					value.Previous.Next = null;
				value.Previous = this;
			}
			_Next = value;
		}
	}

	protected internal FreeRecord(uint length, GGPK ggpk) : base(length, ggpk) {
		Offset = ggpk.baseStream.Position - 8;
		NextFreeOffset = ggpk.baseStream.Read<long>();
		ggpk.baseStream.Position = Offset + Length;
	}

	/// <summary>
	/// Also calls the <see cref="WriteRecordData"/>.
	/// Please calls <see cref="UpdateOffset"/> after this to add the FreeRecord to <see cref="GGPK.FreeRecords"/>
	/// </summary>
	protected internal FreeRecord(long offset, uint length, long nextFreeOffset, GGPK ggpk) : base(length, ggpk) {
		Offset = offset;
		NextFreeOffset = nextFreeOffset;
	}

	protected internal override void WriteRecordData() {
		var s = Ggpk.baseStream;
		Offset = s.Position;
		s.Write(Length);
		s.Write(Tag);
		s.Write(NextFreeOffset);
		s.Seek(Length - (sizeof(int) + sizeof(int) + sizeof(long)), SeekOrigin.Current);
	}

	/*internal int GetSortedIndex() {
		var list = Ggpk._SortedFreeRecords;
		if (list is null)
			return -1;

		var i = CollectionsMarshal.AsSpan(list).BinarySearch(new LengthWrapper(Length - 1));
		if (i < 0)
			i = ~i;
		else if (++i == list.Count)
			return ~i;

		while (list[i] != this)
			if (++i == list.Count || list[i].Length > Length)
				return ~i;
		return i;
	}*/

	/// <summary>
	/// Remove this FreeRecord from the Linked FreeRecord List of ggpk
	/// </summary>
	protected internal virtual void RemoveFromList() {
		var s = Ggpk.baseStream;
		lock (s) {
			Ggpk._SortedFreeRecords?.Remove(this);
			if (Next is null) {
				if (Previous is null) {
					if (Ggpk.FirstFreeRecord == this) {
						s.Position = Ggpk.Record.Offset + (sizeof(long) + sizeof(int) + sizeof(long)); // 20
						s.Write(0L);
						Ggpk.FirstFreeRecord = null;
					}
				} else {
					s.Position = Previous.Offset + sizeof(long);
					s.Write(0L);
					Previous.Next = null;
				}
			} else if (Previous is null) {
				if (Ggpk.Record.FirstFreeRecordOffset == Offset) {
					s.Position = Ggpk.Record.Offset + (sizeof(long) + sizeof(int) + sizeof(long)); // 20
					s.Write(Next.Offset);
					Ggpk.FirstFreeRecord = Next;
				}
			} else {
				s.Position = Previous.Offset + sizeof(long);
				s.Write(Next.Offset);
				Previous.Next = Next;
			}
			Debug.Assert(Previous is null && Next is null);
		}
	}

	/// <summary>
	/// Update the link after the Offset of this FreeRecord is changed
	/// </summary>
	protected internal virtual void UpdateOffset() {
		var s = Ggpk.baseStream;
		lock (s) {
			if (Previous is null) { // first
				var old = Ggpk.FirstFreeRecord;
				s.Position = Ggpk.Record.Offset + (sizeof(long) * 2 + sizeof(int));
				s.Write(Offset);
				Ggpk.FirstFreeRecord = this;
				if (old == this || old is null)
					return;

				// new inserted
				var last = this;
				while (last.Next is not null)
					last = last.Next;
				s.Position = last.Offset + sizeof(long);
				s.Write(old.Offset);
				last.Next = old;
			} else { // except first
				s.Position = Previous.Offset + sizeof(long);
				s.Write(Offset);
				Previous.Next = this;
			}
		}
	}

	protected internal virtual void UpdateLength() {
		var list = Ggpk._SortedFreeRecords;
		if (list is not null) {
			var span = CollectionsMarshal.AsSpan(list);
			var i = span.BinarySearch(new LengthWrapper(Length));
			if (i < 0)
				i = ~i;
			else if (list[i] == this)
				return;

			// Move existing one
			var oi = list.IndexOf(this);
			if (oi != -1) {
				if (oi != i) {
					if (oi < i)
						span[(oi + 1)..i--].CopyTo(span[oi..]);
					else
						span[i..oi].CopyTo(span[(i + 1)..]);
					span[i] = this;
				}
				return;
			}

			list.Insert(i, this);
		}
	}

    internal readonly struct LengthWrapper(uint length) : IComparable<FreeRecord> {
		public readonly int CompareTo(FreeRecord? other) => other is null ? -1 : length.CompareTo(other.Length);
	}
}