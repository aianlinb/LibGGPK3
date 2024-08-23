using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using LibBundle3.Nodes;
using LibBundle3.Records;

using SystemExtensions;
using SystemExtensions.Collections;
using SystemExtensions.Spans;
using SystemExtensions.Streams;

[module: SkipLocalsInit]

namespace LibBundle3;

/// <summary>
/// Class to handle the _.index.bin file.
/// </summary>
public class Index : IDisposable {
	protected internal readonly IBundleFactory bundleFactory;
	/// <summary>
	/// <see cref="Bundle"/> instance of "_.index.bin"
	/// </summary>
	protected readonly Bundle baseBundle;
	/// <summary>
	/// Data for <see cref="ParsePaths"/>
	/// </summary>
	protected readonly byte[] directoryBundleData;

	protected BundleRecord[] _Bundles;
	protected internal readonly DirectoryRecord[] _Directories;
	protected readonly Dictionary<ulong, FileRecord> _Files;

	/// <summary>
	/// Bundles ceated by this library for writing modfied files.
	/// </summary>
	private readonly List<BundleRecord> CustomBundles = [];

	internal Bundle? _BundleToWrite;
	internal MemoryStream? _BundleStreamToWrite;
	internal readonly WeakReference<MemoryStream> WR_BundleStreamToWrite = new(null!);

	public virtual ReadOnlyMemory<BundleRecord> Bundles => _Bundles;
	/// <summary>
	/// Files with their <see cref="FileRecord.PathHash"/> as key.
	/// </summary>
	public virtual ReadOnlyDictionary<ulong, FileRecord> Files => new(_Files);

	/// <summary>
	/// Size to limit each bundle when writing, default to 200MiB
	/// </summary>
	public virtual int MaxBundleSize { get; set; } = 200 * 1024 * 1024;

	protected DirectoryNode? _Root;

#pragma warning disable CS1734
    /// <summary>
    /// Root node of the tree (This will call <see cref="BuildTree"/> with default implementation when first calling).
    /// You can also implement your custom class and use <see cref="BuildTree"/> instead of using this.
	/// <para>This will throw when any file have a null path (See <see cref="ParsePaths"/>)</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="ParsePaths"/> haven't been called</exception>
    /// <exception cref="NullReferenceException">Thrown when a file has null path. See <paramref name="ignoreNullPath"/> of <see cref="ParsePaths"/>.</exception>
#pragma warning restore CS1734
    public virtual DirectoryNode Root => _Root ??= (DirectoryNode)BuildTree(DirectoryNode.CreateInstance, FileNode.CreateInstance);

    public delegate IDirectoryNode CreateDirectoryInstance(string name, IDirectoryNode? parent);
	public delegate IFileNode CreateFileInstance(FileRecord record, IDirectoryNode parent);
	/// <summary>
	/// Build a tree to represent the file and directory structure in bundles
	/// </summary>
	/// <param name="createDirectory">Function to create a instance of <see cref="IDirectoryNode"/></param>
	/// <param name="createFile">Function to create a instance of <see cref="IFileNode"/></param>
	/// <param name="ignoreNullPath">Whether to ignore files with <see cref="FileRecord.Path"/> as <see langword="null"/> instead of throwing.
	/// This happens when <see cref="ParsePaths"/> has not been called or failed to parse some paths (returns not 0).
	/// <para>The ignored files won't appear in the returned tree</para></param>
	/// <returns>The root node of the built tree</returns>
	/// <remarks>
	/// You can implement your custom class and call this, or just use the default implementation by calling <see cref="Root"/>.
	/// </remarks>
	/// <exception cref="InvalidOperationException">Thrown when <see cref="ParsePaths"/> haven't been called.</exception>
	/// <exception cref="NullReferenceException">Thrown when a file has null path. See <paramref name="ignoreNullPath"/>.</exception>
	public virtual IDirectoryNode BuildTree(CreateDirectoryInstance createDirectory, CreateFileInstance createFile, bool ignoreNullPath = false) {
		EnsureNotDisposed();
		if (!ignoreNullPath && !pathsParsed)
			ThrowHelper.Throw<InvalidOperationException>("ParsePaths() must be called before building the tree");
		var root = createDirectory("", null);
		foreach (var f in _Files.Values.OrderBy(f => f.Path)) {
			if (string.IsNullOrEmpty(f.Path)) {
				if (!ignoreNullPath)
					ThrowHelper.Throw<NullReferenceException>("A file has null or empty path, the Index may be broken");
				continue;
			}
			var splittedPath = f.Path.AsSpan().Split('/');
			var parent = root;
			if (splittedPath.MoveNext())
				while (true) {
					var name = splittedPath.Current;
					if (!splittedPath.MoveNext()) // Last one is the file name
						break;
					if (parent.Children.Count <= 0 || parent.Children[^1] is not IDirectoryNode dr || !name.SequenceEqual(dr.Name))
						parent.Children.Add(dr = createDirectory(name.ToString(), parent));
					parent = dr;
				}
			parent.Children.Add(createFile(f, parent));
		}
		return root;
	}

