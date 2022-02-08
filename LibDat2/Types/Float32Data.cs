using System.IO;
using static LibDat2.Types.IFieldData;

namespace LibDat2.Types {
	[FieldType(FieldType.Float32)]
	public class Float32Data : FieldDataBase<float> {
		public Float32Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override void Read(BinaryReader reader) {
			Value = reader.ReadSingle();
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override void FromString(string value) {
			Value = float.Parse(value.TrimEnd('F'));
		}

		/// <inheritdoc/>
		public override string ToString() {
			return Value.ToString() + "f";
		}
	}
}