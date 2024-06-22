using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

using SystemExtensions.Streams;

namespace LibBundle3.Records;
public class BundleRecord {
	/// <summary>
	/// <see cref="Path"/> without extension (which actually recorded in <see cref="Index"/> file)
	/// </summary>
	protected internal string _Path;
	/// <summary>
	/// Path of the bundle file (which ends with ".bundle.bin") under "Bundles2" directory
	/// </summary>
	public virtual string Path => _Path + ".bundle.bin";
	/// <summary>
	/// Size of the uncompressed content in bytes, synced with <see cref="Bundle.UncompressedSize"/>
	/// </summary>
	public virtual int UncompressedSize { get; protected internal set; }
	/// <summary>
	/// Index of the <see cref="BundleRecord"/> in <see cref="Index.Bundles"/>
	/// </summary>
	public virtual int BundleIndex { get; }
	/// <summary>
	/// <see cref="Index"/> instance which contains this bundle
	/// </summary>
	public virtual Index Index { get; }

	protected internal readonly List<FileRecord> _Files = [];
	/// <summary>
	/// Files contained in this bundle, may be changed after <see cref="FileRecord.Redirect"/>
	/// </summary>
	public virtual ReadOnlyCollection<FileRecord> Files => new(_Files);

	protected internal BundleRecord(string path, int uncompressedSize, Index index, int bundleIndex) {
		_Path = path;
		UncompressedSize = uncompressedSize;
		Index = index;
		BundleIndex = bundleIndex;
	}

	/// <summary>
	/// Try to get the bundle instance with <see cref="IBundleFileFactory.GetBundle"/>
	/// </summary>
	/// <remarks>Must dispose the bundle after use</remarks>
	/// <returns>Whether successfully get the instance</returns>
	public virtual bool TryGetBundle([NotNullWhen(true)] out Bundle? bundle) {
		return TryGetBundle(out bundle, out _);
	}
	/// <summary>
	/// Try to get the bundle instance with <see cref="IBundleFileFactory.GetBundle"/>
	/// </summary>
	/// <param name="exception">Exception thrown by <see cref="IBundleFileFactory.GetBundle"/> if failed to get</param>
	/// <remarks>Must dispose the bundle after use</remarks>
	/// <returns>Whether successfully get the instance</returns>
	public virtual bool TryGetBundle([NotNullWhen(true)] out Bundle? bundle, [MaybeNullWhen(true)] out Exception exception) {
		exception = null;
		try {
			bundle = Index.bundleFactory.GetBundle(this);
			return true;
		} catch (Exception ex) {
			exception = ex;
			bundle = null;
			return false;
		}
	}

	/// <summary>
	/// Size of the content when <see cref="Serialize"/> to <see cref="Index"/>
	/// </summary>
	protected internal int RecordLength => _Path.Length + (sizeof(int) + sizeof(int));
	/// <summary>
	/// Function to serialize the record to <see cref="Index"/>
	/// </summary>
	[SkipLocalsInit]
	protected internal virtual void Serialize(Stream stream) {
		Span<byte> span = stackalloc byte[_Path.Length]; // Since _Path contains only ASCII characters
		var count = Encoding.UTF8.GetBytes(_Path, span);
		stream.Write(count);
		stream.Write(span[..count]);
		stream.Write(UncompressedSize);
	}
}