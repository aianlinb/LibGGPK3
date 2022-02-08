namespace LibGGPK3.Records {
	/// <summary>
	/// GGPK record is the very first record and exists at the very beginning of the GGPK file.
	/// It must have excatly 2 entries - One goes to the root directory and the other to a FREE record.
	/// </summary>
	public class GGPKRecord : BaseRecord {
		public static readonly byte[] Tag = new byte[] { (byte)'G', (byte)'G', (byte)'P', (byte)'K' };

		public uint GGPKVersion = 3; // since POE 3.11.2

		public long RootDirectoryOffset;
		public long FirstFreeRecordOffset;

		protected internal GGPKRecord(int length, GGPK ggpk) : base(length, ggpk) {
			Offset = ggpk.FileStream.Position - 8;
			GGPKVersion = (uint)ggpk.FileStream.ReadInt32(); // 3
			RootDirectoryOffset = ggpk.FileStream.ReadInt64();
			FirstFreeRecordOffset = ggpk.FileStream.ReadInt64();
		}

		protected internal override void WriteRecordData() {
			var s = Ggpk.FileStream;
			Offset = s.Position;
			s.Write(Length); // 28
			s.Write(Tag);
			s.Write(GGPKVersion); // 3
			s.Write(RootDirectoryOffset);
			s.Write(FirstFreeRecordOffset);
		}
	}
}