using System;

namespace LibGGPK3.Records;
/// <summary>
/// Base type of all records in GGPK
/// </summary>
public abstract class BaseRecord {
	public BaseRecord(int length, GGPK ggpk) {
		ArgumentOutOfRangeException.ThrowIfNegative(length);
		Length = length;
		Ggpk = ggpk;
	}

	/// <summary>
	/// GGPK which contains this record
	/// </summary>
	public GGPK Ggpk { get; }

	/// <summary>
	/// Offset in pack file where record begins
	/// </summary>
	public long Offset { get; protected internal set; }

	/// <summary>
	/// Length of the entire record in bytes
	/// </summary>
	/// <remarks>
	/// If you're looking for the file length, <see cref="FileRecord.DataLength"/> may be what you want
	/// </remarks>
	public int Length { get; protected internal set; }

	/// <summary>
	/// Write the record data to the current position of GGPK stream, this method must set <see cref="Offset"/> to where the record begins
	/// </summary>
	protected internal abstract void WriteRecordData();
}