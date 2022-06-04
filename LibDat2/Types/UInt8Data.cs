using System.IO;

namespace LibDat2.Types {
	public class UInt8Data : FieldDataBase<byte> {
		public UInt8Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override UInt8Data Read(BinaryReader reader) {
			Value = reader.ReadByte();
			return this;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override UInt8Data FromString(string value) {
			Value = byte.Parse(value);
			return this;
		}
	}
}