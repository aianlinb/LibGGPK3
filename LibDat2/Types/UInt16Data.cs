using System.IO;
using static LibDat2.Types.IFieldData;

namespace LibDat2.Types {
	[FieldType(FieldType.UInt16)]
	public class UInt16Data : FieldDataBase<ushort> {
		public UInt16Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override void Read(BinaryReader reader) {
			Value = reader.ReadUInt16();
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override void FromString(string value) {
			Value = ushort.Parse(value);
		}
	}
}