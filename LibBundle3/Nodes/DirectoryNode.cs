using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LibBundle3.Nodes {
	public class DirectoryNode : BaseNode {
		public ReadOnlyCollection<BaseNode> Children => new(_Children);
		protected internal readonly List<BaseNode> _Children = new();
		
		protected internal DirectoryNode(string name, DirectoryNode? parent) : base(name, parent) {
		}

		public override string GetPath() => Parent == null ? Name : Parent.GetPath() + Name + "/";
	}
}