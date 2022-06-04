using System.IO;

namespace LibDat2.Types {
	public class UInt32Data : FieldDataBase<uint> {
		public UInt32Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override UInt32Data Read(BinaryReader reader) {
			Value = reader.ReadUInt32();
			return this;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override UInt32Data FromString(string value) {
			Value = uint.Parse(value);
			return this;
		}
	}
}