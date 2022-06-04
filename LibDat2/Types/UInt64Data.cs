using System.IO;

namespace LibDat2.Types {
	public class UInt64Data : FieldDataBase<ulong> {
		public UInt64Data(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public override UInt64Data Read(BinaryReader reader) {
			Value = reader.ReadUInt64();
			return this;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			writer.Write(Value);
		}

		/// <inheritdoc/>
		public override UInt64Data FromString(string value) {
			Value = ulong.Parse(value.TrimEnd('U', 'L'));
			return this;
		}

		/// <inheritdoc/>
		public override string ToString() {
			return Value.ToString() + "UL";
		}
	}
}