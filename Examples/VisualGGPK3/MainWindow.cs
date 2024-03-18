using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Eto.Drawing;
using Eto.Forms;

using ImageMagick;

using LibBundledGGPK3;

using VisualGGPK3.TreeItems;

namespace VisualGGPK3 {
	public sealed class MainWindow : Form {
		private BundledGGPK? Ggpk;
		private LibBundle3.Index? Index;
#pragma warning disable CS0618
		private readonly TreeView GGPKTree = new();
		private readonly TreeView BundleTree = new();
#pragma warning restore CS0618
		private readonly TextArea TextPanel = new() { ReadOnly = true };
		private readonly ImageView ImagePanel = new();
		private readonly GridView DatPanel = new();

		private string? imageName;
		private ITreeItem? clickedItem;

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

			var bounds = Screen.Bounds;
			if (bounds.Width <= 1280 || bounds.Height <= 720)
				Size = new(960, 540);
			else
				Size = new(1280, 720);
#if Windows
#pragma warning disable CS0618 // Obsolete
			static void WindowsFix(TreeView tree) {
				var etree = ((Eto.Wpf.Forms.Controls.TreeViewHandler)tree.Handler).Control; // EtoTreeView
#pragma warning restore CS0618
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
				new ButtonMenuItem(OnReplaceClicked) { Text = "Replace" },
				new ButtonMenuItem(OnCopyPathClicked) { Text = "Copy Path" }
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
					Panel2 = new Label() { Text = "This program is still in development" },
					Panel2MinimumSize = 10,
					SplitterWidth = 4,
					Position = 240
				},
				Panel2MinimumSize = 20,
				SplitterWidth = 4,
				Position = 160
			};

			var loading = new TreeItemCollection() {
				new TreeItem() { Text = "Loading . . ." }
			};
			GGPKTree.DataStore = loading;
			BundleTree.DataStore = loading;

			Content = layout;
			LoadComplete += OnLoadComplete;

