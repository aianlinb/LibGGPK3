using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LibBundle3.Nodes {
	public class DirectoryNode : BaseNode {
		public DirectoryNode(string name) : base(name) {
		}

		public SortedSet<BaseNode> Children = new(NodeComparer.Instance);

		public override string GetPath() => base.GetPath() + "/";

		protected sealed class NodeComparer : IComparer<BaseNode> {
			public static readonly IComparer<BaseNode> Instance = OperatingSystem.IsWindows() ? new NodeComparer_Windows() : new NodeComparer();

#pragma warning disable CS8767
			public int Compare(BaseNode x, BaseNode y) {
				if (x is DirectoryNode)
					if (y is DirectoryNode)
						return string.Compare(x.Name, y.Name);
					else
						return -1;
				else
					if (y is DirectoryNode)
						return 1;
					else
						return string.Compare(x.Name, y.Name);
			}

			public sealed class NodeComparer_Windows : IComparer<BaseNode> {
				[DllImport("shlwapi", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
				public static extern int StrCmpLogicalW(string x, string y);
				public int Compare(BaseNode x, BaseNode y) {
					if (x is DirectoryNode)
						if (y is DirectoryNode)
							return StrCmpLogicalW(x.Name, y.Name);
						else
							return -1;
					else
						if (y is DirectoryNode)
							return 1;
						else
							return StrCmpLogicalW(x.Name, y.Name);
				}
			}
		}
	}
}