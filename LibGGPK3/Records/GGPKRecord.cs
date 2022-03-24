namespace LibGGPK3.Records {
	/// <summary>
	/// GGPK record is the very first record and exists at the very beginning of the GGPK file.
	/// </summary>
	public class GGPKRecord : BaseRecord {
		/// <summary>GGPK</summary>
		public const uint Tag = 0x4B504747;

		public uint GGPKVersion = 3; // 3 for PC, 4 for Mac, 2 for gmae-version before 3.11.2 which has no bundle in ggpk

		public long RootDirectoryOffset;
		public long FirstFreeRecordOffset;

		protected internal GGPKRecord(int length, GGPK ggpk) : base(length, ggpk) {
			Offset = ggpk.GGPKStream.Position - 8;
			GGPKVersion = (uint)ggpk.GGPKStream.ReadInt32(); // 3 for PC, 4 for Mac
			RootDirectoryOffset = ggpk.GGPKStream.ReadInt64();
			FirstFreeRecordOffset = ggpk.GGPKStream.ReadInt64();
		}

		protected internal override void WriteRecordData() {
			var s = Ggpk.GGPKStream;
			Offset = s.Position;
			s.Write(Length); // 28
			s.Write(Tag);
			s.Write(GGPKVersion); // 3 for PC, 4 for Mac
			s.Write(RootDirectoryOffset);
			s.Write(FirstFreeRecordOffset);
		}
	}
}