using System.IO;

namespace LibDat2.Types {
	public class Int16Data : FieldDataBase<short> {
		public Int16Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override Int16Data Read(BinaryReader reader) {
			Value = reader.ReadInt16();
			return this;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override Int16Data FromString(string value) {
			Value = short.Parse(value);
			return this;
		}
	}
}