using LibBundle3;
using LibBundle3.Records;
using LibGGPK3;

namespace LibBundledGGPK {
	public class BundledGGPK : GGPK {
		public Index Index { get; }

		/// <param name="parsePathsInIndex">Whether to parse the file paths in <see cref="index"/>. <see langword="false"/> to speed up reading but all <see cref="FileRecord.Path"/> and <see cref="FileRecord.DirectoryRecord"/> in each of <see cref="Index._Files"/> will be <see langword="null"/></param>
		public BundledGGPK(string filePath, bool parsePathsInIndex = true) : base(filePath) {
			var f = (LibGGPK3.Records.FileRecord)FindNode("Bundles2/_.index.bin")!;
			Index = new(new GGFileStream(f), false, parsePathsInIndex) {
				FuncReadBundle = (br) => new(new GGFileStream((LibGGPK3.Records.FileRecord)FindNode("Bundles2/" + br.Path)!), false, br)
			};
		}

#pragma warning disable CA1816
		public override void Dispose() {
			Index.Dispose();
			base.Dispose();
		}
	}
}