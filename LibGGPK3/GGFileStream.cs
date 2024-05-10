using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using LibGGPK3.Records;

using SystemExtensions;

namespace LibGGPK3 {
	/// <summary>
	/// <see cref="Stream"/> to access a file in <see cref="GGPK"/>
	/// </summary>
	/// <remarks>
	/// Use this class only when you have to use a <see cref="Stream"/>,
	/// otherwise use <see cref="FileRecord.Read()"/> and <see cref="FileRecord.Write"/> instead for better performance.
	/// </remarks>
	public class GGFileStream : Stream {
		private static readonly Dictionary<FileRecord, GGFileStream> instances = [];

		/// <summary>
		/// The <see cref="FileRecord"/> this stream created with
		/// </summary>
		public FileRecord Record { get; }

		protected MemoryStream? _Buffer;

		[MemberNotNull(nameof(_Buffer))]
		protected virtual MemoryStream Buffer {
			get {
				if (_Buffer is null) {
					_Buffer = new(Record.DataLength);
					_Buffer.Write(Record.Read(), 0, Record.DataLength);
					_Buffer.Position = _Position;
				}
				return _Buffer;
			}
		}

		protected bool Modified;

		/// <summary>
		/// Create a <see cref="GGFileStream"/> with a existing <see cref="FileRecord"/>
		/// </summary>
		/// <remarks>Each <see cref="FileRecord"/> can only have one instance of <see cref="GGFileStream"/> at the same time</remarks>
		/// <exception cref="InvalidOperationException">Thrown when an instance of <see cref="GGFileStream"/> is exist with the <paramref name="record"/></exception>
		public GGFileStream(FileRecord record) {
			ArgumentNullException.ThrowIfNull(record);
			Record = record;
			lock (instances)
				if (!instances.TryAdd(record, this))
					ThrowHelper.Throw<InvalidOperationException>("An instance of GGFileStream is already created for this FileRecord");
		}

		/// <summary>
		/// Write all changes to GGPK.
		/// Don't call this function before completing all modifications to avoid unnecessary repeated writing and waste of space.
		/// </summary>
		public override void Flush() {
			if (!Modified || _Buffer is null)
				return;
			Record.Write(new(_Buffer.GetBuffer(), 0, (int)_Buffer.Length));
			Modified = false;
		}

		public override int Read(byte[] buffer, int offset, int count) {
			if (_Buffer is not null)
				return _Buffer.Read(buffer, offset, count);
			lock (Record.Ggpk.baseStream) {
				Record.Ggpk.baseStream.Position = Record.DataOffset + _Position;
				var read = Record.Ggpk.baseStream.Read(buffer, offset, count);
				_Position += read;
				return read;
			}
		}

		public override int Read(Span<byte> buffer) {
			if (_Buffer is not null)
				return _Buffer.Read(buffer);
			lock (Record.Ggpk.baseStream) {
				Record.Ggpk.baseStream.Position = Record.DataOffset + _Position;
				var read = Record.Ggpk.baseStream.Read(buffer);
				_Position += read;
				return read;
			}
		}

		public override int ReadByte() {
			if (_Buffer is not null)
				return _Buffer.ReadByte();
			lock (Record.Ggpk.baseStream) {
				Record.Ggpk.baseStream.Position = Record.DataOffset + _Position;
				var value = Record.Ggpk.baseStream.ReadByte();
				++_Position;
				return value;
			}
		}

		public override long Seek(long offset, SeekOrigin origin) {
			var pos = origin switch {
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => _Position + offset,
				SeekOrigin.End => Length + offset,
				_ => throw ThrowHelper.ArgumentOutOfRange(origin),
			};
			if (pos < 0)
				ThrowHelper.Throw<IOException>("Attempted to seek before the beginning of the stream");
			else if (_Buffer is not null)
				_Buffer.Position = pos;
			return _Position = pos;
		}

		/// <summary>
		/// This won't affect the actual file before <see cref="Flush"/> or <see cref="Dispose"/>
		/// </summary>
		public override void SetLength(long value) {
			if (value == Length)
				return;
			Buffer.SetLength(value);
			Modified = true;
			_Position = _Buffer.Position;
		}

		/// <summary>
		/// Won't affect the actual file before <see cref="Flush"/> or <see cref="Dispose"/>
		/// </summary>
		public override void Write(byte[] buffer, int offset, int count) {
			if (count == 0)
				return;
			Buffer.Write(buffer, offset, count);
			Modified = true;
			_Position = _Buffer.Position;
		}

		/// <summary>
		/// Won't affect the actual file before <see cref="Flush"/> or <see cref="Dispose"/>
		/// </summary>
		public override void Write(ReadOnlySpan<byte> buffer) {
			if (buffer.IsEmpty)
				return;
			Buffer.Write(buffer);
			Modified = true;
			_Position = _Buffer.Position;
		}

		/// <summary>
		/// Won't affect the actual file before <see cref="Flush"/> or <see cref="Dispose"/>
		/// </summary>
		public override void WriteByte(byte value) {
			Buffer.WriteByte(value);
			Modified = true;
			_Position = _Buffer.Position;
		}

		public override void CopyTo(Stream destination, int bufferSize) {
			if (_Buffer is not null) {
				_Buffer.CopyTo(destination, bufferSize);
				_Position = _Buffer.Position;
			} else {
				lock (Record.Ggpk.baseStream) {
					Record.Ggpk.baseStream.Position = Record.DataOffset + _Position;
					Record.Ggpk.baseStream.CopyTo(destination, bufferSize);
					_Position = Record.Ggpk.baseStream.Position - Record.DataOffset;
				}
			}
		}

		public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) {
			if (_Buffer is not null)
				return _Buffer.CopyToAsync(destination, bufferSize, cancellationToken).ContinueWith(_ => _Position = _Buffer.Position);
			lock (Record.Ggpk.baseStream) {
				Record.Ggpk.baseStream.Position = Record.DataOffset + _Position;
				return Record.Ggpk.baseStream.CopyToAsync(destination, bufferSize, cancellationToken).ContinueWith(_ => _Position = Record.Ggpk.baseStream.Position - Record.DataOffset);
			}
		}

		public override bool CanRead => Record.Ggpk.baseStream.CanRead;

		/// <returns><see langword="true"/></returns>
		public override bool CanSeek => true;

		public override bool CanWrite => Record.Ggpk.baseStream.CanWrite;

		public override long Length => _Buffer?.Length ?? Record.DataLength;

		/// <summary>Temporarily store the position before <see cref="_Buffer"/> is initialized</summary>
		protected long _Position;
		public override long Position {
			get => _Buffer?.Position ?? _Position;
			set {
				if (_Buffer is not null)
					_Buffer.Position = value;
				else if (value < 0)
					ThrowHelper.ThrowArgumentOutOfRange(value, "Non-negative number required");
				_Position = value;
			}
		}

		/// <param name="disposing"><see langword="true"/> to call <see cref="Flush"/> first</param>
		protected override void Dispose(bool disposing) {
			if (disposing)
				Flush();
			_Buffer?.Dispose();
			lock (instances)
				instances.Remove(Record);
		}

		~GGFileStream() {
			Dispose(false);
		}
	}
}