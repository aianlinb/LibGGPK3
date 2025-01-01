using SystemExtensions.Streams;

namespace LibGGPK3.Records;
/// <summary>
/// GGPK record is the very first record and exists at the very beginning of the GGPK file.
/// </summary>
public class GGPKRecord : BaseRecord {
	/// <summary>GGPK</summary>
	public const int Tag = 0x4B504747;

	/// <summary>
	/// 3 for PC, 4 for Mac, 2 for gmae-version before 3.11.2 which has no bundle in ggpk.
	/// </summary>
	public uint GGPKVersion { get; }

	public long RootDirectoryOffset { get; protected internal set; }
	public long FirstFreeRecordOffset { get; protected internal set; }

	protected internal GGPKRecord(int length, GGPK ggpk) : base(length, ggpk) {
		Offset = ggpk.baseStream.Position - 8;
		GGPKVersion = (uint)ggpk.baseStream.Read<int>(); // 3 for PC, 4 for Mac
		RootDirectoryOffset = ggpk.baseStream.Read<long>();
		FirstFreeRecordOffset = ggpk.baseStream.Read<long>();
	}

	protected internal override void WriteRecordData() {
		var s = Ggpk.baseStream;
		Offset = s.Position;
		s.Write(Length); // 28
		s.Write(Tag);
		s.Write(GGPKVersion); // 3 for PC, 4 for Mac
		s.Write(RootDirectoryOffset);
		s.Write(FirstFreeRecordOffset);
	}
}