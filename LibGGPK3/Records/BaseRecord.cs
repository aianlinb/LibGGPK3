using System;
using System.Collections.Generic;

namespace LibGGPK3.Records;
/// <summary>
/// Base type of all records in GGPK
/// </summary>
public abstract class BaseRecord(uint length, GGPK ggpk) {

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
    /// <remarks>
    /// If you're looking for the file length, <see cref="FileRecord.DataLength"/> may be what you want
    /// </remarks>
    public uint Length { get; protected internal set; } = length;

    /// <summary>
    /// Write the record data to the current position of GGPK stream, this method must set <see cref="Offset"/> to where the record begins
    /// </summary>
    protected internal abstract void WriteRecordData();

    /// <summary>
    /// For <see cref="MemoryExtensions.BinarySearch{T}(ReadOnlySpan{T}, IComparable{T})"/> to search span sorted by <see cref="Length"/>
    /// </summary>
	public readonly struct LengthWrapper(uint length) : IComparable<BaseRecord> {
		public readonly int CompareTo(BaseRecord? other) => other is null ? 1 : length.CompareTo(other.Length);
	}
    /// <summary>
    /// For sorting by <see cref="Length"/>
    /// </summary>
	public class LengthComparer : IComparer<BaseRecord> {
		public int Compare(BaseRecord? x, BaseRecord? y) =>
            x is null ? y is null ? 0 : -1 : y is null ? 1 : x.Length.CompareTo(y.Length);
	}

	/// <summary>
	/// For <see cref="MemoryExtensions.BinarySearch{T}(ReadOnlySpan{T}, IComparable{T})"/> to search span sorted by <see cref="Offset"/>
	/// </summary>
	public readonly struct OffsetWrapper(long offset) : IComparable<BaseRecord> {
		public readonly int CompareTo(BaseRecord? other) => other is null ? 1 : offset.CompareTo(other.Offset);
	}
	/// <summary>
	/// For sorting by <see cref="Offset"/>
	/// </summary>
	public class OffsetComparer : IComparer<BaseRecord> {
		public int Compare(BaseRecord? x, BaseRecord? y) =>
			x is null ? y is null ? 0 : -1 : y is null ? 1 : x.Offset.CompareTo(y.Offset);
	}
}