	/// <summary>
	/// Initialize with a _.index.bin file on disk. (For Steam/Epic version)
	/// </summary>
	/// <param name="filePath">Path to _.index.bin on disk</param>
	/// <param name="parsePaths">
	/// Whether to call <see cref="ParsePaths"/> automatically.
	/// <see langword="false"/> to speed up reading, but all <see cref="FileRecord.Path"/> in each of <see cref="Files"/> will be <see langword="null"/>,
	/// and <see cref="Root"/> and <see cref="BuildTree"/> will be unable to use until you call <see cref="ParsePaths"/> manually.
	/// </param>
	/// <param name="bundleFactory">Factory to handle .bin files of <see cref="Bundle"/></param>
	/// <exception cref="FileNotFoundException" />
	public Index(string filePath, bool parsePaths = true, IBundleFactory? bundleFactory = null) : this(
			  File.Open(filePath = Utils.ExpandPath(filePath), FileMode.Open, FileAccess.ReadWrite, FileShare.Read),
			  false,
			  parsePaths,
			  bundleFactory ?? new DriveBundleFactory(Path.GetDirectoryName(Path.GetFullPath(filePath))!)
		) { }

	/// <summary>
	/// Initialize with <paramref name="stream"/>.
	/// </summary>
	/// <param name="stream">Stream of the _.index.bin file</param>
	/// <param name="leaveOpen">If false, close the <paramref name="stream"/> when this instance is disposed</param>
	/// <param name="parsePaths">
	/// Whether to call <see cref="ParsePaths"/> automatically.
	/// <see langword="false"/> to speed up reading, but all <see cref="FileRecord.Path"/> in each of <see cref="Files"/> will be <see langword="null"/>,
	/// and <see cref="Root"/> and <see cref="BuildTree"/> will be unable to use until you call <see cref="ParsePaths"/> manually.
	/// </param>
	/// <param name="bundleFactory">Factory to handle .bin files of <see cref="Bundle"/></param>
	/// <remarks>
	/// For Steam/Epic version, use <see cref="Index(string, bool, IBundleFactory?)"/> instead,
	/// or you must set <see cref="Environment.CurrentDirectory"/> to the directory where the _.index.bin file is before calling this constructor.
	/// </remarks>
	public unsafe Index(Stream stream, bool leaveOpen = false, bool parsePaths = true, IBundleFactory? bundleFactory = null) {
		ArgumentNullException.ThrowIfNull(stream);
		baseBundle = new(stream, leaveOpen);
		this.bundleFactory = bundleFactory ?? new DriveBundleFactory(string.Empty);
		var data = baseBundle.ReadWithoutCache();
		fixed (byte* p = data) {
			var ptr = (int*)p;

			var bundleCount = *ptr++;
			_Bundles = new BundleRecord[bundleCount];
			for (var i = 0; i < bundleCount; i++) {
				var pathLength = *ptr++;
				var path = new string((sbyte*)ptr, 0, pathLength);
				ptr = (int*)((byte*)ptr + pathLength);
				var uncompressedSize = *ptr++;
				_Bundles[i] = new BundleRecord(path, uncompressedSize, this, i);
				if (path.StartsWith(CUSTOM_BUNDLE_BASE_PATH))
					CustomBundles.Add(_Bundles[i]);
			}

			var fileCount = *ptr++;
			_Files = new(fileCount);
			for (var i = 0; i < fileCount; i++) {
				var nameHash = *(ulong*)ptr;
				ptr += 2;
				var bundle = _Bundles[*ptr++];
				var f = new FileRecord(nameHash, bundle, *ptr++, *ptr++);
				_Files.Add(nameHash, f);
				bundle._Files.Add(f);
			}

			var directoryCount = *ptr++;
			_Directories = new ReadOnlySpan<DirectoryRecord>(ptr, directoryCount).ToArray();
			ptr = (int*)((DirectoryRecord*)ptr + directoryCount);

			directoryBundleData = data[(int)((byte*)ptr - p)..];
		}

		if (parsePaths) {
			var failed = ParsePaths();
			if (failed != 0)
				ThrowHelper.Throw<InvalidDataException>($"Parsing path failed for {failed} files");
		}
	}

