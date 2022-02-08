using LibBundle3;
using LibGGPK3;
using LibGGPK3.Records;

namespace LibBundledGGPK {
	public class BundledGGPK : GGPK {
		public Index index;

		/// <param name="parsePathsInIndex">Whether to parse the file paths in <see cref="index"/>. "<see langword="false"/>" to speed up reading but all <see cref="FileRecord.Path"/> and <see cref="FileRecord.DirectoryRecord"/> in each of <see cref="Index.Files"/> will be <see langword="null"/></param>
		public BundledGGPK(string filePath, bool parsePathsInIndex = true) : base(filePath) {
			var f = (FileRecord)FindNode("Bundles2/_.index.bin")!;
			index = new(new GGFileStream(f), true, parsePathsInIndex);
			index.FuncReadBundle = (br) => new(new GGFileStream((FileRecord)FindNode("Bundles2/" + br.Path + ".bundle.bin")!));
		}

#pragma warning disable CA1816
		public override void Dispose() {
			base.Dispose();
			index.Dispose();
		}
	}
}