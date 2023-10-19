using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace LibBundle3.Records {
	public class BundleRecord {
		protected internal string _Path; // without extension
		public virtual string Path => _Path + ".bundle.bin";
		public virtual int UncompressedSize { get; protected internal set; }

		public virtual int BundleIndex { get; }
		public virtual Index Index { get; }

		protected internal readonly List<FileRecord> _Files = new();
		protected ReadOnlyCollection<FileRecord>? _readonlyFiles;
		public virtual ReadOnlyCollection<FileRecord> Files => _readonlyFiles ??= new(_Files);

		protected internal BundleRecord(string path, int uncompressedSize, Index index, int bundleIndex) {
			_Path = path;
			UncompressedSize = uncompressedSize;
			Index = index;
			BundleIndex = bundleIndex;
		}

		/// <summary>
		/// Try to get the bundle instance with <see cref="Index.FuncReadBundle"/>
		/// </summary>
		/// <remarks>Remember to dispose the bundle after use.</remarks>
		/// <returns>Whether successfully get the instance</returns>
		public virtual bool TryGetBundle([NotNullWhen(true)] out Bundle? bundle) {
			return TryGetBundle(out bundle, out _);
		}
		public virtual bool TryGetBundle([NotNullWhen(true)] out Bundle? bundle, out Exception? exception) {
			exception = null;
			try {
				return (bundle = Index.bundleFactory.GetBundle(this)) != null;
			} catch (Exception ex) {
				exception = ex;
				bundle = null;
				return false;
			}
		}

		protected internal int RecordLength => Encoding.UTF8.GetByteCount(_Path) + (sizeof(int) + sizeof(int));
		/// <summary>
		/// Write the instance to _.index.bin
		/// </summary>
		/// <param name="stream">Stream of _.index.bin</param>
		protected internal virtual void Serialize(Stream stream) {
			var path = Encoding.UTF8.GetBytes(_Path);
			stream.Write(path.Length);
			stream.Write(path, 0, path.Length);
			stream.Write(UncompressedSize);
		}
	}
}