	/// <summary>
	/// Whether <see cref="ParsePaths"/> has been called.
	/// </summary>
	protected bool pathsParsed;
	/// <summary>
	/// Parses all the <see cref="FileRecord.Path"/> of each <see cref="Files"/>.
	/// </summary>
	/// <returns>Number of paths failed to parse, these files will have <see cref="FileRecord.Path"/> as <see langword="null"/><br />
	/// If this method has been called before, skips parsing and returns 0 always no matter how many files have failed last time.</returns>
	/// <remarks>This will automatically be called by constructor if <see langword="true"/> passed to the parsePaths parameter (default to <see langword="true"/>),
	/// and throw if the returned value is not 0.</remarks>
	public virtual unsafe int ParsePaths() {
		EnsureNotDisposed();
		if (pathsParsed)
			return 0;
		ReadOnlySpan<byte> directory;
		using (var directoryBundle = new Bundle(new MemoryStream(directoryBundleData), false))
			directory = directoryBundle.ReadWithoutCache();
		var failed = 0;
		fixed (byte* p = directory) {
			foreach (var d in _Directories) {
				var temp = new List<IEnumerable<byte>>();
				var Base = false;
				var offset = p + d.Offset;
				var ptr = offset;
				while (ptr - offset <= d.Size - 4) {
					var index = *(int*)ptr;
					ptr += 4;
					if (index == 0) {
						Base = !Base;
						if (Base)
							temp.Clear();
					} else {
						index -= 1;
						var str = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
						if (index < temp.Count) {
							var str2 = temp[index].Concat(str.ToArray());
							if (Base)
								temp.Add(str2);
							else {
								var b = str2.ToArray();
								fixed (byte* bp = b)
									if (_Files.TryGetValue(NameHash(b), out var f))
										f.Path = new string((sbyte*)bp, 0, b.Length);
									else
										++failed;
							}
						} else {
							if (Base)
								temp.Add(str.ToArray());
							else {
								if (_Files.TryGetValue(NameHash(str), out var f))
									f.Path = new string((sbyte*)ptr, 0, str.Length);
								else
									++failed;
							}
						}
						ptr += str.Length + 1; // '\0'
					}
				}
			}
		}
		pathsParsed = true;
		return failed;
	}

	/// <summary>
	/// Save the _.index.bin file.
	/// Call this after modifying the files or bundles.
	/// </summary>
	public virtual void Save() {
		lock (this) {
			if (_BundleToWrite is not null) {
				_BundleToWrite.Save(new(_BundleStreamToWrite!.GetBuffer(), 0, (int)_BundleStreamToWrite.Length));
				_BundleToWrite.Dispose();
				_BundleToWrite = null;
				_BundleStreamToWrite.SetLength(0);
			}
			_BundleStreamToWrite = null;

			EnsureNotDisposed();

			using var removed = new ValueList<BundleRecord>();
			for (int i = 0; i < CustomBundles.Count; ++i) {
				var br = CustomBundles[i];
				if (br.Files.Count == 0) { // Empty bundle
					CustomBundles.RemoveAt(i--);
					_Bundles.RemoveAt(br.BundleIndex);
					baseBundle.UncompressedSize -= br.RecordLength;
					removed.Add(br);
				}
			}

			using var ms = new MemoryStream(baseBundle.UncompressedSize);
			ms.Write(_Bundles.Length);
			foreach (var b in _Bundles)
				b.Serialize(ms);

			var cap = (int)ms.Length + (sizeof(int) + sizeof(int)) + _Files.Count * FileRecord.RecordLength + _Directories.Length * DirectoryRecord.RecordLength + directoryBundleData.Length;
			if (ms.Capacity < cap)
				ms.Capacity = cap;

			ms.Write(_Files.Count);
			foreach (var f in _Files.Values)
				f.Serialize(ms);

			ms.Write(_Directories.Length);
			ms.Write(_Directories);

			ms.Write(directoryBundleData, 0, directoryBundleData.Length);
			baseBundle.Save(new(ms.GetBuffer(), 0, (int)ms.Length));

			foreach (var br in removed)
				bundleFactory.DeleteBundle(br.Path);
		}
	}

	/// <summary>
	/// Get a FileRecord from its absolute path (This won't cause the tree building).
	/// The separator of the <paramref name="path"/> must be forward slash '/'
	/// </summary>
	/// <param name="path"><see cref="FileRecord.Path"/> </param>
	/// <returns>Null when not found</returns>
	public virtual bool TryGetFile(scoped ReadOnlySpan<char> path, [NotNullWhen(true)] out FileRecord? file) {
		EnsureNotDisposed();
		return _Files.TryGetValue(NameHash(path), out file);
	}

