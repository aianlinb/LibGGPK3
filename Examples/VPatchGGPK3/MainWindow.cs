using Eto.Drawing;
using Eto.Forms;
using LibGGPK3;
using LibGGPK3.Records;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading.Tasks;

namespace VPatchGGPK3 {
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
			handler.SslOptions.EnabledSslProtocols |= SslProtocols.Tls | SslProtocols.Tls11
												| SslProtocols.Tls12 | SslProtocols.Tls13;
			http = new(handler) { DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };

			var version = Assembly.GetExecutingAssembly().GetName().Version!;
			Title = $"VPatchGGPK3 (v{version.Major}.{version.Minor}.{version.Build})";
			ClientSize = new(450, 280);
			var layout = new DynamicLayout();
			layout.BeginVertical(new Padding(5)).Spacing = new Size(5,10);
			layout.AddRow(new Label() {
				Text = "Content.ggpk:",
				VerticalAlignment = VerticalAlignment.Center
			}, ggpkPath = new TextBox());
			layout.BeginHorizontal();
			layout.BeginVertical();
			layout.Add(new Button((_, _) => {
				var ofd = new OpenFileDialog() {
					Title = "選擇 Content.ggpk"
				};
				ofd.Filters.Add(new FileFilter("GGPK File", ".ggpk"));
				if (!string.IsNullOrWhiteSpace(ggpkPath.Text))
					ofd.FileName = ggpkPath.Text;
				if (ofd.ShowDialog(this) == DialogResult.Ok)
					ggpkPath.Text = ofd.FileName;
			}) { Text = "瀏覽", Height = 35 });
			layout.BeginCentered();
			layout.AddSeparateRow(new Padding(7,0,0,0), controls: new Control[] {
				tw = new RadioButton() {
					Text = "tw",
					Size = new Size(40, 30),
					Checked = true
				}, new RadioButton(tw) {
					Text = "cn",
					Size = new Size(40, 30)
				}
			});
			layout.AddSeparateRow(new Padding(0, 5), controls: new Control[] { new Label() {
				Text = "PIN: ",
				VerticalAlignment = VerticalAlignment.Center
			}, pin = new TextBox() { Width = 50 } });
			layout.EndCentered();
			layout.Add(new Button(OnButtonClick) { Text = "套用中文化", Height = 35 });
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
				Text = "Copyright © 2022-2023 aianlinb",
				TextAlignment = TextAlignment.Right,
				VerticalAlignment = VerticalAlignment.Bottom,
				Size = new Size(100, 20)
			});
			layout.EndVertical();
			layout.EndHorizontal();
			layout.EndVertical();
			LoadComplete += OnLoadComplete;
			Content = layout;
		}

		private void OnLoadComplete(object? sender, EventArgs e) {
			var path = AppContext.BaseDirectory;
			if (string.IsNullOrEmpty(path))
				path = Path.GetDirectoryName(Assembly.GetExecutingAssembly()?.Location ?? Assembly.GetEntryAssembly()?.Location ?? Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName);
			if (!string.IsNullOrEmpty(path))
				Environment.CurrentDirectory = path;
			try {
				var args = Environment.GetCommandLineArgs();
				if (args.Length > 1 && File.Exists(Path.GetFullPath(args[1])))
					ggpkPath.Text = args[1];
				else if (File.Exists("VPatchGGPK3.txt"))
					ggpkPath.Text = File.ReadAllText("VPatchGGPK3.txt");
				else if (OperatingSystem.IsMacOS())
					ggpkPath.Text = "~/Library/Application Support/Path of Exile/Content.ggpk";
				else if (OperatingSystem.IsWindows())
					ggpkPath.Text = @"C:\PathOfExile\Content.ggpk";
			} catch {
				if (OperatingSystem.IsMacOS())
					ggpkPath.Text = "~/Library/Application Support/Path of Exile/Content.ggpk";
				else if (OperatingSystem.IsWindows())
					ggpkPath.Text = @"C:\PathOfExile\Content.ggpk";
			}
		}

		private async void OnButtonClick(object? sender, EventArgs e) {
			var path = ggpkPath.Text;
			try {
				File.WriteAllText("VPatchGGPK3.txt", path);
			} catch { }

			try {
				output.Append("Getting patch information . . .\r\n", true);
				var json = (await JsonDocument.ParseAsync(await http.GetStreamAsync(tw.Checked ? "https://poedb.tw/fg/pin_tw.json" : "https://poedb.tw/fg/pin_cn.json"))).RootElement;
				var url = await Task.Run(() => Extensions.GetPatchServer());
				var officialVersion = url[(url.LastIndexOf('/', url.Length - 2) + 1)..^1];
				if (json.GetProperty("version").GetString()! != officialVersion) {
					MessageBox.Show(this, "Server Version not match Patch Version\r\n編年史中文化更新中，請稍待", "Error", MessageBoxType.Error);
					output.Append("\r\nFailed!\r\n", true);
					return;
				}
				if (json.GetProperty("pin").GetString()! != pin.Text) {
					MessageBox.Show(this, "無效的PIN碼\r\n請詳閱: https://poedb.tw/chinese", "Error", MessageBoxType.Error);
					output.Append("\r\nFailed!\r\n", true);
					return;
				}

				output.Append("Reading ggpk: " + path + "\r\n", true);
				var ggpk = await Task.Run(() => new GGPK(path));
				var md5 = json.GetProperty("md5").GetString()!;
				output.Append("Downloading patch file . . .\r\n", true);
				var zip = new ZipArchive(await http.GetStreamAsync("https://poedb.tw/fg/" + md5 + ".zip"));
				// if (md5 != System.Security.Cryptography.MD5.HashData(...)) ...
				foreach (var entry in zip.Entries) {
					if (entry.FullName.EndsWith('/'))
						continue;
					if (ggpk.FindNode(entry.FullName) is not FileRecord fr) {
						output.Append("Unable to find in ggpk: " + entry.FullName + "\r\n", true);
						continue;
					}
					await Task.Run(() => {
						var fs = entry.Open();
						var b = new byte[entry.Length];
						for (var l = 0; l < b.Length;)
							l += fs.Read(b, l, b.Length - l);
						fs.Close();
						fr.ReplaceContent(b);
					});
					output.Append("Replaced: " + entry.FullName + "\r\n", true);
				}
				output.Append("\r\nDone!\r\n", true);
				output.Append("中文化完成!\r\n", true);
				ggpk.Dispose();
			} catch (Exception ex) {
				MessageBox.Show(this, ex.ToString(), "Error", MessageBoxType.Error);
			}
		}
	}
}