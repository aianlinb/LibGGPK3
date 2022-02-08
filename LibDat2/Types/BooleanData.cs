using System;
using System.IO;
using static LibDat2.Types.IFieldData;

namespace LibDat2.Types {
	[FieldType(FieldType.Boolean)]
	public class BooleanData : FieldDataBase<bool> {
		public BooleanData(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override void Read(BinaryReader reader) {
			var i = reader.ReadByte();
			Value = i switch {
				0 => false,
				1 => true,
				_ => throw new InvalidCastException($"Unable to cast {i} to Boolean"),
			};
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override void FromString(string value) {
			Value = bool.Parse(value);
		}
	}
}