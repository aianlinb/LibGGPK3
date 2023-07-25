using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static LibDat2.Types.IFieldData;

namespace LibDat2.Types {
#pragma warning disable CS8612
	public class ArrayData<TypeOfValueInArray> : ReferenceDataBase<TypeOfValueInArray[]>, IArrayData where TypeOfValueInArray : notnull {
		/// <inheritdoc/>
		public virtual string TypeOfValue { get; }

		public ArrayData(DatContainer dat, string typeOfValue) : base(dat) {
			TypeOfValue = typeOfValue;
		}

		/// <summary>
		/// Read a <see cref="ArrayData{TypeOfValueInArray}"/> from a dat file
		/// </summary>
		public static ArrayData<TypeOfValueInArray> Read(BinaryReader reader, DatContainer dat, string typeOfarrayInArray) {
			long length;
			long offset;
			if (dat.x64) {
				length = reader.ReadInt64();
				offset = reader.ReadInt64();
			} else {
				length = reader.ReadInt32();
				offset = reader.ReadInt32();
			}

			if (length == 0)
				return new(dat, typeOfarrayInArray) {
					Value = Array.Empty<TypeOfValueInArray>(),
					Offset = offset,
					Length = (int)length
				};

			if (dat.ReferenceDatas.TryGetValue(offset, out IReferenceData? rd) && rd is ArrayData<TypeOfValueInArray> a)
				return a;

			reader.BaseStream.Seek(dat.x64 ? -16 : -8, SeekOrigin.Current);
			return new ArrayData<TypeOfValueInArray>(dat, typeOfarrayInArray).Read(reader);
		}

		/// <summary>
		/// Read the pointer and call <see cref="ReadInDataSection"/>.
		/// This won't check the <see cref="DatContainer.ReferenceDatas"/>, use <see cref="Read(BinaryReader, DatContainer, string)"/> instead.
		/// </summary>
		public override ArrayData<TypeOfValueInArray> Read(BinaryReader reader) {
			if (Value != default)
				Dat.ReferenceDataOffsets.Remove(ToString());
			if (Dat.x64)
				Value = new TypeOfValueInArray[reader.ReadInt64()];
			else
				Value = new TypeOfValueInArray[reader.ReadInt32()];
			base.Read(reader);
			return this;
		}

		/// <inheritdoc/>
		protected override unsafe void ReadInDataSection(BinaryReader reader) {
			switch (TypeOfValue) {
				case "bool":
					fixed (bool* b = (object)Value as bool[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length));
					break;
				case "i8":
					fixed (sbyte* b = (object)Value as sbyte[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length));
					break;
				case "i16":
					fixed (short* b = (object)Value as short[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(short)));
					break;
				case "i32":
					fixed (int* b = (object)Value as int[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(int)));
					break;
				case "i64":
					fixed (long* b = (object)Value as long[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(long)));
					break;
				case "u8":
					reader.BaseStream.Read((object)Value as byte[]);
					break;
				case "u16":
					fixed (ushort* b = (object)Value as ushort[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(ushort)));
					break;
				case "u32":
					fixed (uint* b = (object)Value as uint[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(uint)));
					break;
				case "u64":
					fixed (ulong* b = (object)Value as ulong[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(ulong)));
					break;
				case "f32":
					fixed (float* b = (object)Value as float[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(float)));
					break;
				case "f64":
					fixed (double* b = (object)Value as double[])
						reader.BaseStream.Read(new Span<byte>(b, Value.Length * sizeof(double)));
					break;
				default:
					for (var i = 0L; i < Value.Length; ++i)
						Value[i] = (TypeOfValueInArray)IFieldData.Read(reader, TypeOfValue, Dat);
					break;
			}
		}

		/// <inheritdoc/>
		public override void Write(BinaryWriter writer) {
			if (Value.Length == 0) {
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
				case "bool":
					fixed (bool* b = Value as bool[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length));
					break;
				case "i8":
					fixed (sbyte* b = Value as sbyte[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length));
					break;
				case "i16":
					fixed (short* b = Value as short[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(short)));
					break;
				case "i32":
					fixed (int* b = Value as int[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(int)));
					break;
				case "i64":
					fixed (long* b = Value as long[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(long)));
					break;
				case "u8":
					writer.BaseStream.Write(Value as byte[]);
					break;
				case "u16":
					fixed (ushort* b = Value as ushort[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(ushort)));
					break;
				case "u32":
					fixed (uint* b =Value as uint[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(uint)));
					break;
				case "u64":
					fixed (ulong* b = Value as ulong[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(ulong)));
					break;
				case "f32":
					fixed (float* b = Value as float[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(float)));
					break;
				case "f64":
					fixed (double* b = Value as double[])
						writer.BaseStream.Write(new ReadOnlySpan<byte>(b, Value.Length * sizeof(double)));
					break;
				default:
#pragma warning disable IDE0220
					foreach (IFieldData fd in Value)
						fd.Write(writer);
					break;
#pragma warning restore IDE0220
			}
		}

