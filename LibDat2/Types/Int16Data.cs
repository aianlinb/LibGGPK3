using System.IO;
using static LibDat2.Types.IFieldData;

namespace LibDat2.Types {
	[FieldType(FieldType.Int16)]
	public class Int16Data : FieldDataBase<short> {
		public Int16Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override void Read(BinaryReader reader) {
			Value = reader.ReadInt16();
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override void FromString(string value) {
			Value = short.Parse(value);
		}
	}
}