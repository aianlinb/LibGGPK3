using System.IO;

namespace LibDat2.Types {
	public interface IReferenceData : IFieldData {
		// Dependents on the last read/saved dat file
		#region FileDependent
		/// <summary>
		/// Position of data in DataSection
		/// </summary>
		public long Offset { get; }

		/// <summary>
		/// Length of data in DataSection
		/// </summary>
		public int Length { get; }

		/// <summary>
		/// Position of end of data in DataSection
		/// </summary>
		/// <returns><see cref="Offset"/> + <see cref="Length"/></returns>
		public long EndOffset { get; }
		#endregion FileDependent

		/// <summary>
		/// Read the pointer and call <see cref="ReferenceDataBase{TypeOfValue}.ReadInDataSection"/>.
		/// This won't check the <see cref="DatContainer.ReferenceDatas"/>, use <see cref="StringData.Read(BinaryReader, DatContainer)"/> or <see cref="ArrayData{TypeOfValueInArray}.Read(BinaryReader, DatContainer, string)"/> instead.
		/// </summary>
		public new IReferenceData Read(BinaryReader reader);

		/// <summary>
		/// Write the pointer and call WriteInDataSection(BinaryWriter)
		/// </summary>
		public new void Write(BinaryWriter writer);

		/// <summary>
		/// Read the <see cref="IFieldData.Value"/> from its string representation
		/// This won't check the <see cref="DatContainer.ReferenceDatas"/>, use <see cref="StringData.FromString(string, DatContainer)"/> or <see cref="ArrayData{TypeOfValueInArray}.FromString(string, DatContainer, string)"/> instead.
		/// </summary>
		public new IReferenceData FromString(string value);

		/// <summary>
		/// Calculate the length of data in DataSection with current <see cref="IFieldData.Value"/>
		/// </summary>
		public int CalculateLength();
	}
}