	/// <summary>
	/// Find a node in the tree. You should use <see cref="TryGetFile"/> instead of this if you have the absolute path of the file
	/// </summary>
	/// <param name="path">Relative path (with forward slashes) under <paramref name="root"/></param>
	/// <param name="node">The node found, or null when not found, or <paramref name="root"/> if <paramref name="path"/> is empty</param>
	/// <param name="root">Node to start searching, or <see langword="null"/> for <see cref="Root"/></param>
	/// <returns>Whether found a node</returns>
	public virtual bool TryFindNode(scoped ReadOnlySpan<char> path, [NotNullWhen(true)] out ITreeNode? node, DirectoryNode? root = null) {
		EnsureNotDisposed();
		root ??= Root;
		if (path == string.Empty) {
			node = root;
			return true;
		}
		foreach (var name in path.TrimEnd('/').Split('/')) {
			var next = root[name];
			if (next is not DirectoryNode dn)
				return (node = next) is not null;
			root = dn;
		}
		node = root;
		return true;
	}

	#region Extract/Replace
	/// <summary>
	/// Function for <see cref="Index"/>.Extract() to handle the extracted content of each file.
	/// </summary>
	/// <param name="record">Record of the file which the <paramref name="content"/> belongs to</param>
	/// <param name="content">Content of the file, or <see langword="null"/> if failed to get the bundle of the file</param>
	/// <returns><see langword="true"/> to cancel processing remaining files.</returns>
	/// <remarks>Do not Read/Write the <paramref name="record"/> during the extraction in this method.</remarks>
	public delegate bool FileHandler(FileRecord record, ReadOnlyMemory<byte>? content);
	/// <summary>
	/// Function for <see cref="Index"/>.Replace() to be called right after replacing each file.
	/// </summary>
	/// <param name="record">Record of the file replaced</param>
	/// <param name="path">Full path of the file replaced on disk or in zip</param>
	/// <returns><see langword="true"/> to cancel processing remaining files.</returns>
	/// <remarks>Do not Read/Write the <paramref name="record"/> during the replacement in this method.</remarks>
	public delegate bool FileCallback(FileRecord record, string path);

	/// <summary>
	/// Extract files in batch (out of order).
	/// </summary>
	/// <param name="files">Files to extract</param>
	/// <param name="callback">Function to execute on each file, see <see cref="FileHandler"/></param>
	/// <returns>Number of files extracted successfully.</returns>
	public static int Extract(IEnumerable<FileRecord> files, FileHandler callback) {
		var groups = files.GroupBy(f => f.BundleRecord);
		var count = 0;
		foreach (var g in groups) {
			if (g.Key.TryGetBundle(out var bd))
				using (bd)
					foreach (var f in g) {
						++count;
						if (callback(f, f.Read(bd)))
							break;
					}
			else
				foreach (var f in g) {
					if (callback(f, null))
						break;
				}
		}
		return count;
	}

	/// <summary>
	/// Extract files under a <paramref name="node"/> recursively (out of order). 
	/// </summary>
	/// <param name="node">Node to extract</param>
	/// <param name="callback">Function to execute on each file, see <see cref="FileHandler"/></param>
	/// <returns>Number of files extracted successfully.</returns>
	public static int Extract(ITreeNode node, FileHandler callback) => Extract(Recursefiles(node).Select(n => n.Record), callback);

	/// <summary>
	/// Extract files under a <paramref name="node"/> to disk recursively (out of order).
	/// </summary>
	/// <param name="node">Node to extract</param>
	/// <param name="path">Path on disk to extract to</param>
	/// <param name="callback">See <see cref="FileCallback"/></param>
	/// <returns>Number of files extracted successfully.</returns>
	public static int Extract(ITreeNode node, string path, FileCallback? callback = null) {
		path = Path.GetFullPath(Utils.ExpandPath(path.TrimEnd('/', '\\'))) + Path.DirectorySeparatorChar;
		var trim = Path.GetDirectoryName(ITreeNode.GetPath(node).TrimEnd('/'))!.Length;

		Task? lastTask = null;
		FileRecord lastFr = null!;
		string lastPath = null!;
		var result = Extract(Recursefiles(node, path).Select(n => n.Record), (fr, data) => {
			if (!data.HasValue)
				return false;
			if (lastTask is not null) {
				lastTask.GetAwaiter().GetResult();
				if (callback?.Invoke(lastFr, lastPath) ?? false)
					return true;
			}
			lastPath = path + fr.Path[trim..];
			lastTask = SystemExtensions.System.IO.File.WriteAllBytesAsync(lastPath, data.Value).AsTask();
			lastFr = fr;
			return false;
		});
		if (lastTask is not null) {
			lastTask.GetAwaiter().GetResult();
			callback?.Invoke(lastFr, lastPath);
		}
		return result;
	}

