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
		private readonly HttpClient http = new HttpClient(new HttpClientHandler() {
			UseCookies = false,
			CheckCertificateRevocationList = false,
			ServerCertificateCustomValidationCallback = (_, _, _, _) => true
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
			layout.Add(new Button(async (_, _) => {
				try {
					File.WriteAllText("VPatchGGPK3.txt", ggpkPath.Text);
				} catch (Exception ex) {
					output!.Append("Warning: " + ex.Message);
				}
				if (!File.Exists(ggpkPath.Text)) {
					MessageBox.Show(this, "找不到檔案: " + ggpkPath.Text, "Error", MessageBoxType.Error);
					return;
				}

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
						output!.Append("Unable to find in ggpk: " + e.FullName + "\r\n", true);
						continue;
					}
					var f = e.Open();
					var b = new byte[e.Length];
					f.Read(b, 0, b.Length);
					f.Close();
					fr.ReplaceContent(b);
					output!.Append("Replaced: " + e.FullName + "\r\n", true);
				}
				output!.Append("\r\nDone!\r\n", true);
			}) { Text = "套用中文化", Height = 35 });
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
		}

		private void OnLoadComplete(object? sender, EventArgs e) {
			Environment.CurrentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location ?? Assembly.GetCallingAssembly().Location ?? Environment.ProcessPath) ?? Environment.CurrentDirectory;
			if (File.Exists("VPatchGGPK3.txt"))
				ggpkPath.Text = File.ReadAllText("VPatchGGPK3.txt");
		}
	}
}