using System;
using System.IO;

namespace LibDat2.Types {
	public interface IArrayData : IReferenceData {
		/// <summary>
		/// FieldType of value in array
		/// </summary>
		public string TypeOfValue { get; }

		/// <summary>
		/// Read a <see cref="IArrayData"/> from a dat file
		/// </summary>
		public static new IArrayData Read(BinaryReader reader, string typeOfValue, DatContainer dat) {
			if (typeOfValue.StartsWith("array|"))
				return ArrayData<IArrayData>.Read(reader, dat, typeOfValue);
			if (typeOfValue.StartsWith("pair|"))
				return ArrayData<IPairData>.Read(reader, dat, typeOfValue);
			return typeOfValue switch {
				"bool"			=> ArrayData<bool>.Read(reader, dat, typeOfValue),
				"i8"			=> ArrayData<sbyte>.Read(reader, dat, typeOfValue),
				"i16"			=> ArrayData<short>.Read(reader, dat, typeOfValue),
				"i32"			=> ArrayData<int>.Read(reader, dat, typeOfValue),
				"i64"			=> ArrayData<long>.Read(reader, dat, typeOfValue),
				"u8"			=> ArrayData<byte>.Read(reader, dat, typeOfValue),
				"u16"			=> ArrayData<ushort>.Read(reader, dat, typeOfValue),
				"u32"			=> ArrayData<uint>.Read(reader, dat, typeOfValue),
				"u64"			=> ArrayData<ulong>.Read(reader, dat, typeOfValue),
				"f32"			=> ArrayData<float>.Read(reader, dat, typeOfValue),
				"f64"			=> ArrayData<double>.Read(reader, dat, typeOfValue),
				"row"			=> ArrayData<RowData>.Read(reader, dat, typeOfValue),
				"foreignrow"	=> ArrayData<ForeignRowData>.Read(reader, dat, typeOfValue),
				"string"		=> ArrayData<StringData>.Read(reader, dat, typeOfValue),
				"valuestring"	=> ArrayData<ValueStringData>.Read(reader, dat, typeOfValue),
				_ => throw new InvalidCastException("Unknown Type: " + typeOfValue)
			};
		}

		/// <summary>
		/// Create an instance of <see cref="IFieldData"/> from its value in string representation
		/// </summary>
		public static new IArrayData FromString(string value, string typeOfValue, DatContainer dat) {
			if (typeOfValue.StartsWith("array|"))
				return ArrayData<IArrayData>.FromString(value, dat, typeOfValue);
			if (typeOfValue.StartsWith("pair|"))
				return ArrayData<IPairData>.FromString(value, dat, typeOfValue);
			return typeOfValue switch {
				"bool"			=> ArrayData<bool>.FromString(value, dat, typeOfValue),
				"i8"			=> ArrayData<sbyte>.FromString(value, dat, typeOfValue),
				"i16"			=> ArrayData<short>.FromString(value, dat, typeOfValue),
				"i32"			=> ArrayData<int>.FromString(value, dat, typeOfValue),
				"i64"			=> ArrayData<long>.FromString(value, dat, typeOfValue),
				"u8"			=> ArrayData<byte>.FromString(value, dat, typeOfValue),
				"u16"			=> ArrayData<ushort>.FromString(value, dat, typeOfValue),
				"u32"			=> ArrayData<uint>.FromString(value, dat, typeOfValue),
				"u64"			=> ArrayData<ulong>.FromString(value, dat, typeOfValue),
				"f32"			=> ArrayData<float>.FromString(value, dat, typeOfValue),
				"f64"			=> ArrayData<double>.FromString(value, dat, typeOfValue),
				"row"			=> ArrayData<RowData>.FromString(value, dat, typeOfValue),
				"foreignrow"	=> ArrayData<ForeignRowData>.FromString(value, dat, typeOfValue),
				"string"		=> ArrayData<StringData>.FromString(value, dat, typeOfValue),
				"valuestring"	=> ArrayData<ValueStringData>.FromString(value, dat, typeOfValue),
				_ => throw new InvalidCastException("Unknown Type: " + typeOfValue)
			};
		}
	}
}