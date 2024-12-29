using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using System.Text;
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
			s.Write(0); // Null terminator
		} else {
			s.Write(Name);
			s.Write<short>(0); // Null terminator
		}
		// Actual file content writing of FileRecord isn't here (see Write())
	}

	/// <summary>
	/// Get the file content of this record.
	/// </summary>
	/// <remarks>Use <see cref="Read(Span{byte}, int)"/> instead to avoid memory allocation.</remarks>
	public byte[] Read() {
		var buffer = GC.AllocateUninitializedArray<byte>(DataLength);
		Read(buffer);
		return buffer;
	}
	/// <summary>
	/// Get a part of the file content of this record.
	/// </summary>
	/// <remarks>Use <see cref="Read(Span{byte}, int)"/> instead to avoid memory allocation.</remarks>
	public byte[] Read(Range range) {
		var (offset, length) = range.GetOffsetAndLength(DataLength);
		var buffer = GC.AllocateUninitializedArray<byte>(length);
		Read(buffer, offset);
		return buffer;
	}
	/// <summary>
	/// Get a part of the file content starting from <paramref name="offset"/>.
	/// </summary>
	/// <remarks>If the <paramref name="span"/> is too small, the result will be truncated.</remarks>
	public virtual void Read(Span<byte> span, int offset = 0) {
		if ((uint)offset > (uint)DataLength)
			ThrowHelper.ThrowArgumentOutOfRange(offset);
		var len = DataLength - offset;
		if (span.Length > len)
			span = span[..len];
		var s = Ggpk.baseStream;
		lock (s) {
			s.Position = DataOffset + offset;
			s.ReadExactly(span);
		}
	}

	/// <summary>
	/// Replace the file content with <paramref name="newContent"/>,
	/// and move this record to a <see cref="FreeRecord"/> with most suitable size, or end of file if not found.
	/// </summary>
	/// <param name="hash">
	/// The SHA-256 hash of the <paramref name="newContent"/>,
	/// or <see langword="null"/> to calculate it with <see cref="SHA256.HashData(ReadOnlySpan{byte}, Span{byte})"/>.
	/// </param>
	public virtual void Write(ReadOnlySpan<byte> newContent, Vector256<byte>? hash = null) {
		if (hash.HasValue)
			_Hash = hash.Value;
		else
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
				s.Write(_Hash);
			}
			s.Position = DataOffset;
			s.Write(newContent);
			Ggpk.dirtyHashes.Add(Parent!);
		}
	}

	/// <summary>
	/// Write <paramref name="data"/> to the file content starting from <paramref name="offset"/>.
	/// The <paramref name="offset"/> + <paramref name="data"/>.Length must be less than or equal to the <see cref="DataLength"/>.
	/// </summary>
	/// <param name="hash">
	/// The SHA-256 hash of the final file content after writing the <paramref name="data"/>,
	/// or <see langword="null"/> to calculate it with <see cref="SHA256.HashData(ReadOnlySpan{byte}, Span{byte})"/>.
	/// </param>
	public virtual void Write(ReadOnlySpan<byte> data, int offset, Vector256<byte>? hash = null) {
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		var end = checked(offset + data.Length);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(end, DataLength, "offset + data.Length");

		var s = Ggpk.baseStream;
		lock (s) {
			if (hash.HasValue)
				_Hash = hash.Value;
			else {
				var content = ArrayPool<byte>.Shared.Rent(DataLength);
				var span = new Span<byte>(content, 0, DataLength);
				try {
					s.Position = DataOffset;
					s.ReadExactly(span[..offset]);
					s.Seek(data.Length, SeekOrigin.Current);
					s.ReadExactly(span[end..]);
					SHA256.HashData(span, _Hash.AsSpan());
				} finally {
					ArrayPool<byte>.Shared.Return(content);
				}
			}
			s.Position = Offset + sizeof(int) * 3;
			s.Write(_Hash);

			s.Position = DataOffset + offset;
			s.Write(data);
			Ggpk.dirtyHashes.Add(Parent!);
		}
	}
}