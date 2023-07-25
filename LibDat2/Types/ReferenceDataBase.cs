using System.IO;

namespace LibDat2.Types {
#pragma warning disable CS8612
	public abstract class ReferenceDataBase<TypeOfValue> : FieldDataBase<TypeOfValue>, IReferenceData where TypeOfValue : notnull {
		public ReferenceDataBase(DatContainer dat) : base(dat) { }

		/// <inheritdoc/>
		public virtual long Offset {
			get => _Offset;
			protected set {
				if (_Offset == value)
					return;
				_Offset = value;
				RaisePropertyChanged(this, new(nameof(Offset)));
				RaisePropertyChanged(this, new(nameof(EndOffset)));
			}
		}
		private long _Offset;

		/// <inheritdoc/>
		public virtual int Length {
			get => _Length;
			protected set {
				if (_Length == value)
					return;
				_Length = value;
				RaisePropertyChanged(this, new(nameof(Length)));
				RaisePropertyChanged(this, new(nameof(EndOffset)));
			}
		}
		private int _Length;

		/// <inheritdoc/>
		public virtual long EndOffset => Offset + Length;

		IReferenceData IReferenceData.Read(BinaryReader reader) => Read(reader);

		/// <summary>
		/// Read the pointer and call <see cref="ReadInDataSection(BinaryReader)"/>.
		/// This won't check the <see cref="DatContainer.ReferenceDatas"/>, use <see cref="StringData.Read(BinaryReader, DatContainer)"/> or <see cref="ArrayData{TypeOfValueInArray}.Read(BinaryReader, DatContainer, string)"/> instead.
		/// </summary>
		public override ReferenceDataBase<TypeOfValue> Read(BinaryReader reader) {
			Offset = Dat.x64 ? reader.ReadInt64() : reader.ReadInt32();
			Dat.ReferenceDatas[Offset] = this;

			var previousPos = reader.BaseStream.Position;
			var begin = reader.BaseStream.Seek(Offset + Dat.DataSectionOffset, SeekOrigin.Begin);
			ReadInDataSection(reader);
			Length = (int)(reader.BaseStream.Position - begin);
			reader.BaseStream.Seek(previousPos, SeekOrigin.Begin);

			if (Length != 0)
				Dat.ReferenceDataOffsets[ToString()] = Offset;
			return this;
		}

		/// <summary>
		/// Read the value from the DataSection
		/// </summary>
		protected abstract void ReadInDataSection(BinaryReader reader);

		/// <summary>
		/// Write the pointer and call <see cref="WriteInDataSection(BinaryWriter)"/>
		/// </summary>
		public override void Write(BinaryWriter writer) {
			var s = ToString();
			var contains = false;
			if (Dat.ReferenceDataOffsets.TryGetValue(s, out long offset)) {
				var fd = Dat.ReferenceDatas[offset];
				if (fd == this)
					contains = true;
				// Array is a special case that will cause the game to crash if two fields point to the same position even if their datas are completely equal
				if (fd is not IArrayData) {
					if (Dat.x64)
						writer.Write(this.Offset = offset);
					else
						writer.Write((uint)(this.Offset = offset));
					return;
				}
			}

			var Offset = Dat.CurrentOffset;
			if (!contains)
				this.Offset = Offset;
			Dat.ReferenceDatas.Add(Offset, this);
			Dat.ReferenceDataOffsets.TryAdd(s, Offset);

			if (Dat.x64)
				writer.Write(Offset);
			else
				writer.Write((uint)Offset);
			var previousPos = writer.BaseStream.Position;
			var begin = writer.BaseStream.Seek(Offset + Dat.DataSectionOffset, SeekOrigin.Begin);
			Dat.CurrentOffset += CalculateLength(); // For array
			WriteInDataSection(writer);
			Length = (int)(writer.BaseStream.Position - begin);
			writer.BaseStream.Seek(previousPos, SeekOrigin.Begin);
		}

		/// <summary>
		/// Write the value to the DataSection
		/// </summary>
		protected abstract void WriteInDataSection(BinaryWriter writer);

		/// <summary>
		/// Calculate the length of data in DataSection with current <see cref="FieldDataBase{TypeOfValue}.Value"/>
		/// </summary>
		public abstract int CalculateLength();

		IReferenceData IReferenceData.FromString(string value) => (IReferenceData)FromString(value);
	}
}