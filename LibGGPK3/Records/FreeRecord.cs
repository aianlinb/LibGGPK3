using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SystemExtensions;
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
			Debug.Assert(value is null || !value.IsInvalid);
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

	/// <summary>
	/// Whether this FreeRecord is removed from the ggpk and should not be used anymore.
	/// </summary>
	public bool IsInvalid => Length == 0U;

	protected internal FreeRecord(uint length, GGPK ggpk) : base(length, ggpk) {
		Offset = ggpk.baseStream.Position - 8;
		NextFreeOffset = ggpk.baseStream.Read<long>();
		ggpk.baseStream.Position = Offset + Length;
	}

	/// <summary>
	/// Also calls the <see cref="WriteRecordData"/>.
	/// Please calls <see cref="UpdateOffset"/> after this to add the FreeRecord to <see cref="GGPK.FreeRecords"/>
	/// </summary>
	protected internal FreeRecord(long offset, long nextFreeOffset, GGPK ggpk) : base(0U, ggpk) {
		// Length must manually be set by UpdateLength
		Offset = offset;
		NextFreeOffset = nextFreeOffset;
	}

	protected internal override void WriteRecordData() {
		ArgumentOutOfRangeException.ThrowIfLessThan(Length, 16U);
		var s = Ggpk.baseStream;
		Offset = s.Position;
		s.Write(Length);
		s.Write(Tag);
		s.Write(NextFreeOffset);
		s.Seek(Length - (sizeof(int) + sizeof(int) + sizeof(long)), SeekOrigin.Current);
	}

	internal int GetSortedIndex() {
		var length = Length;
		if (length == 0)
			return -1;

		var span = CollectionsMarshal.AsSpan(Ggpk.SortedFreeRecords);
		var i = span.BinarySearch(new LengthWrapper(length));
		if (i < 0)
			return i;

		var i2 = i;
		var result = span[i];
		do {
			if (result == this)
				return i;
		} while (++i != span.Length && (result = span[i]).Length == length);
		while (--i2 != -1 && (result = span[i2]).Length == length) {
			if (result == this)
				return i2;
		}
		return ~i;
	}

	/// <summary>
	/// Remove this FreeRecord from the Linked FreeRecord List of ggpk
	/// </summary>
	protected internal virtual void RemoveFromList() {
		if (IsInvalid)
			return;

		var s = Ggpk.baseStream;
		lock (s) {
			var list = Ggpk._SortedFreeRecords;
			// list?.Remove(this);
			if (list is not null) { // Remove it from the sorted list
				var i = GetSortedIndex();
				if (i >= 0)
					list.RemoveAt(i);
			}

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
			Length = 0; // Make it invalid
		}
	}

	/// <summary>
	/// Update the link after the Offset of this FreeRecord is changed
	/// </summary>
	protected internal virtual void UpdateOffset() {
		var s = Ggpk.baseStream;
		lock (s) {
			if (IsInvalid)
				ThrowHelper.Throw<InvalidOperationException>("The FreeRecord is invalid, it may have already been removed from the ggpk");
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

	protected internal virtual void UpdateLength(uint newLength) {
		if (Length == newLength)
			return;

		var list = Ggpk._SortedFreeRecords;
		if (list is not null) {
			// Fix the order in the sorted list with the new Length
			var span = CollectionsMarshal.AsSpan(list);
			var i = span.BinarySearch(new LengthWrapper(newLength));
			if (i < 0)
				i = ~i;

			if (Length != 0) {
				var oi = GetSortedIndex();
				if (oi >= 0) {
					if (newLength == 0U) // Becomes invalid
						list.RemoveAt(oi);
					else if (oi != i) {
						// Move the element at oi to the middle of i and (i - 1)
						if (oi < i)
							span[(oi + 1)..i--].CopyTo(span[oi..]); // 01234 -> 02134 (when oi = 1, i = 3)
						else
							span[i..oi].CopyTo(span[(i + 1)..]); // 01234 -> 03124 (when oi = 3, i = 1)
						span[i] = this;
					}
					goto done;
				}
				// Unable to find the old one
			}
			list.Insert(i, this);
		}
	done:
		Length = newLength;

		// Test
		FreeRecord? last = null;
		Debug.Assert(list is null || list.TrueForAll(r => {
			var result = last is null || r.Length >= last.Length;
			last = r;
			return result;
		})); //, string.Join('\n', list.Select(f => f.Length)));
	}
}