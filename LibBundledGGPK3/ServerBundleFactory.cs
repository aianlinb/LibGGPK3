using System;
using System.IO;
using System.Net.Http;

using LibBundle3;
using LibBundle3.Records;

using SystemExtensions;

namespace LibBundledGGPK3;
/// <summary>
/// <see cref="DriveBundleFactory"/> but downloads the bundle from the patch server if it doesn't exist.
/// </summary>
/// <remarks>
/// Remember to call <see cref="Dispose"/> when done.
/// </remarks>
/// <param name="baseDirectory">Path on drive to save the downloaded bundles</param>
/// <param name="patchCdnUrl">Can get from <see cref="LibGGPK3.PatchClient.GetPatchCdnUrl"/></param>
public class ServerBundleFactory(string baseDirectory, string patchCdnUrl) : DriveBundleFactory(baseDirectory), IDisposable {
	public Uri CdnUrl => http.BaseAddress!;
	protected readonly HttpClient http = new(new SocketsHttpHandler() { UseCookies = false }) {
		BaseAddress = new(patchCdnUrl),
		DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
	};

	public override Bundle GetBundle(BundleRecord record) {
		var rp = record.Path;
		var path = BaseDirectory + rp;
		if (File.Exists(path))
			return new(path, record); // base.GetBundle(record)

		using var res = http.Send(new(HttpMethod.Get, rp));
		if (!res.IsSuccessStatusCode)
			throw ThrowHelper.Create<HttpRequestException>($"Failed to download bundle ({res.StatusCode}): {rp}");
		var fs = File.Create(path);
		using (var s = res.Content.ReadAsStream())
			s.CopyTo(fs);
		return new(fs, false, record);
	}

	public virtual void Dispose() {
		GC.SuppressFinalize(this);
		http.Dispose();
	}
}