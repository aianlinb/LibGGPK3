using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using SystemExtensions;
using SystemExtensions.Spans;
using SystemExtensions.Streams;

namespace LibGGPK3.Records;
/// <summary>
/// Record contains the data of a file.
/// </summary>
public class FileRecord : TreeNode {
	/// <summary>FILE</summary>
	public const int Tag = 0x454C4946;

	/// <summary>
	/// Offset in pack file where the raw data begins
	/// </summary>
	public long DataOffset => Offset + (Length - DataLength);
	/// <summary>
	/// Length of the raw file data
	/// </summary>
	public int DataLength { get; protected internal set; }

	[SkipLocalsInit]
	protected internal unsafe FileRecord(int length, GGPK ggpk) : base(length, ggpk) {
		var s = ggpk.baseStream;
		Offset = s.Position - 8;
		var nameLength = s.Read<int>() - 1;
		s.Read(out _Hash);
		if (Ggpk.Record.GGPKVersion == 4) {
			Span<byte> b = stackalloc byte[nameLength * sizeof(int)];
			s.ReadExactly(b);
			Name = Encoding.UTF32.GetString(b);
			s.Seek(sizeof(int), SeekOrigin.Current); // Null terminator
		} else {
			Name = s.ReadString(nameLength); // UTF16
			s.Seek(sizeof(char), SeekOrigin.Current); // Null terminator
		}
		DataLength = Length - (int)(s.Position - Offset);
		s.Seek(DataLength, SeekOrigin.Current);
	}

	/// <summary>
	/// Internal Usage
	/// </summary>
	protected internal FileRecord(string name, GGPK ggpk) : base(default, ggpk) {
		Name = name;
		ThrowIfNameEmptyOrContainsSlash();
		Length = CaculateRecordLength();
	}

	protected override int CaculateRecordLength() {
		return 12 + SIZE_OF_HASH + (Ggpk.Record.GGPKVersion == 4 ? sizeof(int) : sizeof(char)) * (Name.Length + 1) + DataLength; // (4 + 4 + 4 + Hash.Length + (Name + "\0").Length * sizeof(char/int)) + DataLength
	}

	[SkipLocalsInit]
	protected internal override unsafe void WriteRecordData() {
		var s = Ggpk.baseStream;
		Offset = s.Position;
		s.Write(Length);
		s.Write(Tag);
		s.Write(Name.Length + 1);
		s.Write(Hash);
		if (Ggpk.Record.GGPKVersion == 4) {
			Span<byte> span = stackalloc byte[Name.Length * sizeof(int)];
			s.Write(span[..Encoding.UTF32.GetBytes(Name, span)]);
			s.Seek(sizeof(int), SeekOrigin.Current); // Null terminator
		} else {
			s.Write(Name);
			s.Seek(sizeof(char), SeekOrigin.Current); // Null terminator
		}
		// Actual file content writing of FileRecord isn't here (see Write())
	}

	/// <summary>
	/// Get the file content of this record.
	/// </summary>
	/// <remarks>Use <see cref="Read(Span{byte})"/> instead to avoid memory allocation.</remarks>
	public byte[] Read() {
		var buffer = GC.AllocateUninitializedArray<byte>(DataLength);
		Read(buffer);
		return buffer;
	}
	/// <summary>
	/// Get a part of the file content of this record.
	/// </summary>
	/// <remarks>Use <see cref="Read(Span{byte}, Range)"/> instead to avoid memory allocation.</remarks>
	public byte[] Read(Range range) {
		var buffer = GC.AllocateUninitializedArray<byte>(DataLength);
		Read(buffer, range);
		return buffer;
	}
	/// <summary>
	/// Get the file content of this record.
	/// </summary>
	/// <param name="span">The span to write the content to, the <see cref="Span{T}.Length"/> must be at least <see cref="DataLength"/></param>
	public virtual void Read(Span<byte> span) {
		var s = Ggpk.baseStream;
		lock (s) {
			s.Position = DataOffset;
			s.ReadExactly(span[..DataLength]);
		}
	}
	/// <summary>
	/// Get a part of the file content of this record.
	/// </summary>
	/// <param name="span">The span to write the content to, the <see cref="Span{T}.Length"/> must be at least <see cref="DataLength"/></param>
	public virtual void Read(Span<byte> span, Range range) {
		var (offset, length) = range.GetOffsetAndLength(DataLength);
		var s = Ggpk.baseStream;
		lock (s) {
			s.Position = DataOffset + offset;
			s.ReadExactly(span[..length]);
		}
	}

	/// <summary>
	/// Replace the file content with <paramref name="newContent"/>,
	/// and move this record to a <see cref="FreeRecord"/> with most suitable size, or end of file if not found.
	/// </summary>
	public virtual void Write(ReadOnlySpan<byte> newContent) {
		SHA256.HashData(newContent, _Hash.AsSpan());
		var s = Ggpk.baseStream;
		lock (s) {
			if (newContent.Length != DataLength) { // Replace a FreeRecord
				var diff = newContent.Length - DataLength;
				DataLength = newContent.Length;
				WriteWithNewLength(Length + diff);
				// Offset and DataOffset will be set by WriteRecordData() in above line
			} else {
				s.Position = Offset + sizeof(int) * 3;
				s.Write(Hash);
			}
			s.Position = DataOffset;
			s.Write(newContent);
			Ggpk.dirtyHashes.Add(Parent!);
		}
	}
}