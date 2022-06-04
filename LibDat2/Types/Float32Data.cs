using System.IO;

namespace LibDat2.Types {
	public class Float32Data : FieldDataBase<float> {
		public Float32Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override Float32Data Read(BinaryReader reader) {
			Value = reader.ReadSingle();
			return this;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override Float32Data FromString(string value) {
			Value = float.Parse(value.TrimEnd('F'));
			return this;
		}

		/// <inheritdoc/>
		public override string ToString() {
			return Value.ToString() + "f";
		}
	}
}