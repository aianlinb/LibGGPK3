using System.IO;

namespace LibDat2.Types {
	public class Float64Data : FieldDataBase<double> {
		public Float64Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override Float64Data Read(BinaryReader reader) {
			Value = reader.ReadDouble();
			return this;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override Float64Data FromString(string value) {
			Value = double.Parse(value.TrimEnd('D'));
			return this;
		}

		/// <inheritdoc/>
		public override string ToString() {
			return Value.ToString() + "d";
		}
	}
}