	/// <summary>
	/// Extract files parallelly (out of order).
	/// </summary>
	/// <param name="files">Files to extract</param>
	/// <param name="callback">
	/// Action to execute on each file, see <see cref="FileHandler"/>
	/// <para>Note that this may be executed parallelly.</para>
	/// </param>
	/// <returns>Number of files extracted successfully.</returns>
	/// <remarks>
	/// This method is experimental and may not be faster than <see cref="Extract(IEnumerable{FileRecord}, FileHandler)"/>.
	/// <para>Files in the same bundle will be extracted sequentially in the same thread.</para>
	/// </remarks>
	public static int ExtractParallel(IEnumerable<FileRecord> files, FileHandler callback) {
		var count = 0;
		var cancelled = false;
		files.GroupBy(f => f.BundleRecord).AsParallel().ForAll(g => {
			if (cancelled)
				return;
			if (g.Key.TryGetBundle(out var bd))
				using (bd)
					foreach (var f in g) {
						if (cancelled)
							return;
						cancelled = callback(f, f.Read(bd));
						Interlocked.Increment(ref count);
					}
			else
				foreach (var f in g) {
					if (cancelled)
						return;
					callback(f, null);
				}
		});
		return count;
	}

	/// <summary>
	/// Extract files under a <paramref name="node"/> recursively (out of order) parallelly.
	/// </summary>
	/// <param name="node">Node to extract</param>
	/// <param name="callback">
	/// Action to execute on each file, see <see cref="FileHandler"/>
	/// <para>Note that this may be executed parallelly.</para>
	/// </param>
	/// <returns>Number of files extracted successfully.</returns>
	/// <remarks>
	/// This method is experimental and may not be faster than <see cref="Extract(ITreeNode, FileHandler)"/>.
	/// <para>Files in the same bundle will be extracted sequentially in the same thread.</para>
	/// </remarks>
	public static int ExtractParallel(ITreeNode node, FileHandler callback) => ExtractParallel(Recursefiles(node).Select(n => n.Record), callback);

	/// <summary>
	/// Extract files under a <paramref name="node"/> to disk recursively (out of order) parallelly.
	/// </summary>
	/// <param name="node">Node to extract</param>
	/// <param name="path">Path on disk to extract to</param>
	/// <param name="callback">
	/// See <see cref="FileCallback"/>
	/// <para>Note that this may be executed parallelly.</para>
	/// </param>
	/// <returns>Number of files extracted successfully.</returns>
	/// <remarks>
	/// This method is experimental and may not be faster than <see cref="Extract(ITreeNode, string, FileCallback?)"/>.
	/// <para>Files in the same bundle will be extracted sequentially in the same thread.</para>
	/// </remarks>
	public static int ExtractParallel(ITreeNode node, string path, FileCallback? callback = null) {
		path = Path.GetFullPath(Utils.ExpandPath(path.TrimEnd('/', '\\'))) + Path.DirectorySeparatorChar;
		var trim = Path.GetDirectoryName(ITreeNode.GetPath(node).TrimEnd('/'))!.Length;
		return ExtractParallel(Recursefiles(node, path).Select(n => n.Record), (fr, data) => {
			var p = path + fr.Path[trim..];
			if (data.HasValue)
				SystemExtensions.System.IO.File.WriteAllBytes(p, data.Value.Span);
			return callback?.Invoke(fr, p) ?? false;
		});
	}

	/// <summary>
	/// Patch with a zip file.
	/// Throw when a file in <paramref name="zipEntries"/> couldn't be found in <paramref name="index"/>.
	/// </summary>
	/// <param name="zipEntries">Entries to read files to replace</param>
	/// <param name="callback">See <see cref="FileCallback"/></param>
	/// <param name="saveIndex">Whether to call <see cref="Save"/> automatically after replacement done</param>
	/// <returns>Number of files replaced.</returns>
	public static int Replace(Index index, IEnumerable<ZipArchiveEntry> zipEntries, FileCallback? callback = null, bool saveIndex = true) {
		index.EnsureNotDisposed();
		var count = 0;
		foreach (var e in zipEntries) {
			if (e.FullName.EndsWith('/')) // dir
				continue;

			if (!index._Files.TryGetValue(index.NameHash(e.FullName), out var fr))
				ThrowHelper.Throw<FileNotFoundException>("Could not found file in Index: " + e.FullName);
			
			var length = (int)e.Length;
			using (var fs = e.Open())
				fr.Write(span => fs.ReadAtLeast(span, length), length);
			++count;

			if (callback?.Invoke(fr, e.FullName) ?? false)
				break;
		}

		if (saveIndex && count != 0)
			index.Save();
		return count;
	}

