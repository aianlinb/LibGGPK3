using System.IO;

namespace LibDat2.Types {
	public class Int32Data : FieldDataBase<int> {
		public Int32Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override Int32Data Read(BinaryReader reader) {
			Value = reader.ReadInt32();
			return this;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override Int32Data FromString(string value) {
			Value = int.Parse(value);
			return this;
		}
	}
}