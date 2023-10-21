using LibBundle3;
using LibGGPK3;
using LibGGPK3.Records;
using System.IO;

namespace LibBundledGGPK3 {
	public class BundledGGPK : GGPK {
		/// For processing bundles in ggpk
		public Index Index { get; }

		/// <param name="filePath">Path to Content.ggpk on disk</param>
		/// <param name="parsePathsInIndex">
		/// Whether to call <see cref="Index.ParsePaths"/> automatically.
		/// <see langword="false"/> to speed up reading, but all <see cref="LibBundle3.Records.FileRecord.Path"/> in each of <see cref="Index.Files"/> of <see cref="Index"/> will be <see langword="null"/>,
		/// and <see cref="Index.Root"/> and <see cref="Index.BuildTree"/> will be unable to use until you call <see cref="Index.ParsePaths"/> manually.
		/// </param>
		/// <exception cref="FileNotFoundException" />
		public BundledGGPK(string filePath, bool parsePathsInIndex = true) : base(filePath) {
			var bundles2 = Root["Bundles2"] as DirectoryRecord ?? throw new("Cannot find directory \"Bundles2\" in GGPK: " + filePath);
			var index = bundles2["_.index.bin"] as FileRecord ?? throw new("Cannot find file \"Bundles2/_.index.bin\" in GGPK: " + filePath);
			Index = new(new GGFileStream(index), false, parsePathsInIndex, new GGPKBundleFactory(this, bundles2));
		}

#pragma warning disable CA1816
		public override void Dispose() {
			Index.Dispose();
			base.Dispose(); // GC.SuppressFinalize(this) in here
		}
	}
}