namespace LibBundle3.Nodes {
	public abstract class BaseNode {
		public BaseNode? Parent;
		public string Name;

		public BaseNode(string name) {
			Name = name;
		}

		/// <summary>
		/// Get the absolute path in the tree
		/// </summary>
		public virtual string GetPath() {
			return Parent == null ? Name : Parent.GetPath() + Name;
		}
	}
}