		/// <summary>
		/// Read a <see cref="ArrayData{TypeOfValueInArray}"/> from its value in string representation
		/// </summary>
		public static ArrayData<TypeOfValueInArray> FromString(string value, DatContainer dat, string typeOfValue) {
			if (!typeOfValue.EndsWith("string"))
				value = Regex.Replace(value, @"\s", "").Replace(",", ", ");
			if (dat.ReferenceDataOffsets.TryGetValue(value, out long offset) && dat.ReferenceDatas.TryGetValue(offset, out IReferenceData? rd) && rd is ArrayData<TypeOfValueInArray> a)
				return a;
			return new ArrayData<TypeOfValueInArray>(dat, typeOfValue).FromString(value);
		}

		IReferenceData IReferenceData.FromString(string value) => FromString(value);

		/// <inheritdoc/>
		public override ArrayData<TypeOfValueInArray> FromString(string value) {
			TypeOfValueInArray[] newValue;

			if (!TypeOfValue.EndsWith("string"))
				value = Regex.Replace(value.Trim(), @"\s", "");
			if (!value.StartsWith('[') || !value.EndsWith(']'))
				throw new InvalidCastException("\"" + value + "\" cannot be converted to an array");

			if (value == "[]")
				newValue = Array.Empty<TypeOfValueInArray>();
			else if (TypeOfValue == "foreignrow") {
				value = value[1..^1]; // Trim '[' ']'
				if (!value.StartsWith('<') || !value.EndsWith('>'))
					throw new InvalidCastException("\"[" + value + "]\" cannot be converted to an array of ForeignRowData");
				var sarray = value[1..^1].Split(">,<"); // Trim '<' '>'
				newValue = new TypeOfValueInArray[sarray.Length];
				for (var n = 0; n < sarray.Length; ++n) {
					var d = new ForeignRowData(Dat).FromString("<" + sarray[n] + ">");
					newValue[n] = (TypeOfValueInArray)(object)d;
				}
			} else if (TypeOfValue.StartsWith("pair|")) {
				value = value[1..^1]; // Trim '[' ']'
				if (!value.StartsWith('(') || !value.EndsWith(')'))
					throw new InvalidCastException("\"[" + value + "]\" cannot be converted to an array of PairData");
				var sarray = value[1..^1].Split("),("); // Trim '(' ')'
				newValue = new TypeOfValueInArray[sarray.Length];
				for (var n = 0; n < sarray.Length; ++n) {
					var t = IPairData.FromString("(" + sarray[n] + ")", TypeOfValue[5..], Dat);
					newValue[n] = (TypeOfValueInArray)(object)t;
				}
			} else if (TypeOfValue.StartsWith("array|")) {
				value = value[1..^1]; // Trim '[' ']'
				if (!value.StartsWith('[') || !value.EndsWith(']'))
					throw new InvalidCastException("\"[" + value + "]\" cannot be converted to an array of PairData");
				var sarray = value[1..^1].Split("],["); // Trim '[' ']'
				newValue = new TypeOfValueInArray[sarray.Length];
				for (var n = 0; n < sarray.Length; ++n) {
					var t = IArrayData.FromString("[" + sarray[n] + "]", TypeOfValue[5..], Dat);
					newValue[n] = (TypeOfValueInArray)(object)t;
				}
			} else {
				var sarray = value[1..^1].Split(','); // Trim '[' ']'
				newValue = new TypeOfValueInArray[sarray.Length];
				if (TypeOfValue.EndsWith("string"))
					for (var n = 0; n < sarray.Length; ++n)
						newValue[n] = (TypeOfValueInArray)IFieldData.FromString(sarray[n].Trim('"', ' '), TypeOfValue, Dat);
				else
					for (var n = 0; n < sarray.Length; ++n)
						newValue[n] = (TypeOfValueInArray)IFieldData.FromString(sarray[n], TypeOfValue, Dat).Value;
			}

			if (Value != default)
				Dat.ReferenceDataOffsets.Remove(ToString());
			Value = newValue;
			Length = CalculateLength();
			if (Offset == default) {
				Offset = Dat.CurrentOffset;
				Dat.CurrentOffset += Length;
				Dat.ReferenceDatas[Offset] = this;
			}
			if (Length != 0)
				Dat.ReferenceDataOffsets[ToString()] = Offset;
			return this;
		}

		/// <inheritdoc/>
		public override string ToString() {
			if (Value == null)
				return "{null}";
			var s = new StringBuilder("[");

			if (TypeOfValue.EndsWith("string"))
				foreach (var f in Value) {
					s.Append(f == null ? "{null}" : $"\"{f}\"");
					s.Append(", ");
				}
			else
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
			if (TypeOfValue == "valuestring")
				return Dat.UTF32
					? Value.Sum(s => Encoding.UTF32.GetByteCount((s as ValueStringData)!.Value)) + Value.Length * 4
					: Value.Sum(s => (s as ValueStringData)!.Value.Length) * 2;
			return Value.Length * SizeOfType(TypeOfValue, Dat.x64);
		}
	}
}