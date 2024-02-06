using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using LibBundle3.Nodes;
using LibBundle3.Records;
using SystemExtensions;
using SystemExtensions.Collections;
using SystemExtensions.Spans;
using SystemExtensions.Streams;

[module: SkipLocalsInit]

namespace LibBundle3 {
	public class Index : IDisposable {
		/// <summary>
		/// <see cref="Bundle"/> instance of "_.index.bin"
		/// </summary>
		protected readonly Bundle baseBundle;
		/// <summary>
		/// Data for <see cref="ParsePaths"/>
		/// </summary>
		protected readonly byte[] directoryBundleData;
		/// <summary>
		/// Bundles ceated by this library for writing modfied files.
		/// </summary>
		protected readonly List<BundleRecord> CustomBundles = [];
		protected BundleRecord[] _Bundles;
		protected internal readonly DirectoryRecord[] _Directories;
		protected readonly Dictionary<ulong, FileRecord> _Files;

		public virtual ReadOnlySpan<BundleRecord> Bundles => _Bundles;
		protected ReadOnlyDictionary<ulong, FileRecord>? _readonlyFiles;
		public virtual ReadOnlyDictionary<ulong, FileRecord> Files => _readonlyFiles ??= new(_Files);

		protected internal readonly IBundleFileFactory bundleFactory;
		/// <summary>
		/// Size to limit each bundle when writing, default to 200MB
		/// </summary>
		public virtual int MaxBundleSize { get; set; } = 209715200;

		protected DirectoryNode? _Root;
		/// <summary>
		/// Root node of the tree (This will call <see cref="BuildTree"/> with default implementation when first calling).
		/// You can also implement your custom class and use <see cref="BuildTree"/>.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown when <see cref="ParsePaths"/> haven't been called</exception>
		public virtual DirectoryNode Root => _Root ??= (DirectoryNode)BuildTree(DirectoryNode.CreateInstance, FileNode.CreateInstance);

