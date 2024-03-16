namespace LibGGPK3.Records {
	/// <summary>
	/// Base type of all records in GGPK
	/// </summary>
	public abstract class BaseRecord(int length, GGPK ggpk) {
		/// <summary>
		/// GGPK which contains this record
		/// </summary>
		public GGPK Ggpk { get; } = ggpk;

		/// <summary>
		/// Offset in pack file where record begins
		/// </summary>
		public long Offset { get; protected internal set; }

		/// <summary>
		/// Length of the entire record in bytes
		/// </summary>
		public int Length { get; protected internal set; } = length;

		/// <summary>
		/// Write the record data to the current position of GGPK stream, this method must set <see cref="Offset"/> to where the record begins
		/// </summary>
		protected internal abstract void WriteRecordData();
	}
}