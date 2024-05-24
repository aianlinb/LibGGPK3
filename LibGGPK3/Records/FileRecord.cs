using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

using SystemExtensions;
using SystemExtensions.Spans;
using SystemExtensions.Streams;

namespace LibGGPK3.Records {
	/// <summary>
	/// Record contains the data of a file.
	/// </summary>
	public class FileRecord : TreeNode {
		/// <summary>FILE</summary>
		public const int Tag = 0x454C4946;

		/// <summary>
		/// Offset in pack file where the raw data begins
		/// </summary>
		public long DataOffset { get; protected set; }
		/// <summary>
		/// Length of the raw file data
		/// </summary>
		public int DataLength { get; protected internal set; }

		[SkipLocalsInit]
		protected internal unsafe FileRecord(int length, GGPK ggpk) : base(length, ggpk) {
			var s = ggpk.baseStream;
			Offset = s.Position - 8;
			var nameLength = s.Read<int>() - 1;
			s.Read(out Hash);
			if (Ggpk.Record.GGPKVersion == 4) {
				Span<byte> b = stackalloc byte[nameLength * sizeof(int)];
				s.ReadExactly(b);
				Name = Encoding.UTF32.GetString(b);
				s.Seek(4, SeekOrigin.Current); // Null terminator
			} else {
				Name = s.ReadString(nameLength); // UTF16
				s.Seek(2, SeekOrigin.Current); // Null terminator
			}
			DataOffset = s.Position;
			DataLength = Length - (int)(DataOffset - Offset);
			s.Seek(DataLength, SeekOrigin.Current);
		}

		/// <summary>
		/// Internal Usage
		/// </summary>
		protected internal FileRecord(string name, GGPK ggpk) : base(default, ggpk) {
			Name = name;
			ThrowIfNameContainsSlash();
			Length = CaculateRecordLength();
		}

		protected override int CaculateRecordLength() {
			return 12 + LENGTH_OF_HASH + (Ggpk.Record.GGPKVersion == 4 ? sizeof(int) : sizeof(char)) * (Name.Length + 1) + DataLength; // (4 + 4 + 4 + Hash.Length + (Name + "\0").Length * sizeof(char/int)) + DataLength
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
			DataOffset = s.Position;
			// Actual file content writing of FileRecord isn't here (see Write())
		}

		/// <summary>
		/// Get the file content of this record
		/// </summary>
		public virtual byte[] Read() {
			var s = Ggpk.baseStream;
			lock (s) {
				s.Position = DataOffset;
				var buffer = GC.AllocateUninitializedArray<byte>(DataLength);
				s.ReadExactly(buffer, 0, DataLength);
				return buffer;
			}
		}

		/// <summary>
		/// Get a part of the file content of this record
		/// </summary>
		public virtual byte[] Read(Range range) {
			var (offset, length) = range.GetOffsetAndLength(DataLength);
			var s = Ggpk.baseStream;
			lock (s) {
				s.Position = DataOffset + offset;
				var buffer = GC.AllocateUninitializedArray<byte>(length);
				s.ReadExactly(buffer, 0, length);
				return buffer;
			}
		}

		/// <summary>
		/// Replace the file content with <paramref name="newContent"/>,
		/// and move this record to a <see cref="FreeRecord"/> with most suitable size, or end of file if not found.
		/// </summary>
		public virtual void Write(ReadOnlySpan<byte> newContent) {
			if (!SHA256.TryHashData(newContent, Hash.AsSpan(), out _))
				ThrowHelper.Throw<UnreachableException>("Unable to compute hash of the content"); // _Hash.Length < LENGTH_OF_HASH
			var s = Ggpk.baseStream;
			lock (s) {
				if (newContent.Length != DataLength) { // Replace a FreeRecord
					DataLength = newContent.Length;
					WriteWithNewLength();
					// Offset and DataOffset will be set by WriteRecordData() in above line
				} else {
					s.Position = Offset + sizeof(int) * 3;
					s.Write(Hash);
				}
				s.Position = DataOffset;
				s.Write(newContent);

				// Performance reduced when replacing in batches
				//if (Parent != Ggpk.Root)
				//	Parent?.RenewHash();
			}
		}
	}
}