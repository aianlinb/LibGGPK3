using System;
using System.IO;
using System.Security.Cryptography;

namespace LibGGPK3.Records {
	/// <summary>
	/// Record contains the data of a file.
	/// </summary>
	public class FileRecord : TreeNode {
		public static readonly byte[] Tag = new byte[] { (byte)'F', (byte)'I', (byte)'L', (byte)'E' };
		public static readonly SHA256 Hash256 = SHA256.Create();

		/// <summary>
		/// Offset in pack file where the raw data begins
		/// </summary>
		public long DataOffset;
		/// <summary>
		/// Length of the raw file data
		/// </summary>
		public int DataLength;

		protected unsafe internal FileRecord(int length, GGPK ggpk) : base(length, ggpk) {
			var s = ggpk.FileStream;
			Offset = s.Position - 8;
			var nameLength = s.ReadInt32();
			s.Read(Hash, 0, 32);

			var name = new char[nameLength - 1];
			fixed (char* p = name)
				s.Read(new(p, name.Length * 2));

			Name = new(name);
			s.Seek(2, SeekOrigin.Current); // Null terminator

			DataOffset = s.Position;
			DataLength = Length - (int)(s.Position - Offset);
			s.Seek(DataLength, SeekOrigin.Current);
		}

		protected internal FileRecord(string name, GGPK ggpk) : base(default, ggpk) {
			Name = name;
			Length = CaculateLength();
		}

		public override int CaculateLength() {
			return Name.Length * 2 + 46 + DataLength; // (4 + 4 + 4 + Hash.Length + (Name + "\0").Length * 2) + DataLength
		}

		protected internal unsafe override void WriteRecordData() {
			var s = Ggpk.FileStream;
			Offset = s.Position;
			s.Write(Length);
			s.Write(Tag);
			s.Write(Name.Length + 1);
			s.Write(Hash);
			fixed (char* p = Name)
				s.Write(new(p, Name.Length * 2));

			s.Write((short)0); // Null terminator
			DataOffset = s.Position;
			// Actual file content writing of FileRecord isn't here
		}

		/// <summary>
		/// Get the file content of this record
		/// </summary>
		/// <param name="ggpkStream">Stream of GGPK file</param>
		public virtual byte[] ReadFileContent() {
			var buffer = new byte[DataLength];
			var s = Ggpk.FileStream;
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
			var s = Ggpk.FileStream;

			if (!Hash256.TryComputeHash(NewContent, Hash, out _))
				throw new("Unable to compute hash of the content");

			if (NewContent.Length != DataLength) { // Replace a FreeRecord
				DataLength = NewContent.Length;
				MoveWithNewLength(CaculateLength());
				// Offset and DataOffset will be set from Write() in above method
			}
			s.Seek(DataOffset, SeekOrigin.Begin);
			s.Write(NewContent);
			s.Flush();
		}
	}
}