		public delegate IDirectoryNode CreateDirectoryInstance(string name, IDirectoryNode? parent);
		public delegate IFileNode CreateFileInstance(FileRecord record, IDirectoryNode parent);
		/// <summary>
		/// Build a tree to represent the file and directory structure in bundles
		/// </summary>
		/// <param name="createDirectory">Function to create a instance of <see cref="IDirectoryNode"/></param>
		/// <param name="createFile">Function to create a instance of <see cref="IFileNode"/></param>
		/// <returns>The root node of the built tree</returns>
		/// <remarks>
		/// You can implement your custom class and call this, or just use the default implementation by calling <see cref="Root"/>.
		/// </remarks>
		/// <exception cref="InvalidOperationException">Thrown when <see cref="ParsePaths"/> haven't been called</exception>
		public virtual IDirectoryNode BuildTree(CreateDirectoryInstance createDirectory, CreateFileInstance createFile) {
			EnsureNotDisposed();
			var root = createDirectory("", null);
			foreach (var f in _Files.Values.OrderBy(f => f.Path ?? throw ThrowHelper.Create<InvalidOperationException>("The Path of a FileRecord is null.\r\nYou may have passed false to the parsePaths parameter of the constructor of Index, and haven't called Index.ParsePaths after that."))) {
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

		/// <param name="filePath">Path to _.index.bin on disk</param>
		/// <param name="parsePaths">
		/// Whether to call <see cref="ParsePaths"/> automatically.
		/// <see langword="false"/> to speed up reading, but all <see cref="FileRecord.Path"/> in each of <see cref="Files"/> will be <see langword="null"/>,
		/// and <see cref="Root"/> and <see cref="BuildTree"/> will be unable to use until you call <see cref="ParsePaths"/> manually.
		/// </param>
		/// <param name="bundleFactory">Factory to handle .bin files of <see cref="Bundle"/></param>
		/// <exception cref="FileNotFoundException" />
		public Index(string filePath, bool parsePaths = true, IBundleFileFactory? bundleFactory = null) : this(
				  File.Open(filePath = Utils.ExpandPath(filePath), FileMode.Open, FileAccess.ReadWrite, FileShare.Read),
				  false,
				  parsePaths,
				  bundleFactory ?? new DriveBundleFactory(Path.GetDirectoryName(Path.GetFullPath(filePath))!)
			) { }

		/// <param name="stream">Stream of the _.index.bin file</param>
		/// <param name="leaveOpen">If false, close the <paramref name="stream"/> when this instance is disposed</param>
		/// <param name="parsePaths">
		/// Whether to call <see cref="ParsePaths"/> automatically.
		/// <see langword="false"/> to speed up reading, but all <see cref="FileRecord.Path"/> in each of <see cref="Files"/> will be <see langword="null"/>,
		/// and <see cref="Root"/> and <see cref="BuildTree"/> will be unable to use until you call <see cref="ParsePaths"/> manually.
		/// </param>
		/// <param name="bundleFactory">Factory to handle .bin files of <see cref="Bundle"/></param>
		public unsafe Index(Stream stream, bool leaveOpen = true, bool parsePaths = true, IBundleFileFactory? bundleFactory = null) {
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

			if (parsePaths)
				ParsePaths();
		}

		/// <summary>
		/// Whether <see cref="ParsePaths"/> has been called
		/// </summary>
		protected bool pathsParsed;
		/// <summary>
		/// Parse all the <see cref="FileRecord.Path"/> of each <see cref="Files"/>.
		/// </summary>
		/// <remarks>This will automatically be called by constructor if <see langword="true"/> passed to the parsePaths parameter (default to <see langword="true"/>).</remarks>
		public virtual unsafe void ParsePaths() {
			EnsureNotDisposed();
			if (pathsParsed)
				return;
			ReadOnlySpan<byte> directory;
			using (var directoryBundle = new Bundle(new MemoryStream(directoryBundleData), false))
				directory = directoryBundle.ReadWithoutCache();
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
										_Files[NameHash(b)].Path = new string((sbyte*)bp, 0, b.Length);
								}
							} else {
								if (Base)
									temp.Add(str.ToArray());
								else
									_Files[NameHash(str)].Path = new string((sbyte*)ptr, 0, str.Length);
							}
							ptr += str.Length + 1; // '\0'
						}
					}
				}
			}
			pathsParsed = true;
		}

		/// <summary>
		/// Save the _.index.bin.
		/// Call this after <see cref="FileRecord.Redirect"/>
		/// </summary>
		public virtual unsafe void Save() {
			EnsureNotDisposed();
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
			/*foreach (var d in _Directories)
				ms.Write(d);*/
			ms.Write(_Directories);

			ms.Write(directoryBundleData, 0, directoryBundleData.Length);
			baseBundle.Save(new(ms.GetBuffer(), 0, (int)ms.Length));
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
		/// <param name="path">Relative path (which not start or end with slash) under <paramref name="root"/></param>
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
			var splittedPath = path.Split('/');
			foreach (var name in splittedPath) {
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
		/// Function for <see cref="Index"/>.Extract to handle the extracted content of each file
		/// </summary>
		/// <param name="record">Record of the file which the <paramref name="content"/> belongs to</param>
		/// <param name="content">Content of the file, or <see langword="null"/> if failed to get the bundle of the file</param>
		public delegate void FileHandler(FileRecord record, ReadOnlyMemory<byte>? content);

		/// <summary>
		/// Extract files in batch.
		/// Use <see cref="FileRecord.Read"/> instead if only single file (each bundle) is needed.
		/// </summary>
		/// <returns>KeyValuePair of each file and its content. The value can be null if the file failed to extract (bundle not found).</returns>
		public static IEnumerable<(FileRecord, ReadOnlyMemory<byte>?)> Extract(IEnumerable<FileRecord> files) {
			var groups = files.GroupBy(f => f.BundleRecord);
			foreach (var g in groups) {
				if (g.Key.TryGetBundle(out var bd))
					using (bd)
						foreach (var f in g)
							yield return new(f, f.Read(bd));
				else
					foreach (var f in g)
						yield return new(f, null);
			}
		}

		/// <summary>
		/// Extract files under a node in batch. 
		/// Use <see cref="FileRecord.Read"/> instead if only single file (each bundle) is needed.
		/// </summary>
		/// <param name="node">Node to extract (recursively)</param>
		/// <returns>KeyValuePair of each file and its content. The value can be null if the file failed to extract (bundle not found).</returns>
		public static IEnumerable<(FileRecord, ReadOnlyMemory<byte>?)> Extract(ITreeNode node) => Extract(Recursefiles(node).Select(n => n.Record));

		/// <summary>
		/// Extract files under a node to disk in batch. 
		/// Use <see cref="FileRecord.Read"/> instead if only single file (each bundle) is needed.
		/// </summary>
		/// <param name="node">Node to extract (recursively)</param>
		/// <param name="path">Path on disk to extract to</param>
		/// <param name="progress">Invoked just after each file extracted</param>
		/// <returns>Number of files extracted</returns>
		public static int Extract(ITreeNode node, string path, Action? progress = null) {
			path = Utils.ExpandPath(path.TrimEnd('/', '\\')) + "/";
			var trim = Path.GetDirectoryName(ITreeNode.GetPath(node).TrimEnd('/'))!.Length;
			var count = 0;
			foreach (var (f, b) in Extract(Recursefiles(node, path).Select(n => n.Record))) {
				if (!b.HasValue)
					continue;
				var p = path + f.Path[trim..];
				Directory.CreateDirectory(Path.GetDirectoryName(p)!);
				using (var fs = File.Create(p))
					fs.Write(b.Value.Span);
				++count;
				progress?.Invoke();
			}
			return count;
		}

		/// <summary>
		/// Extract files parallelly.
		/// </summary>
		/// <param name="files">Files to extract</param>
		/// <param name="callback">Action to execute on each file</param>
		public static void ExtractParallel(IEnumerable<FileRecord> files, FileHandler callback) {
			files.GroupBy(f => f.BundleRecord).AsParallel().ForAll(g => {
				if (g.Key.TryGetBundle(out var bd))
					using (bd)
						foreach (var f in g)
							callback(f, f.Read(bd));
				else
					foreach (var f in g)
						callback(f, null);
			});
		}

		/// <summary>
		/// Extract files under a node in batch. 
		/// Use <see cref="FileRecord.Read"/> instead if only single file (each bundle) is needed.
		/// </summary>
		/// <param name="node">Node to extract (recursively)</param>
		/// <returns>KeyValuePair of each file and its content. The value can be null if the file failed to extract (bundle not found).</returns>
		public static void ExtractParallel(ITreeNode node, FileHandler callback) => ExtractParallel(Recursefiles(node).Select(n => n.Record), callback);

		/// <summary>
		/// Extract files under a node to disk in batch. 
		/// Use <see cref="FileRecord.Read"/> instead if only single file (each bundle) is needed.
		/// </summary>
		/// <param name="node">Node to extract (recursively)</param>
		/// <param name="path">Path on disk to extract to</param>
		/// <param name="progress">Invoked just after each file extracted</param>
		/// <returns>Number of files extracted</returns>
		public static int ExtractParallel(ITreeNode node, string path, Action? progress = null) {
			path = Utils.ExpandPath(path.TrimEnd('/', '\\')) + "/";
			var trim = Path.GetDirectoryName(ITreeNode.GetPath(node).TrimEnd('/'))!.Length;

			var count = 0;
			ExtractParallel(Recursefiles(node, path).Select(n => n.Record), (f, b) => {
				if (!b.HasValue)
					return;
				var p = path + f.Path[trim..];
				Directory.CreateDirectory(Path.GetDirectoryName(p)!);
				using (var fs = File.Create(p))
					fs.Write(b.Value.Span);
				++count;
				progress?.Invoke();
			});
			
			return count;
		}

		/// <summary>
		/// Function for <see cref="Index"/>.Replace to get content of file for replacing and to report progress
		/// </summary>
		/// <param name="record">Record of data content currently being requested</param>
		/// <param name="fileWritten">Number of files processed so far</param>
		/// <param name="content">New content of the file to replace with</param>
		/// <returns>Whether to process this file, <see langword="false"/> to skip replacing this file</returns>
		public delegate bool GetDataHandler(FileRecord record, int fileWritten, [MaybeNullWhen(false)] out ReadOnlySpan<byte> content);

		/// <summary>
		/// Write files in batch.
		/// This call <see cref="Save"/> automatically.
		/// </summary>
		/// <param name="funcGetData">Function to get the content to write for each file</param>
		/// <returns>Number of files replaced</returns>
		public static int Replace(IEnumerable<FileRecord> files, GetDataHandler funcGetData, bool saveIndex = true) {
			var first = files.FirstOrDefault();
			if (first is null)
				return 0;

			var index = first.BundleRecord.Index;
			var count = 0;
			var b = index.GetBundleToWrite(out var originalSize);
			try {
				using var ms = new MemoryStream(originalSize);
				ms.Write(b.ReadWithoutCache(0, originalSize)); // Read original data of bundle
				foreach (var fr in files) {
					if (fr.BundleRecord.Index != index)
						throw new InvalidOperationException("Attempt to mixedly use FileRecords come from different Index");
					if (ms.Length >= index.MaxBundleSize) { // Change another bundle to write
						b.Save(new(ms.GetBuffer(), 0, (int)ms.Length));
						b.Dispose();
						ms.SetLength(0);
						b = index.GetBundleToWrite(out originalSize);
						ms.Write(b.ReadWithoutCache(0, originalSize));
					}
					if (funcGetData(fr, count, out var data)) {
						fr.Redirect(b.Record!, (int)ms.Length, data.Length);
						ms.Write(data);
						++count;
					}
				}
				b.Save(new(ms.GetBuffer(), 0, (int)ms.Length));
			} finally {
				b.Dispose();
			}
			if (saveIndex)
				index.Save();
			return count;
		}

		/// <summary>
		/// Patch with a zip file.
		/// Throw when a file in .zip couldn't be found in Index.
		/// This call <see cref="Save"/> automatically.
		/// </summary>
		/// <returns>Number of files replaced</returns>
		public static int Replace(Index index, IEnumerable<ZipArchiveEntry> zipEntries, bool saveIndex = true) {
			index.EnsureNotDisposed();
			var dict = new Dictionary<FileRecord, ZipArchiveEntry>();
			foreach (var zip in zipEntries) {
				if (zip.FullName.EndsWith('/')) // folder
					continue;
				if (!index._Files.TryGetValue(index.NameHash(zip.FullName), out var f))
					throw new FileNotFoundException("Could not found file in Index: " + zip.FullName);
				dict.Add(f, zip);
			}

			byte[]? array = null;
			try {
				return Replace(dict.Keys, (FileRecord fr, int _, out ReadOnlySpan<byte> content) => {
					if (array is not null)
						ArrayPool<byte>.Shared.Return(array);
					var zip = dict[fr];
					var length = (int)zip.Length;
					using var fs = zip.Open();
					array = ArrayPool<byte>.Shared.Rent(length);
					fs.ReadExactly(new(array, 0, length));
					content = new(array, 0, length);
					return true;
				}, saveIndex);
			} finally {
				if (array is not null)
					ArrayPool<byte>.Shared.Return(array);
			}
		}

		/// <summary>
		/// Write files under a node in batch.
		/// This call <see cref="Save"/> automatically.
		/// </summary>
		/// <param name="node">Node to replace (recursively) (DFS)</param>
		/// <param name="funcGetData">Function to get the content to write for each file</param>
		/// <returns>Number of files replaced</returns>
		public static int Replace(ITreeNode node, GetDataHandler funcGetData, bool saveIndex = true) {
			return Replace(Recursefiles(node).Select(n => n.Record), funcGetData, saveIndex);
		}

		/// <summary>
		/// Write files under a node in batch.
		/// Skip any file which isn't exist under <paramref name="node"/>.
		/// This call <see cref="Save"/> automatically.
		/// </summary>
		/// <param name="node">Node to replace (recursively) (DFS)</param>
		/// <param name="path">Path of a folder on disk to read files to replace</param>
		/// <returns>Number of files replaced</returns>
		public static int Replace(ITreeNode node, string path, bool saveIndex = true) {
			path = Utils.ExpandPath(path.TrimEnd('/', '\\')) + "/";
			var trim = ITreeNode.GetPath(node).TrimEnd('/').Length;

			byte[] b = null!;
			IEnumerable<FileRecord> Enumerate() {
				foreach (var fr in Recursefiles(node).Select(n => n.Record)) {
					var p = path + fr.Path[trim..];
					if (File.Exists(p)) {
						b = File.ReadAllBytes(p);
						yield return fr;
					}
				}
			}

			return Replace(Enumerate(), (FileRecord fr, int _, out ReadOnlySpan<byte> content) => {
				content = b;
				return true;
			}, saveIndex);
		}

		/// <summary>
		/// Write files under a directory in batch.
		/// Skip any file which isn't exist on <paramref name="pathOnDisk"/>.
		/// This call <see cref="Save"/> automatically.
		/// </summary>
		/// <param name="nodePath">Path of a DirectoryNode to replace</param>
		/// <param name="pathOnDisk">Path of a folder on disk to read files to replace</param>
		/// <returns>Number of files replaced</returns>
		public static int Replace(Index index, string nodePath, string pathOnDisk, bool saveIndex = true) {
			nodePath = nodePath.TrimEnd('/');
			if (!index.TryFindNode(nodePath, out _))
				throw new ArgumentException("Couldn't find node in Index with given path", nameof(nodePath));
			pathOnDisk = Utils.ExpandPath(pathOnDisk.TrimEnd('/', '\\'));
			if (Path.DirectorySeparatorChar != '/')
				pathOnDisk = pathOnDisk.Replace(Path.DirectorySeparatorChar, '/');
			var trim = pathOnDisk.Length;

			byte[] b = null!;
			IEnumerable<FileRecord> Enumerate() {
				foreach (var p in Directory.EnumerateFiles(pathOnDisk, "*", SearchOption.AllDirectories)) {
					if (index.TryGetFile(nodePath + p[trim..], out var fr)) {
						b = File.ReadAllBytes(p);
						yield return fr;
					}
				}
			}

			return Replace(Enumerate(), (FileRecord fr, int _, out ReadOnlySpan<byte> content) => {
				content = b;
				return true;
			}, saveIndex);
		}
		#endregion Extract/Replace
		/// <summary>
		/// Path to create bundle (Must end with slash)
		/// </summary>
		protected const string CUSTOM_BUNDLE_BASE_PATH = "LibGGPK3/";
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
		protected internal virtual Bundle GetBundleToWrite(out int originalSize) {
			EnsureNotDisposed();
			originalSize = 0;

			static int GetSize(BundleRecord br) {
				var f = br._Files.MaxBy(f => f.Offset);
				if (f is null)
					return 0;
				return f.Offset + f.Size;
			}

			Bundle? b = null;
			lock (CustomBundles) {
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
					originalSize = GetSize(b.Record!);
				}
			}
			return b;
		}

		/*/// <summary>
		/// Get an available bundle with smallest uncompressed_size
		/// </summary>
		public virtual BundleRecord GetSmallestBundle() {
			EnsureNotDisposed();
			var br = _Bundles.MinBy(b => b.UncompressedSize) ?? throw new("Unable to find an available bundle");
			if (br.TryBundle(out var b))
				return b;

			var list = _Bundles.OrderBy(b => b.UncompressedSize);
			foreach (var b in list)
				if (br.TryBundle(out var b))
					return b;
			throw new("Unable to find an available bundle");
		}*/
		/// <summary>
		/// Create a new bundle and add it to <see cref="Bundles"/> using <see cref="IBundleFileFactory.CreateBundle"/>
		/// </summary>
		/// <param name="bundlePath">Relative path of the bundle without ".bundle.bin"</param>
		protected virtual Bundle CreateBundle(string bundlePath) {
			EnsureNotDisposed();
			var count = _Bundles.Length;
			var br = new BundleRecord(bundlePath, 0, this, count);
			var b = new Bundle(bundleFactory.CreateBundle(bundlePath + ".bundle.bin"), br);
			Array.Resize(ref _Bundles, count + 1);
			_Bundles[count] = br;
			baseBundle.UncompressedSize += br.RecordLength; // Hack to prevent MemoryStream from reallocating in next line
			Save();
			return b;
		}

		/// <summary>
		/// Remove all custom bundles created by <see cref="GetBundleToWrite"/>.
		/// </summary>
		/// <remarks>
		/// You shouldn't call this function unless there are some errors caused by these bundles.
		/// Make sure there is no file in these bundles (using <see cref="FileRecord.Redirect"/> on each of <see cref="BundleRecord.Files"/>, or just restore _.index.bin).
		/// </remarks>
		public virtual void RemoveCustomBundles() {
			EnsureNotDisposed();
			foreach (var b in CustomBundles)
				if (b._Files.Count > 0)
					ThrowHelper.Throw<InvalidOperationException>($"Failed to remove bundle: {b.Path}\r\nThere are still some files point to the bundle");
			_Bundles = _Bundles.Where(b => !b._Path.StartsWith(CUSTOM_BUNDLE_BASE_PATH)).ToArray();
			CustomBundles.Clear();
			bundleFactory.RemoveAllCreatedBundle(CUSTOM_BUNDLE_BASE_PATH);
			Save();
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
						if (ch < 91 && ch >= 65)
							hash = (hash ^ (ch + 32)) * FNV_prime; // ToLower
						else
							hash = (hash ^ ch) * FNV_prime;
					}
				return (((hash ^ 43) * FNV_prime) ^ 43) * FNV_prime; // "++" ('+' == 43)
			}
		}
		#endregion NameHashing

		/// <summary>
		/// Enumerate all files under a node (with DFS)
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
		/// Enumerate all files under a node (with DFS), and call <see cref="Directory.CreateDirectory(string)"/> for each folder
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
		/// <seealso cref="Enumerable.GroupBy"/>
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

		public virtual void Dispose() {
			GC.SuppressFinalize(this);
			baseBundle?.Dispose();
			_Bundles = null!;
			_readonlyFiles = null;
			_Root = null;
		}

		~Index() => Dispose();

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
}