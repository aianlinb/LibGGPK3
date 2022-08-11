namespace LibBundle3.Nodes {
	public abstract class BaseNode {
		public readonly BaseNode? Parent;
		public readonly string Name;

		protected internal BaseNode(string name, DirectoryNode? parent) {
			Name = name;
			Parent = parent;
		}

		/// <summary>
		/// Get the absolute path in the tree
		/// </summary>
		public abstract string GetPath();
	}
}