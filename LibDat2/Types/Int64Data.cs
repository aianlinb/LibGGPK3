using System.IO;

namespace LibDat2.Types {
	public class Int64Data : FieldDataBase<long> {
		public Int64Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override Int64Data Read(BinaryReader reader) {
			Value = reader.ReadInt64();
			return this;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override Int64Data FromString(string value) {
			Value = long.Parse(value.TrimEnd('L'));
			return this;
		}

		/// <inheritdoc/>
		public override string ToString() {
			return Value.ToString() + "L";
		}
	}
}