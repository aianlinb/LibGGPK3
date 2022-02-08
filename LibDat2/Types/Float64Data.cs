using System.IO;
using static LibDat2.Types.IFieldData;

namespace LibDat2.Types {
	[FieldType(FieldType.Float64)]
	public class Float64Data : FieldDataBase<double> {
		public Float64Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override void Read(BinaryReader reader) {
			Value = reader.ReadDouble();
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override void FromString(string value) {
			Value = double.Parse(value.TrimEnd('D'));
		}

		/// <inheritdoc/>
		public override string ToString() {
			return Value.ToString() + "d";
		}
	}
}