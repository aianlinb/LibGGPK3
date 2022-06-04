using System;
using System.IO;

namespace LibDat2.Types {
	public interface IPairData : IFieldData {
		/// <summary>
		/// FieldType of value in array
		/// </summary>
		public string TypeOfValue { get; }

		/// <summary>
		/// Read a <see cref="IArrayData"/> from a dat file
		/// </summary>
		public static new IPairData Read(BinaryReader reader, string typeOfValue, DatContainer dat) {
			if (typeOfValue.StartsWith("array|"))
				return new PairData<IArrayData>(dat, typeOfValue).Read(reader);
			if (typeOfValue.StartsWith("pair|"))
				return new PairData<IPairData>(dat, typeOfValue).Read(reader);
			return typeOfValue switch {
				"bool"			=> new PairData<bool>(dat, typeOfValue).Read(reader),
				"i8"			=> new PairData<sbyte>(dat, typeOfValue).Read(reader),
				"i16"			=> new PairData<short>(dat, typeOfValue).Read(reader),
				"i32"			=> new PairData<int>(dat, typeOfValue).Read(reader),
				"i64"			=> new PairData<long>(dat, typeOfValue).Read(reader),
				"u8"			=> new PairData<byte>(dat, typeOfValue).Read(reader),
				"u16"			=> new PairData<ushort>(dat, typeOfValue).Read(reader),
				"u32"			=> new PairData<uint>(dat, typeOfValue).Read(reader),
				"u64"			=> new PairData<ulong>(dat, typeOfValue).Read(reader),
				"f32"			=> new PairData<float>(dat, typeOfValue).Read(reader),
				"f64"			=> new PairData<double>(dat, typeOfValue).Read(reader),
				"row"			=> new PairData<RowData>(dat, typeOfValue).Read(reader),
				"foreignrow"	=> new PairData<ForeignRowData>(dat, typeOfValue).Read(reader),
				"string"		=> new PairData<StringData>(dat, typeOfValue).Read(reader),
				"valuestring"	=> new PairData<ValueStringData>(dat, typeOfValue).Read(reader),
				_ => throw new InvalidCastException("Unknown Type: " + typeOfValue)
			};
		}

		/// <summary>
		/// Create an instance of <see cref="IFieldData"/> from its value in string representation
		/// </summary>
		public static new IPairData FromString(string value, string typeOfValue, DatContainer dat) {
			if (typeOfValue.StartsWith("array|"))
				return new PairData<IArrayData>(dat, typeOfValue).FromString(value);
			if (typeOfValue.StartsWith("pair|"))
				return new PairData<IPairData>(dat, typeOfValue).FromString(value);
			return typeOfValue switch {
				"bool"			=> new PairData<bool>(dat, typeOfValue).FromString(value),
				"i8"			=> new PairData<sbyte>(dat, typeOfValue).FromString(value),
				"i16"			=> new PairData<short>(dat, typeOfValue).FromString(value),
				"i32"			=> new PairData<int>(dat, typeOfValue).FromString(value),
				"i64"			=> new PairData<long>(dat, typeOfValue).FromString(value),
				"u8"			=> new PairData<byte>(dat, typeOfValue).FromString(value),
				"u16"			=> new PairData<ushort>(dat, typeOfValue).FromString(value),
				"u32"			=> new PairData<uint>(dat, typeOfValue).FromString(value),
				"u64"			=> new PairData<ulong>(dat, typeOfValue).FromString(value),
				"f32"			=> new PairData<float>(dat, typeOfValue).FromString(value),
				"f64"			=> new PairData<double>(dat, typeOfValue).FromString(value),
				"row"			=> new PairData<RowData>(dat, typeOfValue).FromString(value),
				"foreignrow"	=> new PairData<ForeignRowData>(dat, typeOfValue).FromString(value),
				"string"		=> new PairData<StringData>(dat, typeOfValue).FromString(value),
				"valuestring"	=> new PairData<ValueStringData>(dat, typeOfValue).FromString(value),
				_ => throw new InvalidCastException("Unknown Type: " + typeOfValue)
			};
		}
	}
}