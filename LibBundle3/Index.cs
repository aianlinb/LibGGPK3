using LibBundle3.Nodes;
using LibBundle3.Records;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace LibBundle3 {
	public class Index : IDisposable {
		protected BundleRecord[] _Bundles;
		protected Dictionary<ulong, FileRecord> _Files;
		internal DirectoryRecord[] _Directories;
		public virtual ReadOnlySpan<BundleRecord> Bundles => _Bundles;
		public virtual ReadOnlyDictionary<ulong, FileRecord> Files => new(_Files);

		protected readonly Bundle bundle;
		protected readonly string? baseDirectory;
		protected readonly byte[] directoryBundleData;
		protected int UncompressedSize; // For memory alloc when saving

		/// <summary>
		/// Function to get <see cref="Bundle"/> instance with a <see cref="BundleRecord"/>
		/// </summary>
		public Func<BundleRecord, Bundle> FuncReadBundle = static (br) => new((br.Index.baseDirectory ?? "") + br.Path, br);

		protected DirectoryNode? _Root;
		/// <summary>
		/// Root node of the tree (This will call <see cref="BuildTree"/> with default implementation when first calling).
		/// You can also implement your custom class and use <see cref="BuildTree"/>.
		/// </summary>
		public virtual DirectoryNode Root => _Root ??= (DirectoryNode)BuildTree(DirectoryNode.CreateInstance, FileNode.CreateInstance);

		public delegate IDirectoryNode CreateDirectoryInstance(string name, IDirectoryNode? parent);
		public delegate IFileNode CreateFileInstance(FileRecord record, IDirectoryNode parent);
		/// <summary>
		/// Build a tree to represent the file and directory structure in bundles.
		/// You can implement your custom class or just use the default implement by calling <see cref="Root"/>
		/// </summary>
		/// <param name="createDirectory">Function to create a instance of <see cref="IDirectoryNode"/></param>
		/// <param name="createFile">Function to create a instance of <see cref="IFileNode"/></param>
		/// <returns>The root node of the tree</returns>
		public IDirectoryNode BuildTree(CreateDirectoryInstance createDirectory, CreateFileInstance createFile) {
			var root = createDirectory("", null);
			var files = _Files.Values.OrderBy(f => f.Path);
			foreach (var f in files) {
				var nodeNames = f.Path.Split('/');
				var parent = root;
				var lastDirectory = nodeNames.Length - 1;
				for (var i = 0; i < lastDirectory; ++i) {
					if (parent.Children.Count <= 0 || parent.Children[^1] is not IDirectoryNode dr || dr.Name != nodeNames[i])
						parent.Children.Add(dr = createDirectory(nodeNames[i], parent));
					parent = dr;
				}
				parent.Children.Add(createFile(f, parent));
			}
			return root;
		}

		protected internal static string ExpandPath(string path) {
			if (path.StartsWith('~')) {
				if (path.Length == 1) { // ~
					var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.None);
					if (userProfile != "")
						return Environment.ExpandEnvironmentVariables(userProfile);
				} else if (path[1] is '/' or '\\') { // ~/...
					var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.None);
					if (userProfile != "")
						return Environment.ExpandEnvironmentVariables(userProfile + path[1..]);
				}
				try { // ~username/...
					if (!OperatingSystem.IsWindows()) {
						string bash;
						if (File.Exists("/bin/zsh"))
							bash = "/bin/zsh";
						else if (File.Exists("/bin/var/bash"))
							bash = "/bin/var/bash";
						else if (File.Exists("/bin/bash"))
							bash = "/bin/bash";
						else
							return Environment.ExpandEnvironmentVariables(path);
						var p = Process.Start(new ProcessStartInfo(bash) {
							CreateNoWindow = true,
							ErrorDialog = true,
							RedirectStandardInput = true,
							RedirectStandardOutput = true,
							WindowStyle = ProcessWindowStyle.Hidden
						});
						p!.StandardInput.WriteLine("echo " + path);
						var tmp = p.StandardOutput.ReadLine();
						p.Kill();
						p.Dispose();
						if (!string.IsNullOrEmpty(tmp))
							return tmp;
					}
				} catch { }
			}
			return Environment.ExpandEnvironmentVariables(path);
		}

		/// <param name="filePath">Path to _.index.bin</param>
		/// <param name="parsePaths">Whether to parse the file paths in index. <see langword="false"/> to speed up reading but all <see cref="FileRecord.Path"/> in each of <see cref="_Files"/> will be <see langword="null"/></param>
		public Index(string filePath, bool parsePaths = true) : this(File.Open(filePath = ExpandPath(filePath), FileMode.Open, FileAccess.ReadWrite, FileShare.Read), false, parsePaths) {
			baseDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath)) + "/";
		}

		/// <param name="stream">Stream of _.index.bin</param>
		/// <param name="leaveOpen">If false, close the <paramref name="stream"/> after this instance has been disposed</param>
		/// <param name="parsePaths">Whether to parse the file paths in index. <see langword="false"/> to speed up reading but all <see cref="FileRecord.Path"/> in each of <see cref="_Files"/> will be <see langword="null"/></param>
		public unsafe Index(Stream stream, bool leaveOpen = true, bool parsePaths = true) {
			bundle = new(stream, leaveOpen);
			var data = bundle.ReadData();
			UncompressedSize = data.Length;
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
				_Directories = new DirectoryRecord[directoryCount];
				fixed (DirectoryRecord* drp = _Directories)
					Unsafe.CopyBlockUnaligned(drp, ptr, (uint)directoryCount * 20);
				ptr += directoryCount * 5;
				/*
				for (var i = 0; i < directoryCount; i++) {
					var nameHash = *(ulong*)ptr;
					ptr += 2;
					_Directories[i] = new DirectoryRecord(nameHash, *ptr++, *ptr++, *ptr++);
				}
				*/

				directoryBundleData = data[(int)((byte*)ptr - p)..];
			}

			if (parsePaths)
				ParsePaths();
			return;
		}

		public virtual unsafe void ParsePaths() {
			var directoryBundle = new Bundle(new MemoryStream(directoryBundleData), false);
			var directory = directoryBundle.ReadData();
			directoryBundle.Dispose();
			fixed (byte* p = directory) {
				var ptr = p;
				foreach (var d in _Directories) {
					var temp = new List<IEnumerable<byte>>();
					var Base = false;
					var offset = ptr = p + d.Offset;
					while (ptr - offset <= d.Size - 4) {
						var index = *(int*)ptr;
						ptr += 4;
						if (index == 0) {
							Base = !Base;
							if (Base)
								temp.Clear();
						} else {
							index -= 1;
							/*
							var sb = new StringBuilder();
							byte c;
							while ((c = *ptr++) != 0)
								sb.Append((char)c);
							var str = sb.ToString();
							*/
							var strlen = new ReadOnlySpan<byte>(ptr, d.Size).IndexOf((byte)0); // See String.Ctor(sbyte* value)
							//var str = new string((sbyte*)ptr, 0, strlen); // Unicode string
							IEnumerable<byte> str = new ReadOnlySpan<byte>(ptr, strlen).ToArray(); // UTF8 string

							if (index < temp.Count) {
								str = temp[index].Concat(str);
								if (Base)
									temp.Add(str);
								else {
									var b = str.ToArray();
									fixed (byte* bp = b)
										_Files[NameHash(b)].Path = new string((sbyte*)bp, 0, b.Length);
								}
							} else {
								if (Base)
									temp.Add(str);
								else
									_Files[NameHash(new ReadOnlySpan<byte>(ptr, strlen))].Path = new string((sbyte*)ptr, 0, strlen);
							}
							ptr += strlen + 1; // '\0'
						}
					}
				}
			}
		}

		/// <summary>
		/// Save the _.index.bin. Call this after any file changed
		/// </summary>
		public virtual unsafe void Save() {
			var ms = new MemoryStream(UncompressedSize);
			var bw = new BinaryWriter(ms);

			bw.Write(_Bundles.Length);
			foreach (var b in _Bundles)
				b.Save(bw);

			bw.Write(_Files.Count);
			foreach (var f in _Files.Values)
				f.Save(bw);

			bw.Write(_Directories.Length);
			/*
			foreach (var d in _Directories) {
				bw.Write(d.PathHash);
				bw.Write(d.Offset);
				bw.Write(d.Size);
				bw.Write(d.RecursiveSize);
			}
			*/
			fixed (DirectoryRecord* drp = _Directories)
				bw.Write(new ReadOnlySpan<byte>(drp, _Directories.Length * 20));

			bw.Write(directoryBundleData);

			bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
			UncompressedSize = (int)ms.Length;
			ms.Close();
			bw.Close();
		}

		/// <summary>
		/// Extract files to disk, and skip all files in unavailable bundles
		/// </summary>
		/// <param name="node">Node to extract (recursively)</param>
		/// <param name="pathToSave">Path on disk</param>
		public virtual int Extract(ITreeNode node, string pathToSave) {
			pathToSave = pathToSave.Replace('\\', '/');
			if (node is IFileNode fn) {
				var d = Path.GetDirectoryName(pathToSave);
				if (d != null && !pathToSave.EndsWith('/'))
					Directory.CreateDirectory(d);
				var f = File.Create(pathToSave);
				f.Write(fn.Record.Read().Span);
				f.Flush();
				f.Close();
				return 1;
			}
			pathToSave = pathToSave.TrimEnd('/');

			var list = new List<FileRecord>();
			RecursiveList(node, pathToSave, list, true);
			if (list.Count == 0)
				return 0;

			list.Sort(BundleComparer.Instance);
			pathToSave += "/";
			var trim = ITreeNode.GetPath(node).Length;

			var first = list[0];
			if (list.Count == 1) {
				var f = File.Create(pathToSave + first.Path[trim..]);
				f.Write(first.Read().Span);
				f.Flush();
				f.Close();
				return 1;
			}

			var count = 0;
			var br = first.BundleRecord;
			var err = false;
			try {
				br.Bundle.ReadDataAndCache();
			} catch {
				err = true;
			}
			foreach (var fr in list) {
				if (br != fr.BundleRecord) {
					if (!err)
						br.Bundle.Dispose();
					br = fr.BundleRecord;
					try {
						br.Bundle.ReadDataAndCache();
						err = false;
					} catch {
						err = true;
					}
				}
				if (err)
					continue;
				var f = File.Create(pathToSave + fr.Path[trim..]);
				f.Write(fr.Read().Span);
				f.Flush();
				f.Close();
				++count;
			}
			if (!err)
				br.Bundle.Dispose();
			return count;
		}

		/// <summary>
		/// Extract files with their path, throw when a file couldn't be found
		/// </summary>
		/// <param name="filePaths">Path of files to extract, <see langword="null"/> for all files in <see cref="_Files"/></param>
		/// <returns>KeyValuePairs of path and data of each file</returns>
		public virtual IEnumerable<KeyValuePair<string, ReadOnlyMemory<byte>>> Extract(IEnumerable<string>? filePaths) {
			var list = filePaths == null ? new List<FileRecord>(_Files.Values) : filePaths.Select(s => _Files[NameHash(s.Replace('\\', '/').TrimEnd('/'))]).ToList();
			if (list.Count == 0)
				yield break;
			list.Sort(BundleComparer.Instance);

			var first = list[0];
			if (list.Count == 1) {
				yield return new(first.Path, first.Read());
				yield break;
			}

			var br = first.BundleRecord;
			br.Bundle.ReadDataAndCache();
			foreach (var fr in list) {
				if (br != fr.BundleRecord) {
					br.Bundle.Dispose();
					br = fr.BundleRecord;
					br.Bundle.ReadDataAndCache();
				}
				yield return new(fr.Path, fr.Read());
			}
			br.Bundle.Dispose();
		}

		/// <summary>
		/// Replace files from disk
		/// </summary>
		/// <param name="node">Node to replace (recursively)</param>
		/// <param name="pathToLoad">Path on disk</param>
		/// <param name="dontChangeBundle">Whether to force all files to be written to their respective original bundle</param>
		public virtual int Replace(ITreeNode node, string pathToLoad, bool dontChangeBundle = false) {
			if (node is IFileNode fn) {
				fn.Record.Write(File.ReadAllBytes(pathToLoad));
				Save();
				return 1;
			}

			pathToLoad = pathToLoad.Replace('\\', '/').TrimEnd('/');

			var list = new List<FileRecord>();
			RecursiveList(node, pathToLoad, list);
			if (list.Count == 0)
				return 0;

			pathToLoad += "/";
			var trim = ITreeNode.GetPath(node).Length;

			var first = list[0];
			if (list.Count == 1) {
				first.Write(File.ReadAllBytes(pathToLoad + first.Path[trim..]));
				Save();
				return 1;
			}

			var count = 0;
			if (dontChangeBundle) {
				list.Sort(BundleComparer.Instance);

				var br = first.BundleRecord;
				var ms = new MemoryStream(br.Bundle.UncompressedSize);
				ms.Write(br.Bundle.ReadData());
				foreach (var fr in list) {
					if (br != fr.BundleRecord) {
						br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
						ms.Close();
						br = fr.BundleRecord;
						ms = new(br.Bundle.UncompressedSize);
						ms.Write(br.Bundle.ReadData());
					}
					var b = File.ReadAllBytes(pathToLoad + fr.Path[trim..]);
					ms.Write(b);
					fr.Redirect(br, (int)ms.Length, b.Length);
					++count;
				}
				br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
				ms.Close();
			} else {
				var maxSize = 209715200; //200MB
				var br = GetSmallestBundle();
				while (br.Bundle.UncompressedSize >= maxSize)
					maxSize *= 2;
				var ms = new MemoryStream(br.Bundle.UncompressedSize);
				ms.Write(br.Bundle.ReadData());
				foreach (var fr in list) {
					if (ms.Length >= maxSize) {
						br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
						ms.Close();
						br = GetSmallestBundle();
						while (br.Bundle.UncompressedSize >= maxSize)
							maxSize *= 2;
						ms = new(br.Bundle.UncompressedSize);
						ms.Write(br.Bundle.ReadData());
					}
					var b = File.ReadAllBytes(pathToLoad + fr.Path);
					ms.Write(b);
					fr.Redirect(br, (int)ms.Length, b.Length);
					++count;
				}
				br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
				ms.Close();
			}
			if (count != 0)
				Save();
			return count;
		}

		public delegate ReadOnlySpan<byte> FuncGetData(string filePath);
		/// <summary>
		/// Replace files with their path, throw when a file couldn't be found
		/// </summary>
		/// <param name="filePaths">Path of files to replace, <see langword="null"/> for all files in <see cref="_Files"/></param>
		/// <param name="funcGetDataFromFilePath">For getting new data with the path of the file</param>
		/// <param name="dontChangeBundle">Whether to force all files to be written to their respective original bundle</param>
		public virtual void Replace(IEnumerable<string>? filePaths, FuncGetData funcGetDataFromFilePath, bool dontChangeBundle = false) {
			var list = filePaths == null ? new List<FileRecord>(_Files.Values) : filePaths.Select(s => _Files[NameHash(s.Replace('\\', '/').TrimEnd('/'))]).ToList();
			if (list.Count == 0)
				return;

			var first = list[0];
			if (list.Count == 1) {
				first.Write(funcGetDataFromFilePath(first.Path));
				Save();
				return;
			}

			if (dontChangeBundle) {
				list.Sort(BundleComparer.Instance);

				var br = first.BundleRecord;
				var ms = new MemoryStream(br.Bundle.UncompressedSize);
				ms.Write(br.Bundle.ReadData());
				foreach (var fr in list) {
					if (br != fr.BundleRecord) {
						br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
						ms.Close();
						br = fr.BundleRecord;
						ms = new(br.Bundle.UncompressedSize);
						ms.Write(br.Bundle.ReadData());
					}
					var b = funcGetDataFromFilePath(fr.Path);
					ms.Write(b);
					fr.Redirect(br, (int)ms.Length, b.Length);
				}
				br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
				ms.Close();
			} else {
				var maxSize = 209715200; //200MB
				var br = GetSmallestBundle();
				while (br.Bundle.UncompressedSize >= maxSize)
					maxSize *= 2;
				var ms = new MemoryStream(br.Bundle.UncompressedSize);
				ms.Write(br.Bundle.ReadData());
				foreach (var fr in list) {
					if (ms.Length >= maxSize) {
						br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
						ms.Close();
						br = GetSmallestBundle();
						while (br.Bundle.UncompressedSize >= maxSize)
							maxSize *= 2;
						ms = new(br.Bundle.UncompressedSize);
						ms.Write(br.Bundle.ReadData());
					}
					var b = funcGetDataFromFilePath(fr.Path);
					fr.Redirect(br, (int)ms.Length, b.Length);
					ms.Write(b);
				}
				br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
				ms.Close();
			}
			Save();
		}

		/// <summary>
		/// Patch with a zip file and ignore its files that couldn't be found
		/// </summary>
		public virtual void Replace(IEnumerable<ZipArchiveEntry> zipEntries) {
			var maxSize = 209715200; //200MB
			var br = GetSmallestBundle();
			while (br.Bundle.UncompressedSize >= maxSize)
				maxSize *= 2;
			var ms = new MemoryStream(br.Bundle.UncompressedSize);
			ms.Write(br.Bundle.ReadData());
			foreach (var zip in zipEntries) {
				if (zip.FullName.EndsWith('/'))
					continue;
				if (!_Files.TryGetValue(NameHash(zip.FullName), out var f))
					continue;
				if (ms.Length >= maxSize) {
					br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
					ms.Close();
					br = GetSmallestBundle();
					while (br.Bundle.UncompressedSize >= maxSize)
						maxSize *= 2;
					ms = new MemoryStream(br.Bundle.UncompressedSize);
					ms.Write(br.Bundle.ReadData());
				}
				var b = zip.Open();
				f.Redirect(br, (int)ms.Length, (int)zip.Length);
				b.CopyTo(ms);
			}
			br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
			ms.Close();

			Save();
		}

		/// <summary>
		/// Get a FileRecord from its path (This won't cause the tree building),
		/// The separator of the <paramref name="path"/> must be forward slash '/'
		/// </summary>
		/// <returns>Null when not found</returns>
		public virtual bool TryGetFile(string path, [NotNullWhen(true)] out FileRecord? file) {
			return _Files.TryGetValue(NameHash(path), out file);
		}

		/// <param name="path">Relative path under <paramref name="root"/></param>
		/// <param name="root">Node to start searching, or <see langword="null"/> for <see cref="Root"/></param>
		/// <returns>The node found, or <see langword="null"/> when not found</returns>
		public virtual ITreeNode? FindNode(string path, DirectoryNode? root = null) {
			root ??= Root;
			var SplittedPath = path.Split('/', '\\');
			foreach (var name in SplittedPath) {
				if (name == "")
					return root;
				var next = root.Children.FirstOrDefault(n => n.Name == name);
				if (next is not DirectoryNode dn)
					return next;
				root = dn;
			}
			return root;
		}

		/// <summary>
		/// Get an available bundle with smallest uncompressed_size
		/// </summary>
		public virtual BundleRecord GetSmallestBundle() {
			if (_Bundles == null || _Bundles.Length == 0)
				throw new("Unable to find an available bundle");
			var bundles = (BundleRecord[])_Bundles.Clone();
			Array.Sort(bundles, (x, y) => x.UncompressedSize - y.UncompressedSize);
			for (var i = 0; i < bundles.Length; ++i)
				try {
					_ = bundles[i].Bundle;
					return bundles[i];
				} catch { }
			throw new("Unable to find an available bundle");
		}

		public virtual void Dispose() {
			GC.SuppressFinalize(this);
			bundle.Dispose();
		}

		~Index() {
			Dispose();
		}

		/// <param name="node">Node to start recursive</param>
		/// <param name="path">Path on disk which don't end with a slash</param>
		/// <param name="list">A collection to save the results</param>
		/// <param name="createDirectory">Whether to create the directories of the files</param>
		public static void RecursiveList(ITreeNode node, string path, ICollection<FileRecord> list, bool createDirectory = false) {
			if (node is IFileNode fn)
				list.Add(fn.Record);
			else if (node is IDirectoryNode dn) {
				if (createDirectory)
					Directory.CreateDirectory(path);
				foreach (var n in dn.Children)
					RecursiveList(n, path + "/" + n.Name, list, createDirectory);
			}
		}

		/// <summary>
		/// Get the hash of a file path
		/// </summary>
		public unsafe ulong NameHash(ReadOnlySpan<char> name) {
			if (_Directories[0].PathHash == 0xF42A94E69CFF42FE || name[^1] != '/') {
				var c = stackalloc char[name.Length];
				var count = name.ToLowerInvariant(new(c, name.Length));
				var b = stackalloc byte[count];
				count = Encoding.UTF8.GetBytes(c, count, b, count);
				return NameHash(new ReadOnlySpan<byte>(b, count));
			} else
				fixed(char* p = name) {
					var b = stackalloc byte[name.Length];
					var count = Encoding.UTF8.GetBytes(p, name.Length, b, name.Length);
					return NameHash(new ReadOnlySpan<byte>(b, count));
				}
		}

		/// <summary>
		/// Get the hash of a file path,
		/// <paramref name="utf8Str"/> must be lowercased unless it comes from ggpk before patch 3.21.2
		/// </summary>
		protected unsafe ulong NameHash(ReadOnlySpan<byte> utf8Str) {
			return _Directories[0].PathHash switch {
				0xF42A94E69CFF42FE => MurmurHash64A(utf8Str), // since poe 3.21.2 patch
				0x07E47507B4A92E53 => FNV1a64Hash(utf8Str),
				_ => throw new("Unable to detect namehash algorithm"),
			};
		}

		/// <summary>
		/// Get the hash of a file path, <paramref name="utf8Str"/> must be lowercased
		/// </summary>
		protected static unsafe ulong MurmurHash64A(ReadOnlySpan<byte> utf8Str, ulong seed = 0x1337B33F) {
			if (utf8Str[^1] == '/') // TrimEnd('/')
				utf8Str = utf8Str[..^1];
			var length = utf8Str.Length;

			const ulong m = 0xC6A4A7935BD1E995UL;
			const int r = 47;

			ulong h = seed ^ ((ulong)length * m);

			fixed (byte* data = utf8Str) {
				ulong* p = (ulong*)data;
				int numberOfLoops = length >> 3; // div 8
				while (numberOfLoops-- != 0) {
					ulong k = *p++;
					k *= m;
					k ^= k >> r;
					k *= m;

					h ^= k;
					h *= m;
				}

				int remainingBytes = length & 0b111; // mod 8
				if (remainingBytes != 0) {
					int offset = (8 - remainingBytes) << 3; // mul 8
					h ^= *p & (0xFFFFFFFFFFFFFFFFUL >> offset);
					h *= m;
				}
			}

			h ^= h >> r;
			h *= m;
			h ^= h >> r;

			return h;
		}

		/// <summary>
		/// Get the hash of a file path with ggpk before patch 3.21.2
		/// </summary>
		protected static unsafe ulong FNV1a64Hash(ReadOnlySpan<byte> utf8Str) {
			var hash = 0xCBF29CE484222325UL;
			if (utf8Str[^1] == '/') {
				utf8Str = utf8Str[..^1]; // TrimEnd('/')
				foreach (ulong by in utf8Str)
					hash = (hash ^ by) * 0x100000001B3UL;
			} else
				foreach (ulong by in utf8Str) {
					if (by < 91 && by >= 65)
						hash = (hash ^ (by + 32)) * 0x100000001B3UL; // ToLower
					else
						hash = (hash ^ by) * 0x100000001B3UL;
				}
			return (((hash ^ 43) * 0x100000001B3UL) ^ 43) * 0x100000001B3UL; // "++" ('+'==43)
		}

		/// <summary>
		/// For sorting FileRecords with their bundle
		/// </summary>
		public sealed class BundleComparer : IComparer<FileRecord> {
			public static readonly BundleComparer Instance = new();
			private BundleComparer() { }
#pragma warning disable CS8767
			public int Compare(FileRecord x, FileRecord y) {
				return x.BundleRecord.BundleIndex - y.BundleRecord.BundleIndex;
			}
		}
	}
}