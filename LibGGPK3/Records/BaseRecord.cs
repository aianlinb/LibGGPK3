namespace LibGGPK3.Records {
	/// <summary>
	/// Base type of all records in GGPK
	/// </summary>
	public abstract class BaseRecord {
		/// <summary>
		/// Length of the entire record in bytes
		/// </summary>
		public int Length { get; protected internal set; }

		/// <summary>
		/// Offset in pack file where record begins
		/// </summary>
		public long Offset { get; protected internal set; }

		/// <summary>
		/// GGPK which contains this record
		/// </summary>
		public GGPK Ggpk { get; }

		protected BaseRecord(int length, GGPK ggpk) {
			Length = length;
			Ggpk = ggpk;
		}

		/// <summary>
		/// Write the record data to the current position of GGPK stream, this method must set <see cref="Offset"/> to where the record begins
		/// </summary>
		protected internal abstract void WriteRecordData();
	}
}