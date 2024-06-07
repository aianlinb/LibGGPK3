using System.IO;

using LibBundle3.Records;

namespace LibBundle3;
public interface IBundleFileFactory {
	/// <summary>
	/// Create a <see cref="Bundle"/> instance of the <paramref name="record"/>
	/// </summary>
	public Bundle GetBundle(BundleRecord record);

	/// <summary>
	/// Get a <see cref="Stream"/> (which will be cleared before writing) to write when creating a new bundle with specified <paramref name="bundlePath"/> (ends with ".bundle.bin")
	/// </summary>
	/// <param name="bundlePath">Relative path of the bundle which ends with ".bundle.bin"</param>
	public Stream CreateBundle(string bundlePath);

	/// <summary>
	/// Remove a bundle file with <paramref name="bundlePath"/>
	/// </summary>
	/// <param name="bundlePath">Relative path of the bundle which ends with ".bundle.bin"</param>
	/// <returns>
	/// <see langword="true"/> if the bundle is removed, <see langword="false"/> if the bundle doesn't exist
	/// </returns>
	public bool DeleteBundle(string bundlePath);
}