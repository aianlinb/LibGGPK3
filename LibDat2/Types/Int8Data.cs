using System.IO;

namespace LibDat2.Types {
	public class Int8Data : FieldDataBase<sbyte> {
		public Int8Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override Int8Data Read(BinaryReader reader) {
			Value = reader.ReadSByte();
			return this;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override Int8Data FromString(string value) {
			Value = sbyte.Parse(value);
			return this;
		}
	}
}