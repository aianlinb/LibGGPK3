using System.IO;

using LibBundle3;

using LibGGPK3;
using LibGGPK3.Records;

using SystemExtensions;

namespace LibBundledGGPK3;
/// <summary>
/// <see cref="GGPK"/> but also parses "Bundles2/_.index.bin" to <see cref="Index"/>
/// </summary>
public class BundledGGPK : GGPK {
	/// For processing bundles in ggpk
	public Index Index { get; }

	private Index Ctor(bool parsePathsInIndex = true) {
		var bundles2 = Root["Bundles2"] as DirectoryRecord;
		if (bundles2 is null)
			ThrowHelper.Throw<DirectoryNotFoundException>("Cannot find directory \"Bundles2\" in the ggpk");
		var index = bundles2["_.index.bin"] as FileRecord;
		if (index is null)
			ThrowHelper.Throw<FileNotFoundException>("Cannot find file \"Bundles2/_.index.bin\" in the ggpk");
		return new(new GGFileStream(index), false, parsePathsInIndex, new GGPKBundleFactory(this, bundles2));
	}

	/// <param name="filePath">Path to Content.ggpk on disk</param>
	/// <param name="parsePathsInIndex">
	/// Whether to call <see cref="Index.ParsePaths"/> automatically.
	/// <see langword="false"/> to speed up reading, but all <see cref="LibBundle3.Records.FileRecord.Path"/> in each of <see cref="Index.Files"/> of <see cref="Index"/> will be <see langword="null"/>,
	/// and <see cref="Index.Root"/> and <see cref="Index.BuildTree(Index.CreateDirectoryInstance, Index.CreateFileInstance, bool)"/> will be unable to use until you call <see cref="Index.ParsePaths"/> manually.
	/// </param>
	/// <exception cref="FileNotFoundException" />
	public BundledGGPK(string filePath, bool parsePathsInIndex = true) : base(filePath) {
		Index = Ctor(parsePathsInIndex);
	}
	/// <param name="stream">Stream of the Content.ggpk file</param>
	/// <param name="leaveOpen">If false, close the <paramref name="stream"/> when this instance is disposed</param>
	/// <param name="parsePathsInIndex">
	/// Whether to call <see cref="Index.ParsePaths"/> automatically.
	/// <see langword="false"/> to speed up reading, but all <see cref="LibBundle3.Records.FileRecord.Path"/> in each of <see cref="Index.Files"/> of <see cref="Index"/> will be <see langword="null"/>,
	/// and <see cref="Index.Root"/> and <see cref="Index.BuildTree(Index.CreateDirectoryInstance, Index.CreateFileInstance, bool)"/> will be unable to use until you call <see cref="Index.ParsePaths"/> manually.
	/// </param>
	/// <exception cref="FileNotFoundException" />
	public BundledGGPK(Stream stream, bool leaveOpen = false, bool parsePathsInIndex = true) : base(stream, leaveOpen) {
		Index = Ctor(parsePathsInIndex);
	}

#pragma warning disable CA1816
	public override void Dispose() {
		Index.Dispose();
		base.Dispose(); // GC.SuppressFinalize(this) in here
	}
}