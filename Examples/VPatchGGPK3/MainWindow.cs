using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;

using Eto.Drawing;
using Eto.Forms;

using LibBundledGGPK3;

using LibGGPK3;
using SystemExtensions;

namespace VPatchGGPK3;
public class MainWindow : Form {
	private readonly HttpClient http;

	private readonly RadioButton tw;
	private readonly TextBox ggpkPath;
	private readonly TextBox pin;
	private readonly TextArea output;

	public MainWindow() {
#if Mac
		static void closed(object? sender, EventArgs e) => Application.Instance.Quit();
		Closed += closed;
		Application.Instance.Terminating += (s, e) => Closed -= closed;
#endif
		var handler = new SocketsHttpHandler() { UseCookies = false };
		handler.SslOptions.CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
		handler.SslOptions.RemoteCertificateValidationCallback = delegate { return true; };
		http = new(handler) { DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };

		var version = Assembly.GetExecutingAssembly().GetName().Version!;
		if (version.Revision != 0)
			Title = $"VPatchGGPK3 (v{version.Major}.{version.Minor}.{version.Build}.{version.Revision})";
		else
			Title = $"VPatchGGPK3 (v{version.Major}.{version.Minor}.{version.Build})";
		ClientSize = new(650, 400);
		var layout = new DynamicLayout();
		layout.BeginVertical(new Padding(5)).Spacing = new Size(5, 10);
		layout.AddRow(new Label() {
			Text = "Content.ggpk:",
			VerticalAlignment = VerticalAlignment.Center
		}, ggpkPath = new TextBox());
		layout.BeginHorizontal();
		layout.BeginVertical();
		layout.Add(new Button((_, _) => {
			using var ofd = new OpenFileDialog() {
				Title = "選擇 Content.ggpk/_.index.bin",
				Filters = {
					new("GGPK/Index File", ".ggpk", ".bin")
				}
			};
			if (File.Exists(ggpkPath.Text))
				ofd.FileName = ggpkPath.Text;
			if (ofd.ShowDialog(this) == DialogResult.Ok)
				ggpkPath.Text = ofd.FileName;
		}) { Text = "瀏覽", Height = 35 });
		layout.BeginCentered();
		layout.AddSeparateRow(new Padding(7, 0, 0, 0), controls: [
			tw = new RadioButton() {
				Text = "tw",
				Size = new Size(40, 30),
				Checked = true
			}, new RadioButton(tw) {
				Text = "cn",
				Size = new Size(40, 30)
			}
		]);
		layout.AddSeparateRow(new Padding(0, 5), controls: [
			new Label() {
				Text = "PIN: ",
				VerticalAlignment = VerticalAlignment.Center
			},
			pin = new TextBox() { Width = 50 }
		]);
		pin.KeyDown += (_, e) => {
			if (e.Key == Keys.Enter)
				OnButtonClick(pin, EventArgs.Empty);
		};
		layout.EndCentered();
		layout.Add(new Button(OnButtonClick) { Text = "套用中文化", Height = 35 });
		layout.AddSeparateRow(new Padding(0, 5), controls: [new Button(OnZipButtonClick) { Text = "套用 Zip", Height = 25 }]);
		layout.AddSeparateRow(new Padding(0, 10), controls: [new Button(OnRestoreClick) { Text = "還原所有修改", Height = 30 }]);
		layout.AddSpace();
		var link = new LinkButton() {
			Text = "poedb.tw",
			Height = 20
		};
		link.Click += (_, _) => Process.Start(new ProcessStartInfo("https://poedb.tw/chinese") { UseShellExecute = true });
		layout.AddCentered(link);
		layout.Add(new Button((_, _) => Close()) { Text = "關閉", Height = 35 });
		layout.EndBeginVertical();
		layout.Add(output = new TextArea() {
			ReadOnly = true
		}, null, true);
		layout.Add(new Label() {
			Text = "Copyright © 2022-2024 aianlinb",
			TextAlignment = TextAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			Size = new Size(100, 20)
		});
		layout.EndVertical();
		layout.EndHorizontal();
		layout.EndVertical();
		LoadComplete += OnLoadComplete;
		Content = layout;

		void OnLoadComplete(object? sender, EventArgs e) {
			LoadComplete -= OnLoadComplete;
			var path = AppContext.BaseDirectory;
			if (string.IsNullOrEmpty(path))
				path = Path.GetDirectoryName(Environment.ProcessPath);
			if (!string.IsNullOrEmpty(path))
				Environment.CurrentDirectory = path;

			var args = Environment.GetCommandLineArgs();
			if (args.Length > 1 && TrySetPath(Path.GetFullPath(args[1])))
				return;

			var txt = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
				Environment.SpecialFolderOption.DoNotVerify) + "/LibGGPK3/VPatchGGPK3.txt";
			if (File.Exists(txt)) {
				try {
					if (TrySetPath(File.ReadAllText(txt)))
						return;
				} catch { /* No permission */ }
			}

			if (OperatingSystem.IsMacOS() && (TrySetPath("~/Library/Application Support/Path of Exile/Content.ggpk", true)
				|| TrySetPath("~/Library/Application Support/Steam/steamapps/common/Path of Exile/Bundles2/_.index.bin", true)))
				return;
			if (OperatingSystem.IsWindows() && (TrySetPath(@"C:\Program Files (x86)\Grinding Gear Games\Path of Exile\Content.ggpk")
				|| TrySetPath(@"C:\Program Files (x86)\Steam\steamapps\common\Path of Exile\Bundles2\_.index.bin")))
				return;
			if (OperatingSystem.IsLinux())
				TrySetPath("~/.steam/steam/steamapps/common/Path of Exile/Bundles2/_.index.bin", true);

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			bool TrySetPath(string path, bool expand = false) {
				if (File.Exists(expand ? Utils.ExpandPath(path) : path)) {
					ggpkPath.Text = path;
					return true;
				}
				return false;
			}
		}
	}

	private async void OnButtonClick(object? sender, EventArgs e) {
		var path = await VerifyPath();
		if (path is null)
			return;

		try {
			output.Append("Getting patch information . . .\n", true);
			using var jd = await JsonDocument.ParseAsync(await http.GetStreamAsync(tw.Checked ? "https://poedb.tw/fg/pin_tw.json" : "https://poedb.tw/fg/pin_cn.json"));
			var json = jd.RootElement;
			var url = await PatchClient.GetPatchCdnUrlAsync(PatchClient.ServerEndPoints.US);
			var officialVersion = url[(url.LastIndexOf('/', url.Length - 2) + 1)..^1];
			if (json.GetProperty("version").GetString()! != officialVersion) {
				MessageBox.Show(this, "Server Version not match Patch Version\n編年史中文化更新中，請稍後再嘗試", "Error", MessageBoxType.Error);
				output.Append("\nVersion not matched!\n", true);
				return;
			}
			if (json.GetProperty("pin").GetString()! != pin.Text) {
				MessageBox.Show(this, "無效的PIN碼\n請詳閱: https://poedb.tw/tw/chinese\n或: https://poedb.tw/cn/chinese", "Error", MessageBoxType.Error);
				output.Append("\nPIN verification failed!\n", true);
				return;
			}

			var md5 = json.GetProperty("md5").GetString()!;
			output.Append("Downloading patch file . . .\n", true);
			var b = await http.GetByteArrayAsync("https://poedb.tw/fg/" + md5 + ".zip");
			if (!md5.Equals(Convert.ToHexString(System.Security.Cryptography.MD5.HashData(b)), StringComparison.OrdinalIgnoreCase)) {
				MessageBox.Show(this, "下載檔案的MD5校驗失敗，請檢查網路環境後再重試", "Error", MessageBoxType.Error);
				output.Append("\nMD5 verification failed!\n", true);
				return;
			}
			Apply(path, new ZipArchive(new MemoryStream(b)));
		} catch (Exception ex) {
			MessageBox.Show(this, ex.ToString(), "Error", MessageBoxType.Error);
			output.Append("Error!\n", true);
		}
	}

	private async void OnZipButtonClick(object? sender, EventArgs e) {
		var path = await VerifyPath();
		if (path is null)
			return;

		MessageBox.Show(this, "使用zip方式中文化將不會驗證遊戲版本和檔案完整性!\n請確認已先將遊戲和zip更新到最新版本，否則將導致遊戲崩潰!",
			"Warning", MessageBoxButtons.OK, MessageBoxType.Warning);
		Apply(path);
	}

	private async Task<string?> VerifyPath() {
		var path = ggpkPath.Text;
		if (!File.Exists(path)) {
			MessageBox.Show(this, "檔案不存在! 請先選擇正確的ggpk/index路徑", "Error", MessageBoxType.Error);
			return null;
		}

		try {
			var dir = Directory.CreateDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
			Environment.SpecialFolderOption.DoNotVerify) + "/LibGGPK3").FullName;
			await File.WriteAllTextAsync(dir + "/VPatchGGPK3.txt", path);
		} catch { /*ignore*/ }

		return path;
	}

	private async void Apply(string path, ZipArchive? zip = null) {
		if (zip is null) {
			using var ofd = new OpenFileDialog() {
				Filters = { new("Zip File", ".zip") }
			};
			if (ofd.ShowDialog(this) != DialogResult.Ok)
				return;
			zip = ZipFile.OpenRead(ofd.FileName);
		}

		BundledGGPK? ggpk = null;
		LibBundle3.Index index = null!;
		try {
			output.Append($"Reading ggpk/index: {path}\n", true);
			if (path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) {
				index = await Task.Run(() => new LibBundle3.Index(path));
			} else {
				ggpk = await Task.Run(() => new BundledGGPK(path, false));
				index = ggpk.Index;
			}

			var total = zip.Entries.Count(e => !e.FullName.EndsWith('/')/* !dir */);
			output.Append($"Applying {total} files . . .\n", true);
			await Task.Run(() => {
				if (zip.Entries.Any(e => e.FullName.Equals("Bundles2/_.index.bin", StringComparison.OrdinalIgnoreCase))) {
					if (ggpk is null) {
						zip.ExtractToDirectory(Path.GetDirectoryName(Path.GetDirectoryName(path))!, true);
						total = 0;
					} else {
						total -= GGPK.Replace(ggpk.Root, zip.Entries, (fr, p, added) => {
							Application.Instance.Invoke(() => output.Append($"{(added ? "Added: " : "Replaced: ")}{p}\n"));
							return false;
						}, allowAdd: true);
					}
				} else {
					total -= LibBundle3.Index.Replace(index, zip.Entries, (fr, p) => {
						Application.Instance.Invoke(() => output.Append($"Replaced: {p}\n"));
						return false;
					});
				}
			});
			output.Append("\nAll finished\n", true);
			if (total > 0)
				output.Append($"Error: {total} files failed to add/replace!\n", true);
			else
				output.Append("中文化完成!\n", true);
		} finally {
			zip.Dispose();
			ggpk?.Dispose();
			index?.Dispose();
		}
	}

	private async void OnRestoreClick(object? sender, EventArgs e) {
		var path = await VerifyPath();
		if (path is null)
			return;

		if (path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) {
			MessageBox.Show(this, "還原不支援Steam/Epic版本，請使用各平台驗證遊戲檔案的功能", "Error", MessageBoxButtons.OK, MessageBoxType.Error);
			return;
		} else {
			output.Append($"Reading ggpk: {path}\n", true);
			using var ggpk = await Task.Run(() => new GGPK(path));
			ggpk.EraseRootHash();
			if (ggpk.Root.TryFindNode("Bundles2/_.index.bin", out var node)) {
				node.Remove();
				output.Append("Removed: Bundles2/_.index.bin\n", true);
			}
			else
				output.Append("Warning: Cannot find Bundles2/_.index.bin in GGPK!\n", true);
		}
		output.Append($"Done!\n\n", true);
		output.ScrollToEnd();
		MessageBox.Show(this, "已移除_.index.bin，請開啟遊戲讓它更新以完成還原", "Done", MessageBoxButtons.OK, MessageBoxType.Information);
		Close();
	}
}