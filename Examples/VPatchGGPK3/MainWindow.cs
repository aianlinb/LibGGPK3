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
using System.Text.Json;

namespace VPatchGGPK3 {
	public class MainWindow : Form {
		private readonly HttpClient http = new(new HttpClientHandler() {
			UseCookies = false,
			CheckCertificateRevocationList = false,
			ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
		});

		private readonly RadioButton tw;
		private readonly TextBox ggpkPath;
		private readonly TextBox pin;
		private readonly TextArea output;
		public MainWindow() {
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
			layout.AddSeparateRow(new Padding(8,0,0,0), controls: new Control[] {
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
				Text = "PIN:",
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
				Text = "Copyright © 2022 aianlinb",
				TextAlignment = TextAlignment.Right,
				VerticalAlignment = VerticalAlignment.Bottom,
				Size = new Size(100, 20)
			});
			layout.EndVertical();
			layout.EndHorizontal();
			layout.EndVertical();
			LoadComplete += OnLoadComplete;
			Content = layout;
#if MAC
			Closed += OnClosed;
			Application.Instance.Terminating += (s, e) => Closed -= OnClosed;
#endif
		}

		private void OnClosed(object? sender, EventArgs e) {
			Application.Instance.Quit();
		}

		private void OnLoadComplete(object? sender, EventArgs _) {
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
			} catch {
				if (OperatingSystem.IsMacOS())
					ggpkPath.Text = "~/Library/Application Support/Path of Exile/Content.ggpk";
			}
		}

		private async void OnButtonClick(object? sender, EventArgs _) {
			try {
				File.WriteAllText("VPatchGGPK3.txt", ggpkPath.Text);
			} catch { }

			try {
				var json = (await JsonDocument.ParseAsync(await http.GetStreamAsync(tw.Checked ? "https://poedb.tw/fg/pin_tw.json" : "https://poedb.tw/fg/pin_cn.json"))).RootElement;
				var url = await Extensions.GetPatchServer();
				var officialVersion = url[(url.LastIndexOf('/', url.Length - 2) + 1)..^1];
				if (json.GetProperty("version").GetString()! != officialVersion) {
					MessageBox.Show(this, "Server Version not match Patch Version\r\n編年史中文化更新中，請稍待", "Error", MessageBoxType.Error);
					return;
				}
				if (json.GetProperty("pin").GetString()! != pin.Text) {
					MessageBox.Show(this, "無效的PIN碼\r\n請詳閱: https://poedb.tw/chinese", "Error", MessageBoxType.Error);
					return;
				}
				var md5 = json.GetProperty("md5").GetString()!;

				var ggpk = new GGPK(ggpkPath.Text);
				var zip = new ZipArchive(await http.GetStreamAsync("https://poedb.tw/fg/" + md5 + ".zip"));
				foreach (var e in zip.Entries) {
					if (e.FullName.EndsWith('/'))
						continue;
					if (ggpk.FindNode(e.FullName) is not FileRecord fr) {
						output.Append("Unable to find in ggpk: " + e.FullName + "\r\n", true);
						continue;
					}
					var fs = e.Open();
					var b = new byte[e.Length];
					for (var l = 0; l < b.Length;)
						l += fs.Read(b, l, b.Length - l);
					fs.Close();
					fr.ReplaceContent(b);
					output.Append("Replaced: " + e.FullName + "\r\n", true);
				}
				ggpk.Dispose();
				output.Append("\r\nDone!\r\n", true);
			} catch (Exception ex) {
				MessageBox.Show(this, ex.ToString(), "Error", MessageBoxType.Error);
			}
		}
	}
}