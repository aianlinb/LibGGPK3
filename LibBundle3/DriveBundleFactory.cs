using System;
using System.IO;

using LibBundle3.Records;

using SystemExtensions;

using File = System.IO.File;

namespace LibBundle3 {
	public class DriveBundleFactory : IBundleFileFactory {
		/// <summary>
		/// Path of "Bundles2" (parent of _.index.bin) on disk.
		/// </summary>
		protected readonly string baseDirectory;

		/// <param name="baseDirectoryPath">
		/// Path of "Bundles2" (parent of _.index.bin) on disk.
		/// </param>
		public DriveBundleFactory(string baseDirectoryPath) {
			baseDirectory = Path.GetFullPath(baseDirectoryPath);
			if (baseDirectory[^1] != Path.DirectorySeparatorChar) // Works for C:\\ etc..
				baseDirectory += Path.DirectorySeparatorChar;
		}

		public virtual Bundle GetBundle(BundleRecord record) {
			return new(baseDirectory + record.Path, record);
		}

		public virtual Stream CreateBundle(string bundlePath) {
			bundlePath = baseDirectory + bundlePath;
			if (File.Exists(bundlePath))
				ThrowHelper.Throw<InvalidOperationException>("A file with the same name already exists: " + bundlePath);
			Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)!);
			return File.Create(bundlePath);
		}

		public virtual bool RemoveAllCreatedBundle(string customBundleBasePath) {
			customBundleBasePath = customBundleBasePath.TrimEnd('/');
			if (Directory.Exists(customBundleBasePath)) {
				Directory.Delete(customBundleBasePath, true);
				return true;
			}
			if (File.Exists(customBundleBasePath)) {
				File.Delete(customBundleBasePath);
				return true;
			}
			return false;
		}
	}
}