	/// <summary>
	/// Write files under a <paramref name="node"/> recursively (DFS).
	/// The search is based on <paramref name="node"/>, and skip any file not exist in <paramref name="path"/>.
	/// </summary>
	/// <param name="node">Node to replace</param>
	/// <param name="path">Path of a folder on disk to read files to replace</param>
	/// <param name="callback">See <see cref="FileCallback"/></param>
	/// <param name="saveIndex">Whether to call <see cref="Save"/> automatically after replacement done</param>
	/// <returns>Number of files replaced.</returns>
	public static int Replace(ITreeNode node, string path, FileCallback? callback = null, bool saveIndex = true) {
		path = Path.GetFullPath(Utils.ExpandPath(path.TrimEnd('/', '\\'))) + Path.DirectorySeparatorChar;
		var trim = ITreeNode.GetPath(node).Length;

		Index? index = null;
		var count = 0;
		foreach (var fn in Recursefiles(node)) {
			var fr = fn.Record;
			if (index is null) {// first file
				index = fr.BundleRecord.Index;
				index.EnsureNotDisposed();
			} else if (fr.BundleRecord.Index != index)
				ThrowHelper.Throw<InvalidOperationException>("Attempt to mixedly use FileRecords come from different Index");
			
			var p = path + fn.Record.Path[trim..];
			if (File.Exists(p)) {
				fr.Write(File.ReadAllBytes(p));
				++count;

				if (callback?.Invoke(fr, p) ?? false)
					break;
			}
		}

		if (saveIndex)
			index?.Save(); // count != 0
		return count;
	}

	/// <summary>
	/// Write files under a node recursively (DFS).
	/// The search is based on files in <paramref name="pathOnDisk"/>, and skip any file not exist under <paramref name="nodePath"/>.
	/// </summary>
	/// <param name="nodePath">Path of a <see cref="ITreeNode"/> in <paramref name="index"/> to replace. (with forward slashes, and not starting with slash)</param>
	/// <param name="pathOnDisk">Path of a folder on disk to read files to replace</param>
	/// <param name="callback">See <see cref="FileCallback"/></param>
	/// <param name="saveIndex">Whether to call <see cref="Save"/> automatically after replacement done</param>
	/// <returns>Number of files replaced.</returns>
	/// <remarks>
	/// This method won't check if a <see cref="ITreeNode"/> with <paramref name="nodePath"/> is exist.
	/// If not, the search still runs but always return 0.
	/// <para>
	/// Although there's a <paramref name="nodePath"/> parameter,
	/// this method doesn't require an actual <see cref="ITreeNode"/> (which requires <see cref="ParsePaths"/> and <see cref="BuildTree"/>) to work.
	/// </para>
	/// </remarks>
	public static int Replace(Index index, string nodePath, string pathOnDisk, FileCallback? callback = null, bool saveIndex = true) {
		nodePath = nodePath.TrimEnd('/');
		pathOnDisk = Path.GetFullPath(Utils.ExpandPath(pathOnDisk.TrimEnd('/', '\\')));

		var count = 0;
		if (index.TryGetFile(nodePath, out var fr) && File.Exists(pathOnDisk)) {
			fr.Write(File.ReadAllBytes(pathOnDisk), saveIndex);
			callback?.Invoke(fr, pathOnDisk);
			count = 1;
		}

		if (!Directory.Exists(pathOnDisk))
			return count;

		if (Path.DirectorySeparatorChar != '/')
			pathOnDisk = pathOnDisk.Replace(Path.DirectorySeparatorChar, '/');
		var trim = pathOnDisk.Length;


		foreach (var p in Directory.EnumerateFiles(pathOnDisk, "*", SearchOption.AllDirectories))
			if (index.TryGetFile(nodePath + p[trim..], out fr)) {
				fr.Write(File.ReadAllBytes(p));
				++count;

				if (callback?.Invoke(fr, p) ?? false)
					break;
			}

		if (saveIndex && count != 0)
			index.Save();
		return count;
	}
	#endregion Extract/Replace

	/// <summary>
	/// Path to create bundle (Must end with slash)
	/// </summary>
	private const string CUSTOM_BUNDLE_BASE_PATH = "LibGGPK3/";
	/// <summary>
	/// Get an available bundle with size &lt; <see cref="MaxBundleSize"/>) to write under "Bundles2" with name start with <see cref="CUSTOM_BUNDLE_BASE_PATH"/>.
	/// Or create one if not found.
	/// Note that the returned bundle may contain existing data (with size: <paramref name="originalSize"/>) that should not be overwritten.
	/// </summary>
	/// <param name="originalSize">Size of the existing data in the bundle</param>
	/// <remarks>
	/// Since LibBundle3_v2.0.0, all changes to files should be written to a new bundle from this function instead of the old behavior (write to the original bundle or the smallest in Bundles).
	/// Remember to call <see cref="Bundle.Dispose"/> after use to prevent memory leak.
	/// </remarks>
	internal Bundle GetBundleToWrite(out int originalSize) {
		lock (this) {
			EnsureNotDisposed();
			originalSize = 0;

			static int GetSize(BundleRecord br) {
				var f = br._Files.MaxBy(f => f.Offset);
				if (f is null)
					return 0;
				return f.Offset + f.Size;
			}

			Bundle? b = null;
			foreach (var cb in CustomBundles) {
				if ((originalSize = GetSize(cb)) < MaxBundleSize && cb.TryGetBundle(out b))
					break;
			}

			if (b is null) { // Create one
				var path = CUSTOM_BUNDLE_BASE_PATH + CustomBundles.Count;
				if (CustomBundles.Exists(br => br._Path == path)) {
					for (var i = 0; i < CustomBundles.Count; i++) {
						var exists = false;
						for (var j = 0; j < CustomBundles.Count; j++)
							if (CustomBundles[j]._Path == CUSTOM_BUNDLE_BASE_PATH + i) {
								exists = true;
								break;
							}
						if (!exists) {
							path = CUSTOM_BUNDLE_BASE_PATH + i;
							break;
						}
					}
				}
				b = CreateBundle(path);
				CustomBundles.Add(b.Record!);
				originalSize = GetSize(b.Record!);
			}
			return b;
		}
	}

