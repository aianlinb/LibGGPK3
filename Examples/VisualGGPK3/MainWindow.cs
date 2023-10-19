using Eto.Drawing;
using Eto.Forms;
using LibBundledGGPK3;
using LibGGPK3;
using LibGGPK3.Records;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VisualGGPK3.TreeItems;

namespace VisualGGPK3 {
	public class MainWindow : Form {
		public BundledGGPK? Ggpk { get; protected set; }
		public LibBundle3.Index Index { get; protected set; }
#pragma warning disable CS0618
		protected readonly TreeView GGPKTree = new();
		protected readonly TreeView BundleTree = new();
		protected readonly TextArea TextPanel = new() { ReadOnly = true };
		protected readonly ImageView ImagePanel = new();
		protected readonly GridView DatPanel = new();

		protected string? imageName;
		protected ITreeItem? clickedItem;

#pragma warning disable CS8618
		public MainWindow(string? path = null) {
#if Mac
			static void closed(object? sender, EventArgs e) => Application.Instance.Quit();
			Closed += closed;
			Application.Instance.Terminating += (s, e) => Closed -= closed;
#endif
			var version = Assembly.GetExecutingAssembly().GetName().Version!;
			if (version.Revision != 0)
				Title = $"VisualGGPK3 (v{version.Major}.{version.Minor}.{version.Build}.{version.Revision})";
			else
				Title = $"VisualGGPK3 (v{version.Major}.{version.Minor}.{version.Build})";
			try {
				var bounds = Screen.Bounds;
				if (bounds.Width <= 1280 || bounds.Height <= 720)
					Size = new(960, 540);
				else
					Size = new(1280, 720);
			} catch {
				Size = new(1280, 720);
			}
#if Windows
			var h = Handler;
			static void WindowsFix(TreeView tree) {
				var etree = ((Eto.Wpf.Forms.Controls.TreeViewHandler)tree.Handler).Control; // EtoTreeView
																							// Virtualizing
				etree.SetValue(System.Windows.Controls.VirtualizingStackPanel.IsVirtualizingProperty, true);
				etree.SetValue(System.Windows.Controls.VirtualizingStackPanel.VirtualizationModeProperty, System.Windows.Controls.VirtualizationMode.Recycling);
				// Fix expand binding
				var setter = (System.Windows.Setter)etree.ItemContainerStyle.Setters[0];
				((System.Windows.Data.Binding)setter.Value).Mode = System.Windows.Data.BindingMode.TwoWay; // From OneTime
			}
			WindowsFix(GGPKTree);
			WindowsFix(BundleTree);
			// Virtualizing
			var gtext = ((Eto.Wpf.Forms.Controls.GridViewHandler)DatPanel.Handler).Control; // EtoDataGrid
			gtext.SetValue(System.Windows.Controls.VirtualizingStackPanel.IsVirtualizingProperty, true);
			gtext.SetValue(System.Windows.Controls.VirtualizingStackPanel.VirtualizationModeProperty, System.Windows.Controls.VirtualizationMode.Recycling);
#endif
			GGPKTree.SelectionChanged += OnSelectionChanged;
			BundleTree.SelectionChanged += OnSelectionChanged;

			var menu = new ContextMenu(
				new ButtonMenuItem(OnExtractClicked) { Text = "Extract" },
				new ButtonMenuItem(OnReplaceClicked) { Text = "Replace" }
			);
			GGPKTree.MouseUp += (s, e) => {
				if (e.Buttons == MouseButtons.Alternate && GGPKTree.GetNodeAt(e.Location) is ITreeItem item) {
					clickedItem = item;
					menu.Show(GGPKTree);
				}
			};
			BundleTree.MouseUp += (s, e) => {
				if (e.Buttons == MouseButtons.Alternate && BundleTree.GetNodeAt(e.Location) is ITreeItem item) {
					clickedItem = item;
					menu.Show(BundleTree);
				}
			};
			var menu2 = new ContextMenu(new ButtonMenuItem(OnSaveAsPngClicked) { Text = "Save as png" });
			ImagePanel.MouseUp += (s, e) => {
				if (e.Buttons == MouseButtons.Alternate)
					menu2.Show(ImagePanel, e.Location);
			};

			var layout = new Splitter() {
				Panel1 = GGPKTree,
				Panel1MinimumSize = 10,
				Panel2 = new Splitter() {
					Panel1 = BundleTree,
					Panel1MinimumSize = 10,
					Panel2 = ImagePanel,
					Panel2MinimumSize = 10,
					SplitterWidth = 4,
					Position = 240
				},
				Panel2MinimumSize = 20,
				SplitterWidth = 4,
				Position = 120
			};

			var loading = new TreeItemCollection() {
				new TreeItem() { Text = "Loading . . ." }
			};
			GGPKTree.DataStore = loading;
			BundleTree.DataStore = loading;

			Content = layout;
			LoadComplete += OnLoadComplete;

			async void OnLoadComplete(object? sender, EventArgs e) {
				LoadComplete -= OnLoadComplete;
				await Task.Yield();
				if (path == null || !File.Exists(path)) {
					var ofd = new OpenFileDialog() {
						FileName = "Content.ggpk",
						Filters = {
							new() { Name = "GGPK/Index File", Extensions = new string[] { ".ggpk", ".index.bin" } },
							new() { Extensions = new string[] { "" } }
						}
					};
					if (ofd.ShowDialog(this) != DialogResult.Ok) {
						Close();
						return;
					}
					path = ofd.FileName;
				}

				if (path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) {
					await Task.Run(() => Index = new(path));
					GGPKTree.DataStore = new DriveDirectoryTreeItem(Path.GetDirectoryName(path)!, null, GGPKTree) {
						Expanded = true
					};
				} else {
					await Task.Run(() => Ggpk = new(path));
					Index = Ggpk!.Index;
					GGPKTree.DataStore = new GGPKDirectoryTreeItem(Ggpk.Root, null, GGPKTree) {
						Expanded = true
					};
				}
				var bundles = await Task.Run(() => (BundleDirectoryTreeItem)Index!.BuildTree(BundleDirectoryTreeItem.GetFuncCreateInstance(BundleTree), BundleFileTreeItem.CreateInstance));
				bundles.Expanded = true;
				BundleTree.DataStore = bundles;
			}
		}

