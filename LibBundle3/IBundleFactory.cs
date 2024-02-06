using System;
using System.IO;
using LibBundle3.Records;

namespace LibBundle3 {
	public interface IBundleFileFactory {
		/// <summary>
		/// Create a <see cref="Bundle"/> instance of a <see cref="BundleRecord"/>
		/// </summary>
		/// <exception cref="FileNotFoundException">When failed to get the bundle</exception>
		public Bundle GetBundle(BundleRecord record);

		/// <summary>
		/// Get a <see cref="Stream"/> (which will be cleared) to write when creating a new bundle with specified <paramref name="bundlePath"/> (ends with ".bundle.bin")
		/// </summary>
		/// <exception cref="Exception">When failed to create the bundle</exception>
		public Stream CreateBundle(string bundlePath);

		//public void DeleteBundle(string bundlePath);

		/// <summary>
		/// Remove all bundle file created by <see cref="CreateBundle(string)"/> with path starts with <paramref name="customBundleBasePath"/>
		/// </summary>
		/// <returns>Whether successfully found the directory and removed it</returns>
		public bool RemoveAllCreatedBundle(string customBundleBasePath);
	}
}