	/// <summary>
	/// Create a new bundle and add it to <see cref="Bundles"/> using <see cref="IBundleFactory.CreateBundle"/>
	/// </summary>
	/// <param name="bundlePath">Relative path of the bundle without ".bundle.bin"</param>
	protected virtual Bundle CreateBundle(string bundlePath) {
		lock (this) {
			EnsureNotDisposed();
			var len = _Bundles.Length;
			var br = new BundleRecord(bundlePath, 0, this, len);
			var b = new Bundle(bundleFactory.CreateBundle(bundlePath + ".bundle.bin"), br);
			Array.Resize(ref _Bundles, len + 1);
			_Bundles[len] = br;
			baseBundle.UncompressedSize += br.RecordLength; // Hack to prevent MemoryStream from reallocating when saving
			return b;
		}
	}

	#region NameHashing
	/// <summary>
	/// Get the hash of a file path
	/// </summary>
	[SkipLocalsInit]
	public unsafe ulong NameHash(scoped ReadOnlySpan<char> name) {
		if (_Directories[0].PathHash == 0xF42A94E69CFF42FEul) { // since poe 3.21.2 patch
			Span<char> span = stackalloc char[name.Length];
			name.ToLowerInvariant(span); // changing case does not affect length
			var utf8 = MemoryMarshal.AsBytes(span);
			return NameHash(utf8[..Encoding.UTF8.GetBytes(span, utf8)]);
		} else {
			Span<byte> utf8 = stackalloc byte[name.Length];
			return NameHash(utf8[..Encoding.UTF8.GetBytes(name, utf8)]);
		}
	}

	/// <summary>
	/// Get the hash of a file path,
	/// <paramref name="utf8Name"/> must be lowercased unless it comes from ggpk before patch 3.21.2
	/// </summary>
	public unsafe ulong NameHash(scoped ReadOnlySpan<byte> utf8Name) {
		EnsureNotDisposed();
		switch (_Directories[0].PathHash) {
			case 0xF42A94E69CFF42FE:
				return MurmurHash64A(utf8Name); // since poe 3.21.2 patch
			case 0x07E47507B4A92E53:
				return FNV1a64Hash(utf8Name);
		}
		ThrowHelper.Throw<Exception>("Unable to detect the namehash algorithm");
		return 0;
	}

	/// <summary>
	/// Get the hash of a file path, <paramref name="utf8Name"/> must be lowercased
	/// </summary>
	protected static unsafe ulong MurmurHash64A(scoped ReadOnlySpan<byte> utf8Name, ulong seed = 0x1337B33F) {
		if (utf8Name.IsEmpty)
			return 0xF42A94E69CFF42FEul;

		ref ulong p = ref Unsafe.As<byte, ulong>(ref MemoryMarshal.GetReference(utf8Name));
		if (Unsafe.AddByteOffset(ref p, utf8Name.Length - 1) == '/')
			utf8Name = utf8Name.SliceUnchecked(0, utf8Name.Length - 1); // TrimEnd('/')

		const ulong m = 0xC6A4A7935BD1E995ul;
		const int r = 47;

		unchecked {
			seed ^= (ulong)utf8Name.Length * m;

			if (utf8Name.Length >= sizeof(ulong)) {
				ref ulong pEnd = ref Unsafe.Add(ref p, utf8Name.Length / sizeof(ulong));
				do {
					ulong k = p * m;
					seed = (seed ^ (k ^ (k >> r)) * m) * m;
					p = ref Unsafe.Add(ref p, 1);
				} while (Unsafe.IsAddressLessThan(ref p, ref pEnd));
			}

			int remainingBytes = utf8Name.Length % sizeof(ulong);
			if (remainingBytes != 0)
				seed = (seed ^ (p & (ulong.MaxValue >> ((sizeof(ulong) - remainingBytes) * 8)))) * m;

			seed = (seed ^ (seed >> r)) * m;
			return seed ^ (seed >> r);
		}
	}

