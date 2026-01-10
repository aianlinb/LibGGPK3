using System.IO;
using System.Linq;

using LibBundle3.Records;

namespace LibBundle3;
public class DriveBundleFactory : IBundleFactory {
	/// <summary>
	/// Path of "Bundles2" (parent of _.index.bin) on the drive. (ends with slash)
	/// </summary>
	public string BaseDirectory { get; }

	/// <param name="baseDirectory">
	/// Path of "Bundles2" (parent of _.index.bin) on the drive.
	/// </param>
	public DriveBundleFactory(string baseDirectory) {
		BaseDirectory = Path.GetFullPath(baseDirectory);
		if (BaseDirectory[^1] != Path.DirectorySeparatorChar) // Works for C:\ etc..
			BaseDirectory += Path.DirectorySeparatorChar;
	}

	public virtual Bundle GetBundle(BundleRecord record) {
		return new(BaseDirectory + record.Path, record);
	}

	public virtual Stream CreateBundle(string bundlePath) {
		bundlePath = BaseDirectory + bundlePath;
		Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)!);
		return File.Create(bundlePath);
	}

	public virtual bool DeleteBundle(string bundlePath) {
		bundlePath = BaseDirectory + bundlePath;
		if (!File.Exists(bundlePath))
			return false;
		File.Delete(bundlePath);

		var dir = Path.GetDirectoryName(bundlePath)!;
		if (!Directory.EnumerateFileSystemEntries(dir).Any())
			Directory.Delete(dir);

		return true;
	}
}