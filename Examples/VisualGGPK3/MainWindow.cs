using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Eto.Drawing;
using Eto.Forms;

using LibBundledGGPK3;
using LibGGPK3;

using Pfim;

using SystemExtensions;

using VisualGGPK3.TreeItems;

namespace VisualGGPK3;
public sealed class MainWindow : Form {
	private GGPK? Ggpk;
	internal LibBundle3.Index? Index;
#pragma warning disable CS0618
	private readonly TreeView GGPKTree = new();
	private readonly TreeView BundleTree = new();
#pragma warning restore CS0618
	private readonly TextArea TextPanel = new() { ReadOnly = true, Text = "This program hasn't been completed yet" };
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

		var layout = new Splitter() {
			Panel1 = GGPKTree,
			Panel1MinimumSize = 10,
			Panel2 = new Splitter() {
				Panel1 = BundleTree,
				Panel1MinimumSize = 10,
				Panel2 = TextPanel,
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

		async void OnLoadComplete(object? sender, EventArgs e) {
			LoadComplete -= OnLoadComplete;
			await Task.Yield();
			if (path is null || !File.Exists(path)) {
				using var ofd = new OpenFileDialog() {
					FileName = "Content.ggpk",
					Filters = {
						new("GGPK/Index File", ".ggpk", ".bin"),
						allFilesFilters
					}
				};
				if (ofd.ShowDialog(this) != DialogResult.Ok) {
					Close();
					return;
				}
				path = ofd.FileName;
			}

			if (!File.Exists(path))
				throw new FileNotFoundException(path);

			int failed;
			if (path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) {
				failed = await Task.Run(() => {
					Index = new(path, false); // false to parsePaths manually
					return Index.ParsePaths();
				});
				GGPKTree.DataStore = null;
				GGPKTree.Visible = false;
				GGPKTree.Enabled = false;
				layout.Panel1MinimumSize = 0;
				layout.Position = 0;
			} else {
				failed = await Task.Run(() => {
					try {
						var ggpk = new BundledGGPK(path, false); // false to parsePaths manually
						Ggpk = ggpk;
						Index = ggpk.Index;
						return Index.ParsePaths();
					} catch (FileNotFoundException ex) { // No _.index.bin
						Application.Instance.AsyncInvoke(() =>
							MessageBox.Show(this, ex.GetNameAndMessage(), "Warning", MessageBoxType.Warning));
						Ggpk = new GGPK(path);
						return 0;
					}
				});
				GGPKTree.DataStore = new GGPKDirectoryTreeItem(Ggpk!.Root, null, GGPKTree) {
					Expanded = true
				};
			}
			var buildTreeTask = Index is null || failed == Index.Files.Count ? Task.FromResult<BundleDirectoryTreeItem>(null!) :
				Task.Run(() => (BundleDirectoryTreeItem)Index.BuildTree(BundleDirectoryTreeItem.GetFuncCreateInstance(BundleTree), BundleFileTreeItem.CreateInstance, true));
			if (failed != 0)
				TextPanel.Text += $"\n\nWarning: There're {failed} files failed to parse the path, your ggpk file may be broken.";

			var menu = new ContextMenu(
				new ButtonMenuItem(OnExtractClicked) { Text = "Extract" },
				new ButtonMenuItem(OnReplaceClicked) { Text = "Replace" },
				new ButtonMenuItem(OnCopyPathClicked) { Text = "Copy Path" },
				new ButtonMenuItem(OnExportDdsClicked) { Text = "Export .dds to .png" }
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

			GGPKTree.DragEnter += OnDragEnter;
			GGPKTree.DragDrop += OnDragDrop;
			GGPKTree.AllowDrop = true;
			BundleTree.DragEnter += OnDragEnter;
			BundleTree.DragDrop += OnDragDrop;
			BundleTree.AllowDrop = true;

			var bundles = await buildTreeTask;
			if (bundles is not null)
				bundles.Expanded = true;
			BundleTree.DataStore = bundles;
		}
	}

	private void OnSelectionChanged(object? sender, EventArgs _) {
#pragma warning disable CS0618 // Obsolete
		var item = (sender as TreeView)?.SelectedItem;
#pragma warning restore CS0618
		if (item is null)
			return;

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
						else
							ImagePanel.Image = new Bitmap(fileItem.Read().ToArray());
						panel.Panel2 = ImagePanel;
						break;
					case FileTreeItem.DataFormat.DdsImage:
						imageName = Path.GetFileNameWithoutExtension(fileItem.Name);

						ReadOnlySpan<byte> data;
						if (fileItem is GGPKFileTreeItem g2)
							data = g2.Record.Read();
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

						ImagePanel.Image = GetDdsBitmap(data);
						panel.Panel2 = ImagePanel;
						break;
					case FileTreeItem.DataFormat.Dat:
					// TODO: LibDat3
					// 	panel.Panel2 = DatPanel;
					// 	break;
					default:
						TextPanel.Text = "";
						panel.Panel2 = TextPanel;
						// DatPanel.DataStore = null;
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
			Directory.CreateDirectory(Path.GetDirectoryName(sfd.FileName)!);
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
			var dir = Directory.CreateDirectory(Path.GetDirectoryName(sfd.FileName)!).FullName;
			MessageBox.Show(this, $"Extracted {di.Extract(dir)} files to\r\n{dir}", "Done", MessageBoxType.Information);
		}
	}

	private void OnReplaceClicked(object? sender, EventArgs _) {
		if (clickedItem is FileTreeItem fi) {
			var ext = "*" + Path.GetExtension(fi.Name);
			using var ofd = new OpenFileDialog() {
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
			using var ofd = new OpenFileDialog() {
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
		Clipboard.Instance.Text = clickedItem is DirectoryTreeItem di
			? di.GetPath()
			: ((FileTreeItem)clickedItem).GetPath();
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
		Directory.CreateDirectory(Path.GetDirectoryName(sfd.FileName)!);
		(ImagePanel.Image as Bitmap)!.Save(sfd.FileName, Eto.Drawing.ImageFormat.Png);
	}

	private void OnExportDdsClicked(object? sender, EventArgs _) {
		if (clickedItem is FileTreeItem fi) {
			if (fi.Format != FileTreeItem.DataFormat.DdsImage) {
				MessageBox.Show(this, "Selected file is not a dds image", "Error", MessageBoxType.Error);
				return;
			}
			var sfd = new SaveFileDialog() {
				FileName = Path.GetFileNameWithoutExtension(fi.Name) + ".png",
				Filters = {
					new("*.png", "*.png"),
					allFilesFilters
				}
			};
			if (sfd.ShowDialog(this) != DialogResult.Ok)
				return;
			Directory.CreateDirectory(Path.GetDirectoryName(sfd.FileName)!);
			GetDdsBitmap(fi.Read().Span).Save(sfd.FileName, Eto.Drawing.ImageFormat.Png);
			MessageBox.Show(this, $"Saved {sfd.FileName}", "Done", MessageBoxType.Information);
		} else if (clickedItem is DirectoryTreeItem di) {
			var sfd = new SaveFileDialog() {
				CheckFileExists = false,
				FileName = di.Name + ".dir",
				Filters = { allFilesFilters }
			};
			if (sfd.ShowDialog(this) != DialogResult.Ok)
				return;
			var dir = Path.Combine(Path.GetDirectoryName(sfd.FileName)!, di.Name);
			int failed = 0;
			var count = di.Extract((path, data) => {
				var filename = Path.GetFileNameWithoutExtension(path);
				path = Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, path))!).FullName;
				try {
					var bitmap = GetDdsBitmap(data.Span);
					bitmap.Save(Path.Combine(path, filename + ".png"), Eto.Drawing.ImageFormat.Png);
				} catch {
					Interlocked.Increment(ref failed);
				}
			}, ".dds");
			if (failed == 0)
				MessageBox.Show(this, $"Exported {count} files to\r\n{dir}", "Done", MessageBoxType.Information);
			else
				MessageBox.Show(this, $"Exported {count} files to\r\n{dir}\r\n{failed} files failed!", "Done", MessageBoxType.Warning);
		}
	}

	private static readonly FileFilter allFilesFilters = new("All Files", "*");

	private void OnDragEnter(object? sender, DragEventArgs e) {
		if (Index is not null && e.Data.ContainsUris)
			e.Effects = DragEffects.Copy;
	}

	private void OnDragDrop(object? sender, DragEventArgs e) {
		if (Index is null || !e.Data.ContainsUris)
			return;

		try {
			var uris = e.Data.Uris;
			if (uris.Length != 1)
				goto err;
			var uri = uris[0];
			if (!uri.IsFile)
				goto err;
			var path = uri.LocalPath;
			if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
				goto err;

			int count;
			using (var zip = ZipFile.OpenRead(path))
				count = sender == GGPKTree
					? GGPK.Replace((Ggpk ?? throw ThrowHelper.Create<InvalidOperationException>("GGPK replacing is not supported in Steam/Epic mode"))
						.Root, zip.Entries)
					: LibBundle3.Index.Replace(Index!, zip.Entries);
			MessageBox.Show(this, $"Replaced {count} files!", "Done", MessageBoxType.Information);
		} catch (Exception ex) {
			MessageBox.Show(this, ex.ToString(), "Error", MessageBoxType.Error);
		}

		return;
	err:
		MessageBox.Show(this, "Only a single zip file is allowed", "Error", MessageBoxType.Error);
		return;
	}

	private static unsafe Bitmap GetDdsBitmap(ReadOnlySpan<byte> data) {
		fixed (byte* p = data) {
			using var image = Dds.Create(new UnmanagedMemoryStream(p, data.Length), new(allocator: ArrayPoolAllocator.Instance));
			var format = image.Format switch {
				Pfim.ImageFormat.Rgba32 => PixelFormat.Format32bppRgba,
				Pfim.ImageFormat.Rgb24 => PixelFormat.Format24bppRgb,
				_ => throw ThrowHelper.Create<NotSupportedException>()
			};
			return new Bitmap(image.Width, image.Height, format, ToColors(image.Data, format));
		}

		static IEnumerable<Color> ToColors(byte[] data, PixelFormat format) {
			var argb = format == PixelFormat.Format32bppRgba;
			var bpp = argb ? 4 : 3;

			for (var i = 0; i < data.Length; i += bpp) {
				var pixel = MemoryMarshal.Read<int>(new(data, i, sizeof(int)));
				yield return argb ? Color.FromArgb(pixel) : Color.FromRgb(pixel);
			}
		}
	}
}