	/// <summary>
	/// Get the hash of a file path with ggpk before patch 3.21.2
	/// </summary>
	protected static unsafe ulong FNV1a64Hash(scoped ReadOnlySpan<byte> utf8Str) {
		var hash = 0xCBF29CE484222325ul;
		const ulong FNV_prime = 0x100000001B3ul;
		unchecked {
			if (utf8Str[^1] == '/') {
				utf8Str = utf8Str[..^1]; // TrimEnd('/')
				foreach (ulong ch in utf8Str)
					hash = (hash ^ ch) * FNV_prime;
			} else
				foreach (ulong ch in utf8Str) {
					if (ch is >= 'A' and <= 'Z')
						hash = (hash ^ (ch + (ulong)('A' - 'a'))) * FNV_prime; // ToLower
					else
						hash = (hash ^ ch) * FNV_prime;
				}
			return (((hash ^ '+') * FNV_prime) ^ '+') * FNV_prime; // filenames end with two '+'
		}
	}
	#endregion NameHashing

	#region Helpers
	/// <summary>
	/// Enumerate all files under a node (DFS).
	/// </summary>
	/// <param name="node">Node to start recursive</param>
	public static IEnumerable<IFileNode> Recursefiles(ITreeNode node) {
		if (node is IFileNode fn)
			yield return fn;
		else if (node is IDirectoryNode dn)
			foreach (var n in dn.Children)
				foreach (var f in Recursefiles(n))
					yield return f;
	}
	/// <summary>
	/// Enumerate all files under a node (DFS), and call <see cref="Directory.CreateDirectory(string)"/> for each folder.
	/// </summary>
	/// <param name="node">Node to start recursive</param>
	/// <param name="path">Path on disk</param>
	protected static IEnumerable<IFileNode> Recursefiles(ITreeNode node, string path) {
		Directory.CreateDirectory(path);
		if (node is IFileNode fn)
			yield return fn;
		else if (node is IDirectoryNode dn)
			foreach (var n in dn.Children)
				foreach (var f in Recursefiles(n, $@"{path}/{dn.Name}"))
					yield return f;
	}

	/// <summary>
	/// Sort files with index of their bundle (<see cref="BundleRecord.BundleIndex"/>) with CountingSort (stable) to get better performance for reading.
	/// </summary>
	/// <remarks>
	/// <code>IEnumerable&lt;FileRecord&gt;.GroupBy(f => f.BundleRecord);</code> can also achieve similar purposes in some case.
	/// </remarks>
	/// <seealso cref="Enumerable.GroupBy{TSource, TKey}(IEnumerable{TSource}, Func{TSource, TKey})"/>
	public static unsafe FileRecord[] SortWithBundle(IEnumerable<FileRecord> files) {
		var list = files is IReadOnlyList<FileRecord> irl ? irl : files is IList<FileRecord> il ? il.AsIReadOnly() : files.ToList();
		if (list.Count <= 0)
			return [];
		var index = list[0].BundleRecord.Index;
		var bundleCount = index._Bundles.Length;
		var count = new int[bundleCount];
		foreach (var f in list) {
			if (f.BundleRecord.Index != index)
				throw new InvalidOperationException("Attempt to mixedly use FileRecords come from different Index");
			++count[f.BundleRecord.BundleIndex];
		}
		for (var i = 1; i < bundleCount; ++i)
			count[i] += count[i - 1];
		var sorted = new FileRecord[list.Count];
		for (var i = sorted.Length - 1; i >= 0; --i)
			sorted[--count[list[i].BundleRecord.BundleIndex]] = list[i];
		return sorted;
	}

	protected virtual void EnsureNotDisposed() {
		ObjectDisposedException.ThrowIf(_Bundles is null, this);
	}
	#endregion Helpers

	public virtual void Dispose() {
		GC.SuppressFinalize(this);
		lock (this) {
			if (_BundleToWrite is not null) {
				Debug.Fail("There're still changes haven't been saved when disposing Index. Did you forget to call Save()?");
				_BundleToWrite.Dispose();
				_BundleToWrite = null;
			}
			_BundleStreamToWrite?.Dispose();
			_BundleStreamToWrite = null;
			WR_BundleStreamToWrite.SetTarget(null!);
			baseBundle.Dispose();
			_Bundles = null!;
			_Root = null;
		}
	}

	/// <summary>
	/// Currently unused
	/// </summary>
	[Serializable]
	[StructLayout(LayoutKind.Sequential, Size = RecordLength, Pack = 4)]
	protected internal struct DirectoryRecord(ulong pathHash, int offset, int size, int recursiveSize) {
		public ulong PathHash = pathHash;
		public int Offset = offset;
		public int Size = size;
		public int RecursiveSize = recursiveSize;

		/// <summary>
		/// Size of the content when serializing to <see cref="Index"/>
		/// </summary>
		public const int RecordLength = sizeof(ulong) + sizeof(int) * 3;
	}
}