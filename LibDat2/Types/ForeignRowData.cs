using System;
using System.IO;
using System.Text.RegularExpressions;

namespace LibDat2.Types {
	public class ForeignRowData : FieldDataBase<ForeignRowData> {
		public const uint Nullx32Key = 0xFEFEFEFE;
		public const ulong Nullx64Key = 0xFEFEFEFEFEFEFEFE;

		public ulong? Key1;
		public ulong? Key2;

		public ForeignRowData(DatContainer dat) : base(dat) {
			Value = this;
		}

		/// <inheritdoc/>
		public override ForeignRowData Read(BinaryReader reader) {
			if (Dat.x64) {
				Key1 = reader.ReadUInt64();
				if (Key1 == Nullx64Key)
					Key1 = null;
				Key2 = reader.ReadUInt64();
				if (Key2 == Nullx64Key)
					Key2 = null;
			} else {
				Key1 = reader.ReadUInt32();
				if (Key1 == Nullx32Key)
					Key1 = null;
				Key2 = reader.ReadUInt32();
				if (Key2 == Nullx32Key)
					Key2 = null;
			}
			return Value;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			if (Dat.x64) {
				writer.Write(Key1 ?? Nullx64Key);
				writer.Write(Key2 ?? Nullx64Key);
			} else {
				writer.Write((uint?)Key1 ?? Nullx32Key);
				writer.Write((uint?)Key2 ?? Nullx32Key);
			}
		}

		/// <inheritdoc/>
		public override ForeignRowData FromString(string value) {
			var m = ForeignRowRegex.Match(value);
			if (m.Success) {
				if (m.Groups[1].Value.ToLower() == "null")
					Key1 = null;
				else if (ulong.TryParse(m.Groups[1].Value, out var l)) {
					if (Dat.x64 && l == Nullx64Key || !Dat.x64 && l == Nullx32Key)
						Key1 = null;
					else
						Key1 = l;
				} else
					throw new InvalidCastException("Unable to convert " + value + " to ForeignRowData");
				if (m.Groups[2].Value.ToLower() == "null")
					Key2 = null;
				else if (ulong.TryParse(m.Groups[2].Value, out var l)) {
					if (Dat.x64 && l == Nullx64Key || !Dat.x64 && l == Nullx32Key)
						Key2 = null;
					else
						Key2 = l;
				} else
					throw new InvalidCastException("Unable to convert " + value + " to ForeignRowData");
			} else
				throw new InvalidCastException("Unable to convert " + value + " to ForeignRowData");
			return Value;
		}
		public static readonly Regex ForeignRowRegex = new(@"^\s*<\s*(.+?)\s*,\s*(.+?)\s*>\s*$");

		/// <inheritdoc/>
		public override string ToString() {
			return $"<{Key1?.ToString() ?? "null"}, { Key2?.ToString() ?? "null"}>";
		}
	}
}