		protected virtual void OnSelectionChanged(object? sender, EventArgs e) {
			var item = (sender as TreeView)?.SelectedItem;
			if (item == null)
				return;
#if Windows
			if (item.Expandable)
				item.Expanded = !item.Expanded;
#endif
			if (item is FileTreeItem fileItem) {
				var panel = (Splitter)((Splitter)Content).Panel2;
				var format = fileItem.Format;
				try {
					switch (format) {
						case FileTreeItem.DataFormat.Text:
							var span = fileItem.Read().Span;
#if Windows
							if (span.Length > 204800) {
								MessageBox.Show(this, "This text file is too large, only first 200KB will be shown", "Warning", MessageBoxButtons.OK, MessageBoxType.Warning);
								span = span[..204800];
							}
#endif
							if (span[0] == 0xFF)
								TextPanel.Text = span[2..].GetUTF16String();
							else if (Path.GetExtension(fileItem.Name).Equals(".amd", StringComparison.OrdinalIgnoreCase))
								TextPanel.Text = span.GetUTF16String();
							else
								TextPanel.Text = Encoding.UTF8.GetString(span);
							panel.Panel2 = TextPanel;
							break;
						case FileTreeItem.DataFormat.Image:
							imageName = Path.GetFileNameWithoutExtension(fileItem.Name);
							if (fileItem is GGPKFileTreeItem g)
								ImagePanel.Image = new Bitmap(g.Record.Read());
							else if (fileItem is DriveFileTreeItem d)
								using (var stream = File.OpenRead(d.Path))
									ImagePanel.Image = new Bitmap(stream);
							else
								ImagePanel.Image = new Bitmap(fileItem.Read().ToArray());
							panel.Panel2 = ImagePanel;
							break;
						case FileTreeItem.DataFormat.Dds:
							imageName = Path.GetFileNameWithoutExtension(fileItem.Name);
							// TODO
							//panel.Panel2 = ImagePanel;
							break;
						case FileTreeItem.DataFormat.Dat:
						// TODO
						//panel.Panel2 = DatPanel;
						//break;
						default:
							TextPanel.Text = "";
							panel.Panel2 = TextPanel;
							ImagePanel.Image = null;
							DatPanel.DataStore = null;
							break;
					}
				} catch (Exception ex) {
					TextPanel.Text = ex.ToString();
					panel.Panel2 = TextPanel;
				}
			}
		}