			async void OnLoadComplete(object? sender, EventArgs _) {
				LoadComplete -= OnLoadComplete;
				await Task.Yield();
				if (path is null || !File.Exists(path)) {
					var ofd = new OpenFileDialog() {
						FileName = "Content.ggpk",
						Filters = {
							new("GGPK/Index File", ".ggpk", ".index.bin"),
							allFilesFilters
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

		private void OnSelectionChanged(object? sender, EventArgs _) {
#pragma warning disable CS0618 // Obsolete
			var item = (sender as TreeView)?.SelectedItem;
#pragma warning restore CS0618 // Obsolete
			if (item is null)
				return;
#if Windows
			if (item.Expandable)
				item.Expanded = !item.Expanded;
#endif
			if (item is FileTreeItem fileItem) {
				var panel = (Splitter)((Splitter)Content).Panel2;
				var format = fileItem.Format;
				try {
					if (ImagePanel.Image is not null) {
						ImagePanel.Image.Dispose();
						ImagePanel.Image = null;
					}
					switch (format) {
						case FileTreeItem.DataFormat.Text:
							var span = fileItem.Read().Span;
#if Windows
							if (span.Length > 204800) {
								MessageBox.Show(this, "This text file is too large, only the first 100KB will be shown", "Warning", MessageBoxButtons.OK, MessageBoxType.Warning);
								span = span[..102400];
							}
#endif
							if (span.IsEmpty)
								TextPanel.Text = "";
							else if (MemoryMarshal.GetReference(span) == 0xFF)
								TextPanel.Text = new string(MemoryMarshal.Cast<byte, char>(span[2..]));
							else if (fileItem.Name.EndsWith(".amd", StringComparison.OrdinalIgnoreCase))
								TextPanel.Text = new string(MemoryMarshal.Cast<byte, char>(span));
							else
								unsafe {
									fixed (byte* p = span)
										TextPanel.Text = new string((sbyte*)p, 0, span.Length);
								}
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

							ReadOnlySpan<byte> data;
							if (fileItem is GGPKFileTreeItem g2)
								data = g2.Record.Read();
							else if (fileItem is DriveFileTreeItem d)
								data = File.ReadAllBytes(d.Path);
							else
								data = fileItem.Read().Span;

							if (fileItem.Name.EndsWith(".header")) {
								data = data[0] == 3 ? data[28..] : data[16..];
								while (data[0] == '*') {
									data = data[1..];
									if (data.Length > 384 * 1024)
										throw new StackOverflowException();
#pragma warning disable CA2014
									Span<char> path = stackalloc char[data.Length * 2];
#pragma warning restore CA2014
									if (!Index!.TryGetFile(path[Encoding.UTF8.GetChars(data, path)..], out var file))
										throw new FileNotFoundException(null, path[Encoding.UTF8.GetChars(data, path)..].ToString(), null);
									if (path.EndsWith(".header"))
										data = data[0] == 3 ? data[28..] : data[16..];
								}
							}

							using (var image = new MagickImage(data))
								ImagePanel.Image = new Bitmap(image.ToByteArray(MagickFormat.Bmp));
							panel.Panel2 = ImagePanel;
							break;
						case FileTreeItem.DataFormat.Dat:
						// TODO
						//panel.Panel2 = DatPanel;
						//break;
						default:
							TextPanel.Text = "";
							panel.Panel2 = TextPanel;
							//DatPanel.DataStore = null;
							break;
					}
				} catch (Exception ex) {
					TextPanel.Text = ex.ToString();
					panel.Panel2 = TextPanel;
				}
			}
		}

		private void OnExtractClicked(object? sender, EventArgs _) {
			if (clickedItem is FileTreeItem fi) {
				var ext = "*" + Path.GetExtension(fi.Name);
				var sfd = new SaveFileDialog() {
					FileName = fi.Name,
					Filters = {
						new(ext, ext),
						allFilesFilters
					}
				};
				if (sfd.ShowDialog(this) != DialogResult.Ok)
					return;
				var span = fi.Read().Span;
				using (var f = File.OpenHandle(sfd.FileName, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.None, span.Length))
					RandomAccess.Write(f, span, 0);
				MessageBox.Show(this, $"Extracted {span.Length} bytes to\r\n{sfd.FileName}", "Done", MessageBoxType.Information);
			} else if (clickedItem is DirectoryTreeItem di) {
				var sfd = new SaveFileDialog() {
					CheckFileExists = false,
					FileName = di.Name + ".dir",
					Filters = { allFilesFilters }
				};
				if (sfd.ShowDialog(this) != DialogResult.Ok)
					return;
				var dir = Path.GetDirectoryName(sfd.FileName)!;
				MessageBox.Show(this, $"Extracted {di.Extract(dir)} files to\r\n{dir}", "Done", MessageBoxType.Information);
			}
		}

		private void OnReplaceClicked(object? sender, EventArgs _) {
			if (clickedItem is FileTreeItem fi) {
				var ext = "*" + Path.GetExtension(fi.Name);
				var ofd = new OpenFileDialog() {
					FileName = fi.Name,
					Filters = {
						new(ext, ext),
						allFilesFilters
					}
				};
				if (ofd.ShowDialog(this) != DialogResult.Ok)
					return;
				var b = File.ReadAllBytes(ofd.FileName);
				fi.Write(b);
				MessageBox.Show(this, $"Replaced {b.Length} bytes from\r\n{ofd.FileName}", "Done", MessageBoxType.Information);
			} else if (clickedItem is DirectoryTreeItem di) {
				var ofd = new OpenFileDialog() {
					CheckFileExists = false,
					FileName = "{OPEN IN A FOLDER}",
					Filters = { allFilesFilters }
				};
				if (ofd.ShowDialog(this) != DialogResult.Ok)
					return;
				var dir = Path.GetDirectoryName(ofd.FileName)!;
				MessageBox.Show(this, $"Replaced {di.Replace(dir)} files from\r\n{dir}", "Done", MessageBoxType.Information);
			}

			var bd2 = (GGPKTree.DataStore as GGPKDirectoryTreeItem)?.ChildItems.FirstOrDefault(t => t.Text == "Bundles2");
			if (bd2 is GGPKDirectoryTreeItem g) {
				g._ChildItems = null; // Update tree
				GGPKTree.RefreshItem(g);
			}
			OnSelectionChanged(BundleTree, EventArgs.Empty);
		}

		private void OnCopyPathClicked(object? sender, EventArgs _) {
			if (clickedItem is null)
				return;
			var builder = new StringBuilder(128);
			GetPath(clickedItem, builder);
			Clipboard.Instance.Text = builder.ToString();
		}
		private static void GetPath(ITreeItem node, StringBuilder builder) {
			if (node.Parent is null) // Root
				return;
			GetPath(node.Parent, builder);
			builder.Append(node.Text);
			if (node is DirectoryTreeItem)
				builder.Append('/');
		}

		private void OnSaveAsPngClicked(object? sender, EventArgs _) {
			var sfd = new SaveFileDialog {
				FileName = (imageName ?? "unnamed") + ".png",
				Filters = {
					new("Png File", "*.png"),
					allFilesFilters
				}
			};
			if (sfd.ShowDialog(this) != DialogResult.Ok)
				return;
			(ImagePanel.Image as Bitmap)!.Save(sfd.FileName, ImageFormat.Png);
		}

		private static readonly FileFilter allFilesFilters = new("All Files", "*");
	}
}