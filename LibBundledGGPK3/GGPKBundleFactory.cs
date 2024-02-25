using System;
using System.IO;

using LibBundle3;

using LibGGPK3;
using LibGGPK3.Records;

namespace LibBundledGGPK3 {
	/// <param name="bundles2">
	/// Record of "Bundles2" (parent of _.index.bin) in GGPK.
	/// </param>
	public class GGPKBundleFactory(BundledGGPK ggpk, DirectoryRecord bundles2) : IBundleFileFactory {
		public BundledGGPK Ggpk { get; } = ggpk;
		/// <summary>
		/// Record of "Bundles2" (parent of _.index.bin) in GGPK.
		/// </summary>
		protected readonly DirectoryRecord Bundles2 = bundles2;

		public virtual Bundle GetBundle(LibBundle3.Records.BundleRecord record) {
			return new(
					new GGFileStream(
						Ggpk.TryFindNode(record.Path, out var node, Bundles2) && node is FileRecord fr
						? fr
						: throw new FileNotFoundException("Cannot find bundle: \"Bundles2/" + record.Path + "\" in GGPK")),
					false,
					record
				);
		}

		public virtual Stream CreateBundle(string bundlePath) {
			var i = bundlePath.LastIndexOf('/');
			if (i < 0)
				return new GGFileStream(Bundles2.AddFile(bundlePath));
			return new GGFileStream(Ggpk.FindOrCreateDirectory(bundlePath.AsSpan(0, i), Bundles2).AddFile(bundlePath[(i + 1)..]));
		}

		public virtual bool RemoveAllCreatedBundle(string customBundleBasePath) {
			if (!Ggpk.TryFindNode(customBundleBasePath.TrimEnd('/'), out var node, Bundles2))
				return false;
			node.Remove();
			return true;
		}
	}
}