using System;
using System.IO;

namespace LibDat2.Types {
	public class BooleanData : FieldDataBase<bool> {
		public BooleanData(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override BooleanData Read(BinaryReader reader) {
			var i = reader.ReadByte();
			Value = i switch {
				0 => false,
				1 => true,
				_ => throw new InvalidCastException($"Unable to cast {i} to Boolean"),
			};
			return this;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override BooleanData FromString(string value) {
			Value = bool.Parse(value);
			return this;
		}
	}
}