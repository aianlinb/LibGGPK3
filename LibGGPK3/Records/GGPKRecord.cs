namespace LibGGPK3.Records {
	/// <summary>
	/// GGPK record is the very first record and exists at the very beginning of the GGPK file.
	/// It must have excatly 2 entries - One goes to the root directory and the other to a FREE record.
	/// </summary>
	public class GGPKRecord : BaseRecord {
		/// <summary>GGPK</summary>
		public const uint Tag = 0x4B504747;

		public uint GGPKVersion = 3; // since POE 3.11.2

		public long RootDirectoryOffset;
		public long FirstFreeRecordOffset;

		protected internal GGPKRecord(int length, GGPK ggpk) : base(length, ggpk) {
			Offset = ggpk.GGPKStream.Position - 8;
			GGPKVersion = (uint)ggpk.GGPKStream.ReadInt32(); // 3
			RootDirectoryOffset = ggpk.GGPKStream.ReadInt64();
			FirstFreeRecordOffset = ggpk.GGPKStream.ReadInt64();
		}

		protected internal override void WriteRecordData() {
			var s = Ggpk.GGPKStream;
			Offset = s.Position;
			s.Write(Length); // 28
			s.Write(Tag);
			s.Write(GGPKVersion); // 3
			s.Write(RootDirectoryOffset);
			s.Write(FirstFreeRecordOffset);
		}
	}
}