using System.IO;

namespace LibDat2.Types {
	public class UInt16Data : FieldDataBase<ushort> {
		public UInt16Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override UInt16Data Read(BinaryReader reader) {
			Value = reader.ReadUInt16();
			return this;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override UInt16Data FromString(string value) {
			Value = ushort.Parse(value);
			return this;
		}
	}
}