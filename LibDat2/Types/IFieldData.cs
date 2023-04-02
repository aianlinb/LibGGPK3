using System;
using System.ComponentModel;
using System.IO;

namespace LibDat2.Types {
	public interface IFieldData : INotifyPropertyChanged {
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
			if (type.StartsWith("pair|"))
				return SizeOfType(type[5..], x64) * 2;
			return type switch {
				"foreignrow"	=> x64 ? 16 : 8,
				"row"			=> x64 ? 8 : 4,
				"string"		=> x64 ? 8 : 4,
				"bool"			=> 1,
				"i8"			=> 1,
				"u8"			=> 1,
				"i16"			=> 2,
				"u16"			=> 2,
				"i32"			=> 4,
				"u32"			=> 4,
				"enumrow" => 4,
				"f32"			=> 4,
				"i64"			=> 8,
				"u64"			=> 8,
				"f64"			=> 8,
				"valuestring"	=> -1, // Shouldn't be used
				_ => throw new ArgumentException("Unknown Type: " + type, nameof(type))
			};
		}

		/// <summary>
		/// Read a <see cref="IFieldData"/> from a dat file.
		/// </summary>
		public static IFieldData Read(BinaryReader reader, string type, DatContainer dat) {
			if (type.StartsWith("array|"))
				return IArrayData.Read(reader, type[6..], dat);
			if (type.StartsWith("pair|"))
				return IPairData.Read(reader, type[5..], dat);
			IFieldData fd = type switch {
				"bool"			=> new BooleanData(dat),
				"i8"			=> new Int8Data(dat),
				"i16"			=> new Int16Data(dat),
				"i32"			=> new Int32Data(dat),
				"i64"			=> new Int64Data(dat),
				"u8"			=> new UInt8Data(dat),
				"u16"			=> new UInt16Data(dat),
				"u32"			=> new UInt32Data(dat),
				"enumrow"		=> new UInt32Data(dat),
				"u64"			=> new UInt64Data(dat),
				"f32"			=> new Float32Data(dat),
				"f64"			=> new Float64Data(dat),
				"row"			=> new RowData(dat),
				"foreignrow"	=> new ForeignRowData(dat),
				"string"		=> StringData.Read(reader, dat),
				"valuestring"	=> new ValueStringData(dat),
				_ => throw new InvalidCastException("Unknown Type: " + type)
			};
			if (type != "string")
				return fd.Read(reader);
			return fd;
		}

		/// <summary>
		/// Create an instance of <see cref="IFieldData"/> from its value in string representation
		/// </summary>
		public static IFieldData FromString(string value, string type, DatContainer dat) {
			if (type.StartsWith("array|"))
				return IArrayData.FromString(value, type[6..], dat);
			if (type.StartsWith("pair|"))
				return IPairData.FromString(value, type[5..], dat);
			IFieldData fd = type switch {
				"bool"			=> new BooleanData(dat),
				"i8"			=> new Int8Data(dat),
				"i16"			=> new Int16Data(dat),
				"i32"			=> new Int32Data(dat),
				"i64"			=> new Int64Data(dat),
				"u8"			=> new UInt8Data(dat),
				"u16"			=> new UInt16Data(dat),
				"u32"			=> new UInt32Data(dat),
				"enumrow"		=> new UInt32Data(dat),
				"u64"			=> new UInt64Data(dat),
				"f32"			=> new Float32Data(dat),
				"f64"			=> new Float64Data(dat),
				"row"			=> new RowData(dat),
				"foreignrow"	=> new ForeignRowData(dat),
				"string"		=> StringData.FromString(value, dat),
				"valuestring"	=> new ValueStringData(dat),
				_ => throw new InvalidCastException("Unknown Type: " + type)
			};
			if (type != "string")
				return fd.FromString(value);
			return fd;
		}

		/// <summary>
		/// Read the value from a dat file
		/// </summary>
		public IFieldData Read(BinaryReader reader);

		/// <summary>
		/// Write the value to a dat file
		/// </summary>
		public void Write(BinaryWriter writer);

		/// <summary>
		/// Read the value from its string representation
		/// </summary>
		public IFieldData FromString(string value);

		/// <summary>
		/// Get the string representation of the value
		/// </summary>
		public string ToString();
	}
}