﻿using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Threading;
using System.Threading.Tasks;

using LibGGPK3.Records;

using SystemExtensions;
using SystemExtensions.Collections;
using SystemExtensions.Spans;

namespace LibGGPK3;

/// <summary>
/// Client to interact with the patch server.
/// </summary>
/// <remarks>
/// <para>Currently supports protocol version 6 only.</para>
/// <para>
/// Call <see cref="ConnectAsync(EndPoint)"/> before using other methods.<br />
/// Sample server endpoints are in <see cref="ServerEndPoints"/>.
/// </para>
/// <para>
/// All async methods will be executed sequentially, executing them simultaneously will only cause more waiting.
/// </para>
/// </remarks>
public class PatchClient : IDisposable {
	public static class ServerEndPoints {
		/// <summary>
		/// patch.pathofexile.com:12995
		/// </summary>
		public static readonly DnsEndPoint US = new("patch.pathofexile.com", 12995);
		/// <summary>
		/// patch.pathofexile.tw:12999
		/// </summary>
		public static readonly DnsEndPoint TW = new("patch.pathofexile.tw", 12999);
		/// <summary>
		/// patch.pathofexile2.com:13064
		/// </summary>
		public static readonly DnsEndPoint US2 = new("patch.pathofexile2.com", 13060);
		/// <summary>
		/// patch.pathofexile2.tw:13070
		/// </summary>
		public static readonly DnsEndPoint TW2 = new("patch.pathofexile2.tw", 13070);
	}

	protected readonly Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
	protected Task? lastRequest;

	public virtual byte ProtocolVersion => 6;
	public virtual bool Connected => CdnUrl is not null && socket.Connected;
	/// <summary>
	/// CDN URL to download patch files. Only available after <see cref="ConnectAsync(EndPoint)"/> is called and completed.
	/// </summary>
	public string? CdnUrl { get; protected set; }

	public virtual Task ConnectAsync(EndPoint server) {
		lock (socket) {
			if (lastRequest is not null && !lastRequest.IsCompleted)
				ThrowHelper.Throw<InvalidOperationException>("Another task is running. ConnectAsync must be the first method to be called.");
			return lastRequest = Core();
		}

		async Task Core() {
			await socket.ConnectAsync(server).ConfigureAwait(false);

			var array = ArrayPool<byte>.Shared.Rent(256);
			try {
				array[0] = 1; // Opcode
				array[1] = ProtocolVersion;
				await socket.SendAsync(new ArraySegment<byte>(array, 0, 2)).ConfigureAwait(false);

				var len = await socket.ReceiveAsync(array).ConfigureAwait(false);
				ParsePacket(new(array, 0, len));
			} finally {
				ArrayPool<byte>.Shared.Return(array);
			}

			void ParsePacket(ReadOnlySpan<byte> span) {
				if (span.ReadAndSlice<byte>() != 2) // Opcode
					ThrowInvalidOpcode();
				span = span.Slice(32); // 32 bytes empty
				var length = span.ReadAndSlice<ushort>();
				Utils.EnsureBigEndian(ref length);
				CdnUrl = new(MemoryMarshal.Cast<byte, char>(span)[..length]);
			}
		}
	}

	/// <param name="directoryPath"><see cref="TreeNode.GetPath()"/> without trailing slash</param>
	/// <param name="throwIfNotFound">Throw <see cref="ArgumentException"/> if the given directory not found</param>
	/// <returns>The entries of the given directory, or <see langword="null"/> if not found</returns>
	public virtual Task<EntryInfo[]?> QueryDirectoryAsync(string directoryPath, bool throwIfNotFound = true) {
		if (CdnUrl is null)
			ThrowHelper.Throw<InvalidOperationException>("You must call ConnectAsync first before using this instance");

		Task? toWait = null;
		lock (socket) {
			if (lastRequest is not null && !lastRequest.IsCompleted)
				toWait = lastRequest;
			var result = Core();
			lastRequest = result;
			return result;
		}

		async Task<EntryInfo[]?> Core(Task? _ = null) {
			if (toWait is not null)
				await toWait.ConfigureAwait(false);

			const int ResponseHeaderLength = sizeof(byte) + sizeof(uint) + sizeof(uint) + sizeof(uint); // Opcode + compressedLen + decompressedLen + compressedLen
			var len = sizeof(byte) + sizeof(ushort) + directoryPath.Length * sizeof(char);
			var a = new ArrayPoolRenter<byte>(Math.Max(ResponseHeaderLength, len));
			var array = a.Array;

			FillPacket(array);
			await socket.SendAsync(new ArraySegment<byte>(array, 0, len)).ConfigureAwait(false);

			len = await socket.ReceiveAsync(new ArraySegment<byte>(array, 0, ResponseHeaderLength)).ConfigureAwait(false);
			if (len == 0) {
				if (throwIfNotFound)
					ThrowHelper.Throw<ArgumentException>("Directory not found", nameof(directoryPath));
				return null;
			}
			int compressedLen;
			int decompressedLen;
			GetLength(new(array, 0, len));

			if (array.Length < compressedLen) {
				a.Resize(compressedLen);
				array = a.Array;
			}
			len = 0;
			do {
				len += await socket.ReceiveAsync(new ArraySegment<byte>(array, len, compressedLen - len)).ConfigureAwait(false);
			} while (len < compressedLen);
			return ParsePacket(new(array, 0, len));

			void FillPacket(Span<byte> span) {
				span.WriteAndSlice<byte>(3); // Opcode
				BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)directoryPath.Length);
				MemoryMarshal.Cast<char, byte>(directoryPath.AsSpan()).CopyTo(span.Slice(sizeof(ushort)));
			}

