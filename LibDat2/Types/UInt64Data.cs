using System.IO;
using static LibDat2.Types.IFieldData;

namespace LibDat2.Types {
	[FieldType(FieldType.UInt64)]
	public class UInt64Data : FieldDataBase<ulong> {
		public UInt64Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override void Read(BinaryReader reader) {
			Value = reader.ReadUInt64();
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override void FromString(string value) {
			Value = ulong.Parse(value.TrimEnd('U', 'L'));
		}

		/// <inheritdoc/>
		public override string ToString() {
			return Value.ToString() + "UL";
		}
	}
}