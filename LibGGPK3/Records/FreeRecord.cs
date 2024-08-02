using System;
using System.Collections.Generic;
using System.IO;

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
	public long NextFreeOffset { get; protected internal set; }

	protected internal FreeRecord(int length, GGPK ggpk) : base(length, ggpk) {
		Offset = ggpk.baseStream.Position - 8;
		NextFreeOffset = ggpk.baseStream.Read<long>();
		ggpk.baseStream.Position = Offset + Length;
	}

	/// <summary>
	/// Also calls the <see cref="WriteRecordData"/>.
	/// Please calls <see cref="UpdateOffset"/> after this to add the FreeRecord to <see cref="GGPK.FreeRecordList"/>
	/// </summary>
	protected internal FreeRecord(long offset, int length, long nextFreeOffset, GGPK ggpk) : base(length, ggpk) {
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

	/// <summary>
	/// Remove this FreeRecord from the Linked FreeRecord List
	/// </summary>
	/// <param name="node">Node in <see cref="GGPK.FreeRecordList"/> to remove</param>
	protected internal virtual void RemoveFromList(LinkedListNode<FreeRecord>? node = null) {
		var s = Ggpk.baseStream;
		lock (s) {
			node ??= Ggpk.FreeRecordList.Find(this);
			if (node is null)
				return;
			var previous = node.Previous?.Value;
			var next = node.Next?.Value;
			if (next is null)
				if (previous is null) {
					Ggpk.Record.FirstFreeRecordOffset = 0;
					s.Position = Ggpk.Record.Offset + (sizeof(long) * 2 + sizeof(int));
					s.Write((long)0);
				} else {
					previous.NextFreeOffset = 0;
					s.Position = previous.Offset + sizeof(long);
					s.Write((long)0);
				}
			else if (previous is null) {
				Ggpk.Record.FirstFreeRecordOffset = next.Offset;
				s.Position = Ggpk.Record.Offset + (sizeof(long) * 2 + sizeof(int));
				s.Write(next.Offset);
			} else {
				previous.NextFreeOffset = next.Offset;
				s.Position = previous.Offset + sizeof(long);
				s.Write(next.Offset);
			}
			Ggpk.FreeRecordList.Remove(node);
		}
	}

	/// <summary>
	/// Update the link after the Offset of this FreeRecord is changed
	/// </summary>
	/// <param name="node">Node in <see cref="GGPK.FreeRecordList"/> of this record</param>
	protected internal virtual LinkedListNode<FreeRecord> UpdateOffset(LinkedListNode<FreeRecord>? node = null) {
		if (node is not null) {
			if (node.Value != this)
				ThrowHelper.Throw<ArgumentException>("The provided node doesn't belong to this record", nameof(node));
		} else
			node = Ggpk.FreeRecordList.Find(this);

		var s = Ggpk.baseStream;
		lock (s) {
			var previousFree = node is null ? Ggpk.FreeRecordList.Last?.Value : node.Previous?.Value;
			if (previousFree is null) { // empty
				Ggpk.Record.FirstFreeRecordOffset = Offset;
				s.Position = Ggpk.Record.Offset + (sizeof(long) * 2 + sizeof(int));
				s.Write(Offset);
			} else {
				previousFree.NextFreeOffset = Offset;
				s.Position = previousFree.Offset + sizeof(long);
				s.Write(Offset);
			}
			return node ?? Ggpk.FreeRecordList.AddLast(this);
		}
	}
}