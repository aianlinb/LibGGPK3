using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LibGGPK3.Records {
	/// <summary>
	/// Record contains the data of a file.
	/// </summary>
	public class FileRecord : TreeNode {
		/// <summary>FILE</summary>
		public const uint Tag = 0x454C4946;
		public static readonly SHA256 Hash256 = SHA256.Create();

		/// <summary>
		/// Offset in pack file where the raw data begins
		/// </summary>
		public long DataOffset { get; protected set; }
		/// <summary>
		/// Length of the raw file data
		/// </summary>
		public int DataLength { get; protected internal set; }

		protected unsafe internal FileRecord(int length, GGPK ggpk) : base(length, ggpk) {
			var s = ggpk.GGPKStream;
			Offset = s.Position - 8;
			var nameLength = s.ReadInt32() - 1;
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
			DataOffset = s.Position;
			DataLength = Length - (int)(s.Position - Offset);
			s.Seek(DataLength, SeekOrigin.Current);
		}

		protected internal FileRecord(string name, GGPK ggpk) : base(default, ggpk) {
			Name = name;
			Length = CaculateRecordLength();
		}

		protected override int CaculateRecordLength() {
			return (Name.Length + 1) * (Ggpk.GgpkRecord.GGPKVersion == 4 ? 4 : 2) + 44 + DataLength; // (4 + 4 + 4 + Hash.Length + (Name + "\0").Length * 2) + DataLength
		}

		protected internal unsafe override void WriteRecordData() {
			var s = Ggpk.GGPKStream;
			Offset = s.Position;
			s.Write(Length);
			s.Write(Tag);
			s.Write(Name.Length + 1);
			s.Write(Hash);
			if (Ggpk.GgpkRecord.GGPKVersion == 4) {
				s.Write(Encoding.UTF32.GetBytes(Name));
				s.Write(0); // Null terminator
			} else {
				fixed (char* p = Name)
					s.Write(new(p, Name.Length * 2));
				s.Write((short)0); // Null terminator
			}
			DataOffset = s.Position;
			// Actual file content writing of FileRecord isn't here
		}

		/// <summary>
		/// Get the file content of this record
		/// </summary>
		public virtual byte[] ReadFileContent() {
			var buffer = new byte[DataLength];
			var s = Ggpk.GGPKStream;
			s.Flush();
			s.Seek(DataOffset, SeekOrigin.Begin);
			for (var l = 0; l < DataLength;)
				l += s.Read(buffer, l, DataLength - l);
			return buffer;
		}

		/// <summary>
		/// Replace the file content with a new content,
		/// and move the record to the FreeRecord with most suitable size.
		/// </summary>
		public virtual void ReplaceContent(ReadOnlySpan<byte> NewContent) {
			var s = Ggpk.GGPKStream;
			if (!Hash256.TryComputeHash(NewContent, _Hash, out _))
				throw new("Unable to compute hash of the content");
			if (NewContent.Length != DataLength) { // Replace a FreeRecord
				DataLength = NewContent.Length;
				MoveWithNewLength(CaculateRecordLength());
				// Offset and DataOffset will be set from Write() in above method
			}
			s.Seek(DataOffset, SeekOrigin.Begin);
			s.Write(NewContent);
			s.Flush();
		}
	}
}