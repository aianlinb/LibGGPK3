using System.IO;

using LibBundle3;

using LibGGPK3;
using LibGGPK3.Records;

using SystemExtensions;

namespace LibBundledGGPK3;
/// <param name="bundles2">
/// Record of "Bundles2" (parent of _.index.bin) in GGPK.
/// </param>
public class GGPKBundleFactory(GGPK ggpk, DirectoryRecord bundles2) : IBundleFactory {
	public GGPK Ggpk { get; } = ggpk;
	/// <summary>
	/// Record of "Bundles2" (parent of _.index.bin) in GGPK.
	/// </summary>
	protected readonly DirectoryRecord Bundles2 = bundles2;

	public virtual Bundle GetBundle(LibBundle3.Records.BundleRecord record) {
		return new(
				new GGFileStream(
					Bundles2.TryFindNode(record.Path, out var node) && node is FileRecord fr
					? fr
					: throw ThrowHelper.Create<FileNotFoundException>("Cannot find bundle: \"Bundles2/" + record.Path + "\" in GGPK")),
				false,
				record
			);
	}

	public virtual Stream CreateBundle(string bundlePath) {
		Bundles2.FindOrAddFile(bundlePath, out var fr);
		return new GGFileStream(fr);
	}

	public virtual bool DeleteBundle(string bundlePath) {
		if (!Bundles2.TryFindNode(bundlePath, out var node) || node is not FileRecord)
			return false;
		node.Remove();
		if (node.Parent.Count == 0)
			node.Parent.Remove();
		return true;
	}
}