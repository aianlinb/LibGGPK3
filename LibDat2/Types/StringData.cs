using System;
using System.IO;
using System.Text;

namespace LibDat2.Types {
	public class StringData : ReferenceDataBase<string> {
		public StringData(DatContainer dat) : base(dat) { }

		/// <summary>
		/// Read a <see cref="StringData"/> from a dat file
		/// </summary>
		public static StringData Read(BinaryReader reader, DatContainer dat) {
			var offset = dat.x64 ? reader.ReadInt64() : reader.ReadInt32();
			if (dat.ReferenceDatas.TryGetValue(offset, out IReferenceData? rd) && rd is StringData s)
				return s;
			reader.BaseStream.Seek(dat.x64 ? -8 : -4, SeekOrigin.Current);
			return new StringData(dat).Read(reader);
		}
		
		public override StringData Read(BinaryReader reader) {
			if (Value != default)
				Dat.ReferenceDataOffsets.Remove(ToString());
			base.Read(reader);
			return this;
		}

		/// <inheritdoc/>
		protected override void ReadInDataSection(BinaryReader reader) {
			var s = new ValueStringData(Dat);
			s.Read(reader);
			Value = s.Value;
		}

		/// <inheritdoc/>
		protected unsafe override void WriteInDataSection(BinaryWriter writer) {
			if (Value != null)
				if (Dat.UTF32)
					writer.Write(Encoding.UTF32.GetBytes(Value));
				else
					fixed (char* c = Value) // string is stored in UTF-16 in C#, so we can directly get its bytes for writing
						writer.BaseStream.Write(new ReadOnlySpan<byte>(c, Value.Length * 2));
			writer.Write(0); // \0 at the end of string
		}

		/// <summary>
		/// Read a <see cref="StringData"/> from its value in string representation
		/// </summary>
		public static StringData FromString(string value, DatContainer dat) {
			if (dat.ReferenceDataOffsets.TryGetValue(value, out long offset) && dat.ReferenceDatas.TryGetValue(offset, out IReferenceData? rd) && rd is StringData s)
				return s;
			return new StringData(dat).FromString(value);
		}

		/// <summary>
		/// Read the <see cref="IFieldData.Value"/> from its string representation
		/// This won't check the <see cref="DatContainer.ReferenceDatas"/>, use <see cref="FromString(string, DatContainer)"/> instead.
		/// </summary>
		public override StringData FromString(string value) {
			if (Value != default)
				Dat.ReferenceDataOffsets.Remove(ToString());

			Value = value;
			Length = CalculateLength();
			if (Offset == default) {
				Offset = Dat.CurrentOffset;
				Dat.CurrentOffset += Length;
				Dat.ReferenceDatas[Offset] = this;
			}
			Dat.ReferenceDataOffsets[value] = Offset;
			return this;
		}

		/// <inheritdoc/>
		public override int CalculateLength() {
			return (Dat.UTF32 ? Encoding.UTF32.GetByteCount(Value) : (Value.Length * 2)) + 4;
		}
	}
}