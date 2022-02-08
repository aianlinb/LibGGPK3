using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static LibDat2.Types.IFieldData;

namespace LibDat2.Types {
	[FieldType(FieldType.Array)]
#pragma warning disable CS8612
	public class ArrayData<TypeOfValueInArray> : ReferenceDataBase<TypeOfValueInArray[]>, IArrayData {
		/// <inheritdoc/>
		public virtual FieldType TypeOfValue { get; }
		public ArrayData(DatContainer dat, FieldType typeOfValue) : base(dat) {
			TypeOfValue = typeOfValue;
		}

		/// <summary>
		/// Read a <see cref="ArrayData{TypeOfValueInArray}"/> from a dat file
		/// </summary>
		public static ArrayData<TypeOfValueInArray> Read(BinaryReader reader, DatContainer dat, FieldType typeOfarrayInArray) {
			long length;
			long offset;
			if (dat.x64) {
				length = reader.ReadInt64();
				offset = reader.ReadInt64();
			} else {
				length = reader.ReadInt32();
				offset = reader.ReadInt32();
			}

			if (typeOfarrayInArray == FieldType.Unknown || length == 0)
				return new(dat, typeOfarrayInArray) {
					Value = Array.Empty<TypeOfValueInArray>(),
					Offset = offset,
					Length = (int)length
				};

			if (dat.ReferenceDatas.TryGetValue(offset, out IReferenceData? rd) && rd is ArrayData<TypeOfValueInArray> a)
				return a;

			reader.BaseStream.Seek(dat.x64 ? -16 : -8, SeekOrigin.Current);
			var ad = new ArrayData<TypeOfValueInArray>(dat, typeOfarrayInArray);
			ad.Read(reader);

			return ad;
		}

		/// <summary>
		/// Read the pointer and call <see cref="ReadInDataSection"/>.
		/// This won't check the <see cref="DatContainer.ReferenceDatas"/>, use <see cref="Read(BinaryReader, DatContainer, FieldType)"/> instead.
		/// </summary>
		public override void Read(BinaryReader reader) {
			if (Value != default)
				Dat.ReferenceDataOffsets.Remove(ToString());
			if (Dat.x64)
				Value = new TypeOfValueInArray[reader.ReadInt64()];
			else
				Value = new TypeOfValueInArray[reader.ReadInt32()];
			base.Read(reader);
		}

		/// <inheritdoc/>
		protected override unsafe void ReadInDataSection(BinaryReader reader) {
			switch (TypeOfValue) {
				case FieldType.Boolean:
					fixed (bool* b = (object)Value as bool[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length));
					break;
				case FieldType.Int8:
					fixed (sbyte* b = (object)Value as sbyte[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length));
					break;
				case FieldType.Int16:
					fixed (short* b = (object)Value as short[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(short)));
					break;
				case FieldType.Int32:
					fixed (int* b = (object)Value as int[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(int)));
					break;
				case FieldType.Int64:
					fixed (long* b = (object)Value as long[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(long)));
					break;
				case FieldType.UInt8:
					reader.BaseStream.Read((object)Value as byte[]);
					break;
				case FieldType.UInt16:
					fixed (ushort* b = (object)Value as ushort[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(ushort)));
					break;
				case FieldType.UInt32:
					fixed (uint* b = (object)Value as uint[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(uint)));
					break;
				case FieldType.UInt64:
					fixed (ulong* b = (object)Value as ulong[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(ulong)));
					break;
				case FieldType.Float32:
					fixed (float* b = (object)Value as float[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(float)));
					break;
				case FieldType.Float64:
					fixed (double* b = (object)Value as double[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(double)));
					break;
				case FieldType.Row:
				case FieldType.ForeignRow:
				case FieldType.Array:
				case FieldType.String:
				case FieldType.ValueString:
					for (var i = 0L; i < Value.Length; ++i)
						Value[i] = (TypeOfValueInArray)IFieldData.Read(reader, TypeOfValue, Dat);
					break;
				default:
					throw new InvalidCastException("Unknown Type: " + TypeOfValue);
			}
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			if (TypeOfValue == FieldType.Unknown || Value.Length == 0) {
				Length = 0;
				Offset = Dat.CurrentOffset;
				if (Dat.x64) {
					writer.Write(0L);
					writer.Write(Offset);
				} else {
					writer.Write(0);
					writer.Write((int)Offset);
				}
				return;
			}

			if (Dat.x64)
				writer.Write((long)Value.Length);
			else
				writer.Write(Value.Length);
			base.Write(writer);
		}

