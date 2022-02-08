using System;
using System.IO;

namespace LibDat2.Types {
	[FieldType(FieldType.Array)]
	public interface IArrayData : IReferenceData {
		/// <summary>
		/// FieldType of value in array
		/// </summary>
		public FieldType TypeOfValue { get; }

		/// <summary>
		/// Read a <see cref="IArrayData"/> from a dat file
		/// </summary>
		public static new IArrayData Read(BinaryReader reader, FieldType typeOfValueInArray, DatContainer dat) {
			return typeOfValueInArray switch {
				FieldType.Boolean => ArrayData<bool>.Read(reader, dat, typeOfValueInArray),
				FieldType.Int8 => ArrayData<sbyte>.Read(reader, dat, typeOfValueInArray),
				FieldType.Int16 => ArrayData<short>.Read(reader, dat, typeOfValueInArray),
				FieldType.Int32 => ArrayData<int>.Read(reader, dat, typeOfValueInArray),
				FieldType.Int64 => ArrayData<long>.Read(reader, dat, typeOfValueInArray),
				FieldType.UInt8 => ArrayData<byte>.Read(reader, dat, typeOfValueInArray),
				FieldType.UInt16 => ArrayData<ushort>.Read(reader, dat, typeOfValueInArray),
				FieldType.UInt32 => ArrayData<uint>.Read(reader, dat, typeOfValueInArray),
				FieldType.UInt64 => ArrayData<ulong>.Read(reader, dat, typeOfValueInArray),
				FieldType.Float32 => ArrayData<float>.Read(reader, dat, typeOfValueInArray),
				FieldType.Float64 => ArrayData<double>.Read(reader, dat, typeOfValueInArray),
				FieldType.Row => ArrayData<RowData>.Read(reader, dat, typeOfValueInArray),
				FieldType.ForeignRow => ArrayData<ForeignRowData>.Read(reader, dat, typeOfValueInArray),
				FieldType.Array => throw new ArgumentException("Reading array of array is not implemented", nameof(typeOfValueInArray)),
				FieldType.String => ArrayData<StringData>.Read(reader, dat, typeOfValueInArray),
				FieldType.ValueString => ArrayData<ValueStringData>.Read(reader, dat, typeOfValueInArray),
				FieldType.Unknown => ArrayData<object>.Read(reader, dat, typeOfValueInArray),
				_ => throw new InvalidCastException("Unknown Type: " + typeOfValueInArray)
			};
		}

		/// <summary>
		/// Create an instance of <see cref="IFieldData"/> from its value in string representation
		/// </summary>
		public static new IArrayData FromString(string value, FieldType typeOfValueInArray, DatContainer dat) {
			return typeOfValueInArray switch {
				FieldType.Boolean => ArrayData<bool>.FromString(value, dat, typeOfValueInArray),
				FieldType.Int8 => ArrayData<sbyte>.FromString(value, dat, typeOfValueInArray),
				FieldType.Int16 => ArrayData<short>.FromString(value, dat, typeOfValueInArray),
				FieldType.Int32 => ArrayData<int>.FromString(value, dat, typeOfValueInArray),
				FieldType.Int64 => ArrayData<long>.FromString(value, dat, typeOfValueInArray),
				FieldType.UInt8 => ArrayData<byte>.FromString(value, dat, typeOfValueInArray),
				FieldType.UInt16 => ArrayData<ushort>.FromString(value, dat, typeOfValueInArray),
				FieldType.UInt32 => ArrayData<uint>.FromString(value, dat, typeOfValueInArray),
				FieldType.UInt64 => ArrayData<ulong>.FromString(value, dat, typeOfValueInArray),
				FieldType.Float32 => ArrayData<float>.FromString(value, dat, typeOfValueInArray),
				FieldType.Float64 => ArrayData<double>.FromString(value, dat, typeOfValueInArray),
				FieldType.Row => ArrayData<RowData>.FromString(value, dat, typeOfValueInArray),
				FieldType.ForeignRow => ArrayData<ForeignRowData>.FromString(value, dat, typeOfValueInArray),
				FieldType.Array => throw new InvalidOperationException("Modifying array of array is not implemented"),
				FieldType.String => ArrayData<StringData>.FromString(value, dat, typeOfValueInArray),
				FieldType.ValueString => ArrayData<ValueStringData>.FromString(value, dat, typeOfValueInArray),
				FieldType.Unknown => ArrayData<object>.FromString(value, dat, typeOfValueInArray),
				_ => throw new InvalidCastException("Unknown Type: " + typeOfValueInArray)
			};
		}
	}
}