		protected virtual void OnExtractClicked(object? sender, EventArgs e) {
			if (clickedItem is FileTreeItem fi) {
				var sfd = new SaveFileDialog() {
					FileName = fi.Name,
					Filters = {
						new() { Name = "All Files", Extensions = new string[] { "*" } }
					}
				};
				if (sfd.ShowDialog(this) != DialogResult.Ok)
					return;
				var span = fi.Read().Span;
				using (var f = File.Create(sfd.FileName, 0, FileOptions.SequentialScan))
					f.Write(span);
				MessageBox.Show(this, $"Extracted {span.Length} bytes to\r\n{sfd.FileName}", "Done", MessageBoxType.Information);
			} else if (clickedItem is DirectoryTreeItem di) {
				var sfd = new SaveFileDialog() {
					CheckFileExists = false,
					FileName = di.Name + ".dir",
					Filters = {
						new() { Name = "All Files", Extensions = new string[] { "*" } }
					}
				};
				if (sfd.ShowDialog(this) != DialogResult.Ok)
					return;
				var dir = Path.GetDirectoryName(sfd.FileName)!;
				MessageBox.Show(this, $"Extracted {di.Extract(dir)} files to\r\n{dir}", "Done", MessageBoxType.Information);
			}
		}

		protected virtual void OnReplaceClicked(object? sender, EventArgs e) {
			if (clickedItem is FileTreeItem fi) {
				var ofd = new OpenFileDialog() {
					FileName = fi.Name,
					Filters = {
						new() { Name = "All Files", Extensions = new string[] { "*" } }
					}
				};
				if (ofd.ShowDialog(this) == DialogResult.Ok) {
					var b = File.ReadAllBytes(ofd.FileName);
					fi.Write(b);
					MessageBox.Show(this, $"Replaced {b.Length} bytes from\r\n{ofd.FileName}", "Done", MessageBoxType.Information);
				}
			} else if (clickedItem is DirectoryTreeItem di) {
				var ofd = new OpenFileDialog() {
					CheckFileExists = false,
					FileName = "{OPEN IN A FOLDER}",
					Filters = {
						new() { Name = "All Files", Extensions = new string[] { "*" } }
					}
				};
				if (ofd.ShowDialog(this) != DialogResult.Ok)
					return;
				var dir = Path.GetDirectoryName(ofd.FileName)!;
				MessageBox.Show(this, $"Replaced {di.Replace(dir)} files from\r\n{dir}", "Done", MessageBoxType.Information);
			}
		}

		protected virtual void OnSaveAsPngClicked(object? sender, EventArgs e) {
			var sfd = new SaveFileDialog {
				FileName = (imageName ?? "unnamed") + ".png",
				Filters = { new() { Name = "Png File", Extensions = new string[] { "*.png" } } }
			};
			if (sfd.ShowDialog(this) != DialogResult.Ok)
				return;
			((Bitmap)ImagePanel.Image).Save(sfd.FileName, ImageFormat.Png);
		}
	}
}