		/// <inheritdoc/>
		protected override unsafe void WriteInDataSection(BinaryWriter writer) {
			switch (TypeOfValue) {
				case FieldType.Boolean:
					fixed (bool* b = Value as bool[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length));
					break;
				case FieldType.Int8:
					fixed (sbyte* b = Value as sbyte[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b,Value.Length));
					break;
				case FieldType.Int16:
					fixed (short* b = Value as short[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(short)));
					break;
				case FieldType.Int32:
					fixed (int* b = Value as int[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(int)));
					break;
				case FieldType.Int64:
					fixed (long* b = Value as long[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(long)));
					break;
				case FieldType.UInt8:
					writer.BaseStream.Write(Value as byte[]);
					break;
				case FieldType.UInt16:
					fixed (ushort* b = Value as ushort[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(ushort)));
					break;
				case FieldType.UInt32:
					fixed (uint* b =Value as uint[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(uint)));
					break;
				case FieldType.UInt64:
					fixed (ulong* b = Value as ulong[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(ulong)));
					break;
				case FieldType.Float32:
					fixed (float* b = Value as float[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(float)));
					break;
				case FieldType.Float64:
					fixed (double* b = Value as double[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(double)));
					break;
				case FieldType.Row:
#pragma warning disable CS8602
					foreach (var r in Value as RowData[])
						r.Write(writer);
					break;
				case FieldType.ForeignRow:
					foreach (var fr in Value as ForeignRowData[])
						fr.Write(writer);
					break;
				case FieldType.Array:
					foreach (IArrayData? a in Value)
						a.Write(writer);
					break;
				case FieldType.String:
					foreach (var s in Value as StringData[])
						s.Write(writer);
					break;
				case FieldType.ValueString:
					foreach (var s in Value as ValueStringData[])
						s.Write(writer);
					break;
				default:
					throw new InvalidCastException("Unknown Type: " + TypeOfValue);
			}
		}

		/// <summary>
		/// Read a <see cref="ArrayData{TypeOfValueInArray}"/> from its value in string representation
		/// </summary>
		public static ArrayData<TypeOfValueInArray> FromString(string value, DatContainer dat, FieldType typeOfarrayInArray) {
			value = typeOfarrayInArray == FieldType.String || typeOfarrayInArray == FieldType.ValueString ? value : Regex.Replace(value, @"\s", "").Replace(",", ", ");
			if (dat.ReferenceDataOffsets.TryGetValue(value, out long offset) && dat.ReferenceDatas.TryGetValue(offset, out IReferenceData? rd) && rd is ArrayData<TypeOfValueInArray> a)
				return a;

			var ad = new ArrayData<TypeOfValueInArray>(dat, typeOfarrayInArray);
			ad.FromString(value);
			return ad;
		}

		/// <inheritdoc/>
		public override void FromString(string value) {
			if (Value != default)
				Dat.ReferenceDataOffsets.Remove(ToString());

			var value2 = TypeOfValue == FieldType.String || TypeOfValue == FieldType.ValueString ? value.Trim(' ') : Regex.Replace(value, @"\s", "");
			if (!value2.StartsWith('[') || !value2.EndsWith(']'))
				throw new InvalidCastException("\"" + value + "\" cannot be converted to an array");
			if (TypeOfValue == FieldType.Unknown || value2 == "[]")
				Value = Array.Empty<TypeOfValueInArray>();
			else if (TypeOfValue == FieldType.ForeignRow) {
				value2 = value2[1..^1]; // Trim '[' ']'
				if (!value2.StartsWith('<') || !value2.EndsWith('>'))
					throw new InvalidCastException("\"" + value + "\" cannot be converted to an array of ForeignKeyType(foreignrow)");
				var sarray = value2[1..^1].Split(">,<"); // Trim '<' '>'
				Value = new TypeOfValueInArray[sarray.Length];
				for (var n = 0; n < sarray.Length; ++n) {
					var d = new ForeignRowData(Dat);
					d.FromString("<" + sarray[n] + ">");
					Value[n] = (TypeOfValueInArray)(object)d;
				}
			} else {
				var sarray = value2[1..^1].Split(','); // Trim '[' ']'
				Value = new TypeOfValueInArray[sarray.Length];
				switch (TypeOfValue) {
					case FieldType.String:
					case FieldType.ValueString:
						for (var n = 0; n < sarray.Length; ++n)
							Value[n] = (TypeOfValueInArray)IFieldData.FromString(sarray[n], TypeOfValue, Dat);
						break;
					default:
						for (var n = 0; n < sarray.Length; ++n)
							Value[n] = (TypeOfValueInArray)IFieldData.FromString(sarray[n], TypeOfValue, Dat).Value;
						break;
				}
			}

			Length = CalculateLength();
			if (Offset == default) {
				Offset = Dat.CurrentOffset;
				Dat.CurrentOffset += Length;
				Dat.ReferenceDatas[Offset] = this;
			}
			Dat.ReferenceDataOffsets[value] = Offset;
		}

		/// <inheritdoc/>
		public override string ToString() {
			if (Value == null)
				return "{null}";
			var s = new StringBuilder("[");
			foreach (var f in Value) {
				s.Append(f?.ToString() ?? "{null}");
				if (Value is uint or ulong)
					s.Append('U');
				if (Value is long or ulong)
					s.Append('L');
				else if (Value is float)
					s.Append('F');
				else if (Value is double)
					s.Append('D');
				s.Append(", ");
			}
			if (s.Length > 2)
				s.Remove(s.Length - 2, 2);
			s.Append(']');
			return s.ToString();
		}

		/// <inheritdoc/>
		public override int CalculateLength() {
			if (TypeOfValue == FieldType.ValueString)
				return Dat.UTF32
					? Value.Sum((s) => Encoding.UTF32.GetByteCount((s as ValueStringData).Value)) + Value.Length * 4
					: Value.Sum((s) => (s as ValueStringData).Value.Length) * 2;
			return Value.Length * IFieldData.SizeOfType(TypeOfValue, Dat.x64);
		}
	}
}