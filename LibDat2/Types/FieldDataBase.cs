using System.ComponentModel;
using System.IO;

namespace LibDat2.Types {
	public abstract class FieldDataBase<TypeOfValue> : IFieldData where TypeOfValue : notnull {
		/// <summary>
		/// <see cref="DatContainer"/> which created this instance
		/// </summary>
		public DatContainer Dat;

		/// <summary>
		/// Data of this field
		/// </summary>
		public virtual TypeOfValue Value {
			get => _Value;
			set {
				_Value = value;
				PropertyChanged(this, new(nameof(Value)));
				PropertyChanged(this, new(nameof(StringValue)));
			}
		}
		private TypeOfValue _Value;

		/// <inheritdoc/>
		object IFieldData.Value { get => Value!; set => Value = (TypeOfValue)value; }

		/// <summary>
		/// <see cref="Value"/> in string representation.
		/// Equals to <see cref="ToString()"/> and <see cref="FromString(string)"/>
		/// </summary>
		public virtual string StringValue { get => ToString(); set => FromString(value); }

#pragma warning disable CS8612
		public event PropertyChangedEventHandler PropertyChanged = new((o, e) => { });
#pragma warning restore CS8612
		protected virtual void RaisePropertyChanged(object sender, PropertyChangedEventArgs e) => PropertyChanged(sender, e);

		/// <summary>
		/// Create an instance of <see cref="FieldDataBase{TypeOfValue}"/>
		/// </summary>
#pragma warning disable CS8618
		public FieldDataBase(DatContainer dat) {
			Dat = dat;
		}

		/// <summary>
		/// Read the <see cref="Value"/> from a dat file
		/// </summary>
		public abstract FieldDataBase<TypeOfValue> Read(BinaryReader reader);

		IFieldData IFieldData.Read(BinaryReader reader) {
			return Read(reader);
		}

		/// <summary>
		/// Write the <see cref="Value"/> to a dat file
		/// </summary>
		public abstract void Write(BinaryWriter writer);

		/// <summary>
		/// Read the <see cref="Value"/> from its string representation
		/// </summary>
		public abstract FieldDataBase<TypeOfValue> FromString(string value);

		IFieldData IFieldData.FromString(string value) {
			return FromString(value);
		}

		/// <summary>
		/// Get the string representation of the <see cref="Value"/>
		/// </summary>
		public override string ToString() {
			return Value?.ToString() ?? "{null}"; // "{null}" is an error
		}
	}
}