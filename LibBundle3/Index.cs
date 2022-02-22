using LibBundle3.Nodes;
using LibBundle3.Records;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace LibBundle3 {
	public class Index : IDisposable {
		public BundleRecord[] Bundles;
		public Dictionary<ulong, FileRecord> Files;
		public DirectoryRecord[] Directories;

		protected readonly string? baseDirectory;
		protected readonly Bundle bundle;
		protected readonly byte[] directoryBundleData;
		protected int UncompressedSize; // For memory alloc when saving

		protected DirectoryNode? _Root;
		/// <summary>
		/// Root node of the tree (This will cause the tree building when first calling)
		/// </summary>
		public virtual DirectoryNode Root {
			get {
				if (_Root == null) {
					_Root = new("");
					foreach (var f in Files.Values) {
						var paths = f.Path.Split('/');
						var parent = Root;
						var lastDirectory = paths.Length - 1;
						for (var i = 0; i < lastDirectory; ++i) {
							if (parent.Children.FirstOrDefault(n => n.Name == paths[i]) is not DirectoryNode next) {
								next = new DirectoryNode(paths[i]);
								parent.Children.Add(next);
								next.Parent = parent;
							}
							parent = next;
						}
						parent.Children.Add(new FileNode(f));
					}
				}
				return _Root;
			}
		}

		/// <summary>
		/// Function to get <see cref="Bundle"/> instance with a <see cref="BundleRecord"/>
		/// </summary>
		public Func<BundleRecord, Bundle> FuncReadBundle = static (br) => new((br.Index.baseDirectory ?? "") + br.Path + ".bundle.bin");

		/// <param name="filePath">Path to _.index.bin</param>
		/// <param name="parsePaths">Whether to parse the file paths in index. "<see langword="false"/>" to speed up reading but all <see cref="FileRecord.Path"/> and <see cref="FileRecord.DirectoryRecord"/> in each of <see cref="Files"/> will be <see langword="null"/></param>
		public Index(string filePath, bool parsePaths = true) : this(File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read), false, parsePaths) {
			baseDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath));
			if (baseDirectory != null)
				baseDirectory += "/";
		}

		/// <param name="stream">Stream of _.index.bin</param>
		/// <param name="leaveOpen">If false, close the <paramref name="stream"/> after this instance has been disposed</param>
		/// <param name="parsePaths">Whether to parse the file paths in index. "<see langword="false"/>" to speed up reading but all <see cref="FileRecord.Path"/> and <see cref="FileRecord.DirectoryRecord"/> in each of <see cref="Files"/> will be <see langword="null"/></param>
		public unsafe Index(Stream stream, bool leaveOpen = true, bool parsePaths = true) {
			bundle = new(stream, leaveOpen);
			var data = bundle.ReadData();
			UncompressedSize = data.Length;
			var UTF8 = Encoding.UTF8;
			fixed (byte* p = data) {
				var ptr = (int*)p;

				var bundleCount = *ptr++;
				Bundles = new BundleRecord[bundleCount];
				for (var i = 0; i < bundleCount; i++) {
					var pathLength = *ptr++;
					var path = UTF8.GetString((byte*)ptr, pathLength);
					ptr = (int*)((byte*)ptr + pathLength);
					var uncompressedSize = *ptr++;
					Bundles[i] = new BundleRecord(path, uncompressedSize, this) { BundleIndex = i };
				}

				var fileCount = *ptr++;
				Files = new(fileCount);
				for (var i = 0; i < fileCount; i++) {
					var nameHash = *(ulong*)ptr;
					ptr += 2;
					var f = new FileRecord(nameHash, *ptr++, *ptr++, *ptr++);
					Files[nameHash] = f;
					var b = Bundles[f.BundleIndex];
					f.BundleRecord = b;
					b.Files.Add(f);
				}

				var directoryCount = *ptr++;
				Directories = new DirectoryRecord[directoryCount];
				for (var i = 0; i < directoryCount; i++) {
					var nameHash = *(ulong*)ptr;
					ptr += 2;
					Directories[i] = new DirectoryRecord(nameHash, *ptr++, *ptr++, *ptr++);
				}

				directoryBundleData = data[(int)((byte*)ptr - p)..];
			}

			if (!parsePaths)
				return;

			var directoryBundle = new Bundle(new MemoryStream(directoryBundleData), false);
			var directory = directoryBundle.ReadData();
			directoryBundle.Dispose();
			fixed (byte* p = directory) {
				var ptr = p;
				foreach (var d in Directories) {
					var temp = new List<string>();
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
							var sb = new StringBuilder();
							byte c;
							while ((c = *ptr++) != 0)
								sb.Append((char)c);
							var str = sb.ToString();
							if (index < temp.Count)
								str = temp[index] + str;
							if (Base)
								temp.Add(str);
							else {
								var f = Files[FNV1a64Hash(str)];
								f._Path = str;
								d.Children.Add(f);
								f.DirectoryRecord = d;
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// Save the _.index.bin. Call this after any file changed
		/// </summary>
		public virtual void Save() {
			var ms = new MemoryStream(UncompressedSize);
			var bw = new BinaryWriter(ms);
			var UTF8 = Encoding.UTF8;

			bw.Write(Bundles.Length);
			foreach (var b in Bundles) {
				var path = UTF8.GetBytes(b.Path);
				bw.Write(path.Length);
				bw.Write(path, 0, path.Length);
				bw.Write(b.UncompressedSize);
			}

			bw.Write(Files.Count);
			foreach (var f in Files.Values) {
				bw.Write(f.PathHash);
				bw.Write(f.BundleIndex);
				bw.Write(f.Offset);
				bw.Write(f.Size);
			}

			bw.Write(Directories.Length);
			foreach (var d in Directories) {
				bw.Write(d.PathHash);
				bw.Write(d.Offset);
				bw.Write(d.Size);
				bw.Write(d.RecursiveSize);
			}

			bw.Write(directoryBundleData);

			bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
			bw.Close();
		}

		/// <summary>
		/// Extract files to disk, and skip all files in unavailable bundles
		/// </summary>
		/// <param name="node">Node to extract (recursively)</param>
		/// <param name="pathToSave">Path on disk</param>
		public virtual void Extract(BaseNode node, string pathToSave) {
			pathToSave = pathToSave.Replace('\\', '/');
			if (node is FileNode fn) {
				var d = Path.GetDirectoryName(pathToSave);
				if (d != null && !pathToSave.EndsWith('/'))
					Directory.CreateDirectory(d);
				var f = File.Create(pathToSave);
				f.Write(fn.Record.Read().Span);
				f.Flush();
				f.Close();
				return;
			}
			pathToSave = pathToSave.TrimEnd('/');

			var list = new List<FileRecord>();
			RecursiveList(node, pathToSave, list, true);
			if (list.Count == 0)
				return;

			list.Sort(BundleComparer.Instance);
			pathToSave += "/";
			var trim = node.GetPath().Length;

			var first = list[0];
			if (list.Count == 1) {
				var f = File.Create(pathToSave + first.Path[trim..]);
				f.Write(first.Read().Span);
				f.Flush();
				f.Close();
				return;
			}

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
						br.Bundle.CachedData = null;
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
				var f = File.Create(pathToSave + first.Path[trim..]);
				f.Write(fr.Read().Span);
				f.Flush();
				f.Close();
			}
			if (!err)
				br.Bundle.CachedData = null;
		}

		/// <summary>
		/// Extract files with their path, throw when a file couldn't be found
		/// </summary>
		/// <param name="filePaths">Path of files to extract, <see langword="null"/> for all files in <see cref="Files"/></param>
		/// <returns>KeyValuePairs of path and data of each file</returns>
		public virtual IEnumerable<KeyValuePair<string, Memory<byte>>> Extract(IEnumerable<string>? filePaths) {
			var list = filePaths == null ? new List<FileRecord>(Files.Values) : filePaths.Select(s => Files[FNV1a64Hash(s.Replace('\\', '/'))]).ToList();
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
					br.Bundle.CachedData = null;
					br = fr.BundleRecord;
					br.Bundle.ReadDataAndCache();
				}
				yield return new(fr.Path, fr.Read());
			}
			br.Bundle.CachedData = null;
		}

		/// <summary>
		/// Replace files from disk
		/// </summary>
		/// <param name="node">Node to replace (recursively)</param>
		/// <param name="pathToLoad">Path on disk</param>
		/// <param name="dontChangeBundle">Whether to force all files to be written to their respective original bundle</param>
		public virtual void Replace(BaseNode node, string pathToLoad, bool dontChangeBundle = false) {
			if (node is FileNode fn) {
				fn.Record.Write(File.ReadAllBytes(pathToLoad));
				Save();
				return;
			}

			pathToLoad = pathToLoad.Replace('\\', '/').TrimEnd('/');

			var list = new List<FileRecord>();
			RecursiveList(node, pathToLoad, list);
			if (list.Count == 0)
				return;

			pathToLoad += "/";
			var trim = node.GetPath().Length;

			var first = list[0];
			if (list.Count == 1) {
				first.Write(File.ReadAllBytes(pathToLoad + first.Path[trim..]));
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
						br.UncompressedSize = br.Bundle.UncompressedSize;
						br = fr.BundleRecord;
						ms = new(br.Bundle.UncompressedSize);
						ms.Write(br.Bundle.ReadData());
					}
					var b = File.ReadAllBytes(pathToLoad + fr.Path[trim..]);
					ms.Write(b);
					fr.Offset = (int)ms.Length;
					fr.Size = b.Length;
				}
				br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
				ms.Close();
				br.UncompressedSize = br.Bundle.UncompressedSize;
			} else {
				var maxSize = 200000000; //200MB
				var br = GetSmallestBundle();
				while (br.Bundle.UncompressedSize >= maxSize)
					maxSize *= 2;
				var ms = new MemoryStream(br.Bundle.UncompressedSize);
				ms.Write(br.Bundle.ReadData());
				foreach (var fr in list) {
					if (ms.Length >= maxSize) {
						br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
						ms.Close();
						br.UncompressedSize = br.Bundle.UncompressedSize;
						br = GetSmallestBundle();
						while (br.Bundle.UncompressedSize >= maxSize)
							maxSize *= 2;
						ms = new(br.Bundle.UncompressedSize);
						ms.Write(br.Bundle.ReadData());
					}
					var b = File.ReadAllBytes(pathToLoad + fr.Path);
					fr.Redirect(br, (int)ms.Length, b.Length);
					ms.Write(b);
				}
				br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
				ms.Close();
				br.UncompressedSize = br.Bundle.UncompressedSize;
			}
			Save();
		}

		public delegate ReadOnlySpan<byte> FuncGetData(string filePath);
		/// <summary>
		/// Replace files with their path, throw when a file couldn't be found
		/// </summary>
		/// <param name="filePaths">Path of files to replace, <see langword="null"/> for all files in <see cref="Files"/></param>
		/// <param name="funcGetDataFromFilePath">For getting new data with the path of the file</param>
		/// <param name="dontChangeBundle">Whether to force all files to be written to their respective original bundle</param>
		public virtual void Replace(IEnumerable<string>? filePaths, FuncGetData funcGetDataFromFilePath, bool dontChangeBundle = false) {
			var list = filePaths == null ? new List<FileRecord>(Files.Values) : filePaths.Select(s => Files[FNV1a64Hash(s.Replace('\\', '/'))]).ToList();
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
						br.UncompressedSize = br.Bundle.UncompressedSize;
						br = fr.BundleRecord;
						ms = new(br.Bundle.UncompressedSize);
						ms.Write(br.Bundle.ReadData());
					}
					var b = funcGetDataFromFilePath(fr.Path);
					ms.Write(b);
					fr.Offset = (int)ms.Length;
					fr.Size = b.Length;
				}
				br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
				ms.Close();
				br.UncompressedSize = br.Bundle.UncompressedSize;
			} else {
				var maxSize = 200000000; //200MB
				var br = GetSmallestBundle();
				while (br.Bundle.UncompressedSize >= maxSize)
					maxSize *= 2;
				var ms = new MemoryStream(br.Bundle.UncompressedSize);
				ms.Write(br.Bundle.ReadData());
				foreach (var fr in list) {
					if (ms.Length >= maxSize) {
						br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
						ms.Close();
						br.UncompressedSize = br.Bundle.UncompressedSize;
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
				br.UncompressedSize = br.Bundle.UncompressedSize;
			}
			Save();
		}

		/// <summary>
		/// Patch with a zip file and ignore its files that couldn't be found
		/// </summary>
		public virtual void Replace(IEnumerable<ZipArchiveEntry> zipEntries) {
			var maxSize = 200000000; //200MB
			var br = GetSmallestBundle();
			while (br.Bundle.UncompressedSize >= maxSize)
				maxSize *= 2;
			var ms = new MemoryStream(br.Bundle.UncompressedSize);
			ms.Write(br.Bundle.ReadData());
			foreach (var zip in zipEntries) {
				if (zip.FullName.EndsWith('/'))
					continue;
				if (!Files.TryGetValue(FNV1a64Hash(zip.FullName), out var f))
					continue;
				if (ms.Length >= maxSize) {
					br.Bundle.SaveData(new(ms.GetBuffer(), 0, (int)ms.Length));
					ms.Close();
					br.UncompressedSize = br.Bundle.UncompressedSize;
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
			br.UncompressedSize = br.Bundle.UncompressedSize;

			Save();
		}

		/// <summary>
		/// Get a FileRecord from its path (This won't cause the tree building),
		/// The separator of the <paramref name="path"/> must be forward slash '/'
		/// </summary>
		/// <returns>Null when not found</returns>
		public virtual bool TryGetFile(string path, out FileRecord? file) {
			return Files.TryGetValue(FNV1a64Hash(path), out file);
		}

		/// <param name="path">Relative path below <paramref name="parent"/></param>
		/// <param name="parent">Node to start searching</param>
		public virtual BaseNode? FindNode(string path, DirectoryNode? parent = null) {
			parent ??= Root;
			var SplittedPath = path.Split('/', '\\');
			foreach (var name in SplittedPath) {
				var next = parent.Children.FirstOrDefault(n => n.Name == name);
				if (next is not DirectoryNode dn)
					return next;
				parent = dn;
			}
			return parent;
		}

		/// <summary>
		/// Get a available bundle with smallest uncompressed_size
		/// </summary>
		public virtual BundleRecord GetSmallestBundle() {
			if (Bundles == null || Bundles.Length == 0)
				throw new("Unable to find an available bundle");
			var bundles = (BundleRecord[])Bundles.Clone();
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
		public static void RecursiveList(BaseNode node, string path, ICollection<FileRecord> list, bool createDirectory = false) {
			if (node is FileNode fn)
				list.Add(fn.Record);
			else if (node is DirectoryNode dn) {
				if (createDirectory)
					Directory.CreateDirectory(path);
				foreach (var n in dn.Children)
					RecursiveList(n, path + "/" + n.Name, list, createDirectory);
			}
		}

		/// <summary>
		/// Get the hash of a file path
		/// </summary>
		public static ulong FNV1a64Hash(string str) {
			if (str.EndsWith('/'))
				str = str.TrimEnd('/') + "++";
			else
				str = str.ToLower() + "++";

			var bs = Encoding.UTF8.GetBytes(str);
			var hash = 0xCBF29CE484222325UL;
			foreach (var by in bs)
				hash = (hash ^ by) * 0x100000001B3UL;
			// Equals to: bs.Aggregate(0xCBF29CE484222325UL, (current, by) => (current ^ by) * 0x100000001B3UL);
			return hash;
		}

		/// <summary>
		/// For sorting FileRecords with their bundle
		/// </summary>
		public sealed class BundleComparer : IComparer<FileRecord> {
			public static readonly BundleComparer Instance = new();
			private BundleComparer() {
			}
#pragma warning disable CS8767
			public int Compare(FileRecord x, FileRecord y) {
				return x.BundleRecord.BundleIndex - y.BundleRecord.BundleIndex;
			}
		}
	}
}