using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace LibDat2.Types {
	public interface IFieldData : INotifyPropertyChanged {
		/// <summary>
		/// Define the "size"/"pointer size" in bytes of <see cref="FieldType"/>
		/// </summary>
		[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
		public class FieldTypeAttribute : Attribute {
			public readonly FieldType Type;
			public FieldTypeAttribute(FieldType type) {
				Type = type;
			}
		}

		/// <summary>
		/// Data type of the field
		/// </summary>
		public enum FieldType {
			/// <summary>
			/// For array of unknown type
			/// </summary>
			Unknown,
			/// <summary>
			/// bool
			/// </summary>
			Boolean,
			/// <summary>
			/// sbyte
			/// </summary>
			Int8,
			/// <summary>
			/// short
			/// </summary>
			Int16,
			/// <summary>
			/// int
			/// </summary>
			Int32,
			/// <summary>
			/// long
			/// </summary>
			Int64,
			/// <summary>
			/// byte
			/// </summary>
			UInt8,
			/// <summary>
			/// ushort
			/// </summary>
			UInt16,
			/// <summary>
			/// uint
			/// </summary>
			UInt32,
			/// <summary>
			/// ulong
			/// </summary>
			UInt64,
			/// <summary>
			/// float
			/// </summary>
			Float32,
			/// <summary>
			/// double
			/// </summary>
			Float64,
			/// <summary>
			/// key type in old definition
			/// </summary>
			Row,
			/// <summary>
			/// foreignkey type in old definition
			/// </summary>
			ForeignRow,
			Array,
			/// <summary>
			/// string
			/// </summary>
			String,
			/// <summary>
			/// Value type of string as opposed to the reference type
			/// For Languague.dat
			/// </summary>
			ValueString
		}

		/// <summary>
		/// Data of this field
		/// </summary>
		public object Value { get; set; }

		/// <summary>
		/// <see cref="Value"/> in string representation.
		/// Equals to <see cref="ToString()"/> and <see cref="FromString(string)"/>
		/// </summary>
		public string StringValue { get; set; }

		/// <summary>
		/// Get the length in dat file of a type of field
		/// </summary>
		public static int SizeOfType(string type, bool x64) {
			if (type.StartsWith("array|"))
				return x64 ? 16 : 8;
			else
				return type switch {
					"foreignrow" => x64 ? 16 : 8,
					"row" => x64 ? 8 : 4,
					"string" => x64 ? 8 : 4,
					"bool" => 1,
					"i8" => 1,
					"u8" => 1,
					"i16" => 2,
					"u16" => 2,
					"i32" => 4,
					"enumrow" => 4,
					"u32" => 4,
					"f32" => 4,
					"i64" => 8,
					"u64" => 8,
					"f64" => 8,
					"valuestring" => 0,
					"valueString" => 0, // forward support
					"array" => 0,
					_ => throw new InvalidCastException($"Unknown Type: {type}")
				};
		}

		/// <summary>
		/// Get the length in dat file of a type of field
		/// </summary>
		public static int SizeOfType(FieldType type, bool x64) {
			return type switch {
				FieldType.Unknown => 0,
				FieldType.Boolean => 1,
				FieldType.Int8 => 1,
				FieldType.Int16 => 2,
				FieldType.Int32 => 4,
				FieldType.Int64 => 8,
				FieldType.UInt8 => 1,
				FieldType.UInt16 => 2,
				FieldType.UInt32 => 4,
				FieldType.UInt64 => 8,
				FieldType.Float32 => 4,
				FieldType.Float64 => 8,
				FieldType.Row => x64 ? 8 : 4,
				FieldType.ForeignRow => x64 ? 16 : 8,
				FieldType.Array => x64 ? 16 : 8,
				FieldType.String => x64 ? 8 : 4,
				FieldType.ValueString => -1,
				_ => throw new InvalidCastException($"Unknown Type: {type}")
			};
		}

		/// <summary>
		/// Convert type string from DatDefinitions to <see cref="FieldType"/> enum
		/// </summary>
		public static readonly ReadOnlyDictionary<string, FieldType> TypeFromString = new(new Dictionary<string, FieldType> {
			{ "bool", FieldType.Boolean  },
			{ "i8", FieldType.Int8  },
			{ "i16", FieldType.Int16 },
			{ "i32", FieldType.Int32 },
			{ "enumrow", FieldType.Int32 },
			{ "i64", FieldType.Int64 },
			{ "u8", FieldType.UInt8  },
			{ "u16", FieldType.UInt16 },
			{ "u32", FieldType.UInt32 },
			{ "u64", FieldType.UInt64 },
			{ "f32", FieldType.Float32 },
			{ "f64", FieldType.Float64 },
			{ "row", FieldType.Row },
			{ "foreignrow", FieldType.ForeignRow },
			{ "array", FieldType.Unknown },
			{ "string", FieldType.String },
			{ "valuestring", FieldType.ValueString },
			{ "valueString", FieldType.ValueString } // forward support
		});

		/// <summary>
		/// Read a <see cref="IFieldData"/> from a dat file.
		/// If <paramref name="type"/> is <see cref="FieldType.Array"/>, use <see cref="IArrayData.Read(BinaryReader, DatContainer, FieldType)"/> instead
		/// </summary>
		public static IFieldData Read(BinaryReader reader, FieldType type, DatContainer dat) {
			IFieldData? fd = type switch {
				FieldType.Boolean		=> new BooleanData(dat),
				FieldType.Int8			=> new Int8Data(dat),
				FieldType.Int16			=> new Int16Data(dat),
				FieldType.Int32			=> new Int32Data(dat),
				FieldType.Int64			=> new Int64Data(dat),
				FieldType.UInt8			=> new UInt8Data(dat),
				FieldType.UInt16		=> new UInt16Data(dat),
				FieldType.UInt32		=> new UInt32Data(dat),
				FieldType.UInt64		=> new UInt64Data(dat),
				FieldType.Float32		=> new Float32Data(dat),
				FieldType.Float64		=> new Float64Data(dat),
				FieldType.Row			=> new RowData(dat),
				FieldType.ForeignRow	=> new ForeignRowData(dat),
				FieldType.Array			=> throw new InvalidOperationException("IArrayData.Read should be called instead"),
				FieldType.String		=> StringData.Read(reader, dat),
				FieldType.ValueString	=> new ValueStringData(dat),
				_ => throw new InvalidCastException("Unknown Type: " + type)
			};
			if (fd is not IReferenceData)
				fd.Read(reader);
			return fd;
		}

		/// <summary>
		/// Create an instance of <see cref="IFieldData"/> from its value in string representation
		/// If <paramref name="type"/> is <see cref="FieldType.Array"/>, use <see cref="IArrayData.FromString(BinaryReader, DatContainer, FieldType)"/> instead
		/// </summary>
		public static IFieldData FromString(string value, FieldType type, DatContainer dat) {
			IFieldData fd = type switch {
				FieldType.Boolean		=> new BooleanData(dat),
				FieldType.Int8			=> new Int8Data(dat),
				FieldType.Int16			=> new Int16Data(dat),
				FieldType.Int32			=> new Int32Data(dat),
				FieldType.Int64			=> new Int64Data(dat),
				FieldType.UInt8			=> new UInt8Data(dat),
				FieldType.UInt16		=> new UInt16Data(dat),
				FieldType.UInt32		=> new UInt32Data(dat),
				FieldType.UInt64		=> new UInt64Data(dat),
				FieldType.Float32		=> new Float32Data(dat),
				FieldType.Float64		=> new Float64Data(dat),
				FieldType.Row			=> new RowData(dat),
				FieldType.ForeignRow	=> new ForeignRowData(dat),
				FieldType.Array			=> throw new InvalidOperationException("IFieldData.FromArrayString should be called instead"),
				FieldType.String		=> StringData.FromString(value, dat),
				FieldType.ValueString	=> new ValueStringData(dat),
				_ => throw new InvalidCastException("Unknown Type: " + type)
			};
			if (type != FieldType.String)
				fd.FromString(value);
			return fd;
		}

		/// <summary>
		/// Read the value from a dat file
		/// </summary>
		public void Read(BinaryReader reader);

		/// <summary>
		/// Write the value to a dat file
		/// </summary>
		public void Write(BinaryWriter writer);

		/// <summary>
		/// Read the value from its string representation
		/// </summary>
		public void FromString(string value);

		/// <summary>
		/// Get the string representation of the value
		/// </summary>
		public string ToString();
	}
}