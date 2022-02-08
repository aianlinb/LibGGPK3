using System.IO;
using static LibDat2.Types.IFieldData;

namespace LibDat2.Types {
	[FieldType(FieldType.Int64)]
	public class Int64Data : FieldDataBase<long> {
		public Int64Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override void Read(BinaryReader reader) {
			Value = reader.ReadInt64();
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override void FromString(string value) {
			Value = long.Parse(value.TrimEnd('L'));
		}

		/// <inheritdoc/>
		public override string ToString() {
			return Value.ToString() + "L";
		}
	}
}