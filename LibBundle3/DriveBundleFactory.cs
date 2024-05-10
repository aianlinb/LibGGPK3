using System.IO;

using LibBundle3.Records;

using File = System.IO.File;

namespace LibBundle3 {
	public readonly struct DriveBundleFactory : IBundleFileFactory {
		/// <summary>
		/// Path of "Bundles2" (parent of _.index.bin) on disk. (ends with slash)
		/// </summary>
		public string BaseDirectory { get; }

		/// <param name="baseDirectoryPath">
		/// Path of "Bundles2" (parent of _.index.bin) on disk.
		/// </param>
		public DriveBundleFactory(string baseDirectoryPath) {
			BaseDirectory = Path.GetFullPath(baseDirectoryPath);
			if (BaseDirectory[^1] != Path.DirectorySeparatorChar) // Works for C:\ etc..
				BaseDirectory += Path.DirectorySeparatorChar;
		}

		public Bundle GetBundle(BundleRecord record) {
			return new(BaseDirectory + record.Path, record);
		}

		public Stream CreateBundle(string bundlePath) {
			bundlePath = BaseDirectory + bundlePath;
			Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)!);
			return File.Create(bundlePath);
		}

		public bool DeleteBundle(string bundlePath) {
			bundlePath = BaseDirectory + bundlePath;
			if (!File.Exists(bundlePath))
				return false;
			File.Delete(bundlePath);
			return true;
		}
	}
}