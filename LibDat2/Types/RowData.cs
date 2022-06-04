using System;
using System.IO;
using System.Text.RegularExpressions;

namespace LibDat2.Types {
	public class RowData : FieldDataBase<RowData> {
		public const uint Nullx32Key = 0xFEFEFEFE;
		public const ulong Nullx64Key = 0xFEFEFEFEFEFEFEFE;

		public ulong? Key;

		public RowData(DatContainer dat) : base(dat) {
			Value = this;
		}

		/// <inheritdoc/>
		public override RowData Read(BinaryReader reader) {
			if (Dat.x64) {
				Key = reader.ReadUInt64();
				if (Key == Nullx64Key)
					Key = null;
			} else {
				Key = reader.ReadUInt32();
				if (Key == Nullx32Key)
					Key = null;
			}
			return this;
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			if (Dat.x64)
				writer.Write(Key ?? Nullx64Key);
			else
				writer.Write((uint?)Key ?? Nullx32Key);
		}

		/// <inheritdoc/>
		public override RowData FromString(string value) {
			var m = RowRegex.Match(value);
			if (m.Success) {
				if (m.Groups[1].Value.ToLower() == "null")
					Key = null;
				else if (ulong.TryParse(m.Groups[1].Value, out var l))
					if (Dat.x64 && l == Nullx64Key || !Dat.x64 && l == Nullx32Key)
						Key = null;
					else
						Key = l;
				else
					throw new InvalidCastException("Unable to convert " + value + " to RowData");
			} else
				throw new InvalidCastException("Unable to convert " + value + " to RowData");
			return Value;
		}
		public static readonly Regex RowRegex = new(@"^\s*<\s*(.+?)\s*>\s*$");

		/// <inheritdoc/>
		public override string ToString() {
			return $"<{Key?.ToString() ?? "null"}>";
		}
	}
}