			void GetLength(ReadOnlySpan<byte> span) {
				if (span.ReadAndSlice<byte>() != 4) // Opcode
					ThrowInvalidOpcode();
				compressedLen = span.ReadAndSlice<int>();
				Utils.EnsureBigEndian(ref compressedLen);
				decompressedLen = MemoryMarshal.Read<int>(span);
				Utils.EnsureBigEndian(ref decompressedLen);
				// Skip second compressedLen
			}

			EntryInfo[] ParsePacket(ReadOnlySpan<byte> span) {
				var array = ArrayPool<byte>.Shared.Rent(decompressedLen);
				try {
					decompressedLen = LZ4.Decompress(span, array);
					var resultSpan = new ReadOnlySpan<byte>(array, 0, decompressedLen);
					a.Dispose();

					resultSpan = resultSpan.Slice(sizeof(int) + BinaryPrimitives.ReadInt32LittleEndian(resultSpan) * sizeof(char)); // Skip directoryName
					var count = resultSpan.ReadAndSlice<int>();
					Utils.EnsureLittleEndian(ref count);
					var result = new EntryInfo[count];
					for (var i = 0; i < count; i++) {
						var type = resultSpan.ReadAndSlice<byte>();
						var fileSize = resultSpan.ReadAndSlice<int>();
						if (type == 1)
							fileSize = -1;

						var nameLength = resultSpan.ReadAndSlice<int>();
						Utils.EnsureLittleEndian(ref nameLength);
						nameLength *= sizeof(char);
						var name = MemoryMarshal.Cast<byte, char>(resultSpan[..nameLength]).ToString();
						resultSpan = resultSpan.SliceUnchecked(nameLength);

						result[i] = new(fileSize, name, /*hash*/resultSpan.ReadAndSlice<Vector256<byte>>());
					}
					return result;
				} finally {
					ArrayPool<byte>.Shared.Return(array);
				}
			}
		}
	}

	public virtual Task<string> QueryPatchNotesUrlAsync() {
		if (CdnUrl is null)
			ThrowHelper.Throw<InvalidOperationException>("You must call ConnectAsync first before using this instance");

		Task? toWait = null;
		lock (socket) {
			if (lastRequest is not null && !lastRequest.IsCompleted)
				toWait = lastRequest;
			var result = Core();
			lastRequest = result;
			return result;
		}

		async Task<string> Core(Task? _ = null) {
			if (toWait is not null)
				await toWait;
			var array = ArrayPool<byte>.Shared.Rent(256);
			try {
				array[0] = 5; // Opcode
				await socket.SendAsync(new ArraySegment<byte>(array, 0, 1)).ConfigureAwait(false);

				var len = await socket.ReceiveAsync(array).ConfigureAwait(false);
				return ParsePacket(new(array, 0, len));

				string ParsePacket(ReadOnlySpan<byte> span) {
					if (span.ReadAndSlice<byte>() != 6) // Opcode
						ThrowInvalidOpcode();
					var length = span.ReadAndSlice<ushort>();
					Utils.EnsureBigEndian(ref length);
					return MemoryMarshal.Cast<byte, char>(span)[..length].ToString();
				}
			} finally {
				ArrayPool<byte>.Shared.Return(array);
			}
		}
	}

	public readonly struct EntryInfo(int fileSize, string name, Vector256<byte> hash) {
		public readonly int FileSize = fileSize;
		public readonly string Name = name;
		public readonly Vector256<byte> Hash = hash;
	}

	/// <summary>
	/// Update a <paramref name="node"/> in ggpk from the patch server recursively,
	/// if its <see cref="TreeNode.Hash"/> doesn't match or it is <see cref="GGPK.Root"/>.
	/// </summary>
	/// <returns>
	/// Total number of <see cref="FileRecord"/>s updated/added/removed.
	/// </returns>
	/// <remarks>
	/// <para>
	/// Updating <see cref="GGPK.Root"/> is equivalent to starting patching of the game.
	/// </para>
	/// <para>
	/// If you want to revert all changes done by this library,
	/// you must call <see cref="GGPK.EraseRootHash"/> first and pass <see cref="GGPK.Root"/> to <paramref name="node"/>.
	/// </para>
	/// </remarks>
	public virtual async Task<int> UpdateNodeAsync(TreeNode node, TimeSpan? timeoutEachFile = null, CancellationToken cancellationToken = default) {
		cancellationToken.ThrowIfCancellationRequested();
		node.Ggpk.RenewHashes();

		string path;
		if (node == node.Ggpk.Root) {
			path = string.Empty;
		} else {
			path = node.Parent!.GetPath().TrimEnd('/');
			if (node.Hash == (await QueryDirectoryAsync(path).ConfigureAwait(false))!.First(e => e.Name == node.Name).Hash)
				return 0; // Hash is matched, skip updating
			path = $"{path}/{node.Name}";
		}

		cancellationToken.ThrowIfCancellationRequested();
		var endPoint = socket.RemoteEndPoint!;
		using var http = new HttpClient(new SocketsHttpHandler { UseCookies = false }) { BaseAddress = new(CdnUrl!), Timeout = timeoutEachFile ?? Timeout.InfiniteTimeSpan };
		try {
			return await UpdateCore(node, path, node.Hash).ConfigureAwait(false);
		} finally {
			node.Ggpk.baseStream.Flush();
		}

		async Task<int> UpdateCore(TreeNode node, string path, Vector256<byte> hash) {
			Debug.WriteLine(path);
			if (node is FileRecord fr) {
				using var res = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
				fr.Write(await res.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false), hash);
				return 1;
			}

			IEnumerable<EntryInfo> entrieInfos;
			try {
				entrieInfos = (await QueryDirectoryAsync(path).ConfigureAwait(false))!;
			} catch (SocketException) {
				cancellationToken.ThrowIfCancellationRequested();
				// After download large files/directories from cdn, the connection of patch server may timeout here, so we retry once
				if (socket.Connected) {
					socket.Shutdown(SocketShutdown.Both);
					await socket.DisconnectAsync(true, default).ConfigureAwait(false);
				}
				await ConnectAsync(endPoint).ConfigureAwait(false);
				entrieInfos = (await QueryDirectoryAsync(path).ConfigureAwait(false))!;
			}
			cancellationToken.ThrowIfCancellationRequested();
			var root = path.Length == 0;
			if (root)
				entrieInfos = entrieInfos.Where(e => e.Name != "Redist" && e.Name != "signature.bin" && !e.Name.StartsWith("update.dat~")); // Special case files/directories placed outside ggpk
			var entries = entrieInfos.Where(e => !e.Name.EndsWith(".dll") && !e.Name.EndsWith(".exe")) // Special case files placed outside ggpk
				.Select(e => (Info: e, NameHash: TreeNode.GetNameHash(e.Name))).OrderBy(e => e.NameHash).ToArray();
			var dir = (node as DirectoryRecord)!;
			var count = 0;
			for (var i = 0; i < entries.Length; ++i) {
				var (Info, NameHash) = entries[i];
				TreeNode n;
				if (i >= dir.Count)
					n = AddNode(dir, Info);
				else {
					n = dir[i];
					while (n.NameHash < NameHash) {
						n.Remove();
						n = dir[i];
						++count;
					}

					if (n.NameHash > NameHash)
						n = AddNode(dir, Info);
					else if (n.Hash == Info.Hash)
						continue;
				}
				count += await UpdateCore(n, root ? Info.Name : $"{path}/{Info.Name}", Info.Hash).ConfigureAwait(false);
				Debug.Assert(n is DirectoryRecord && Info.FileSize == -1 || n is FileRecord f && f.DataLength == Info.FileSize, Info.FileSize.ToString());
			}

			if (dir.Count == 0)
				dir.Remove();
			else if (count != 0)
				dir.RenewHash(hash);
			return count;

			static TreeNode AddNode(DirectoryRecord dir, EntryInfo e) {
				if (e.FileSize == -1) {
					dir.AddDirectory(e.Name, out var result);
					return result;
				} else {
					dir.AddFile(e.Name, out var result, e.FileSize);
					return result;
				}
			}
		}
	}

	/// <exception cref="InvalidDataException"/>
	[DoesNotReturn, DebuggerNonUserCode]
	protected static void ThrowInvalidOpcode() {
		throw new InvalidDataException("Invalid response opcode");
	}

	public virtual void Dispose() {
		GC.SuppressFinalize(this);
		socket.Dispose();
	}

	public static async Task<string> GetPatchCdnUrlAsync(EndPoint server) {
		using var client = new PatchClient();
		await client.ConnectAsync(server).ConfigureAwait(false);
		return client.CdnUrl!;
	}
}