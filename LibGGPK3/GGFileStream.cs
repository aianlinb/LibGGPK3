using LibGGPK3.Records;
using System;
using System.IO;

namespace LibGGPK3 {
	/// <summary>
	/// Stream to access a file in <see cref="GGPK"/>, use <see cref="FileRecord.ReadFileContent"/> and <see cref="FileRecord.ReplaceContent"/> for better performance
	/// </summary>
	public class GGFileStream : Stream {
		public FileRecord Record { get; }

		protected MemoryStream? _Buffer;
		protected virtual MemoryStream Buffer {
			get {
				if (_Buffer == null) {
					_Buffer = new(Record.DataLength);
					_Buffer.Write(Record.ReadFileContent(), 0, Record.DataLength);
					_Buffer.Seek(_Position, SeekOrigin.Begin);
				}
				return _Buffer;
			}
		}

		protected bool Modified;

		public GGFileStream(FileRecord record) {
			Record = record;
		}

		/// <summary>
		/// Write all changes to GGPK
		/// </summary>
		public override void Flush() {
			if (_Buffer == null || !Modified)
				return;
			Record.ReplaceContent(new(_Buffer.GetBuffer(), 0, (int)_Buffer.Length));
			Modified = false;
		}

		public override int Read(byte[] buffer, int offset, int count) {
			if (_Buffer != null)
				return Buffer.Read(buffer, offset, count);
			Record.Ggpk.GGPKStream.Seek(Record.DataOffset + _Position, SeekOrigin.Begin);
			var read = Record.Ggpk.GGPKStream.Read(buffer, offset, count);
			_Position += read;
			return read;
		}

		public override int Read(Span<byte> buffer) {
			if (_Buffer != null)
				return _Buffer.Read(buffer);
			Record.Ggpk.GGPKStream.Seek(Record.DataOffset + _Position, SeekOrigin.Begin);
			var read = Record.Ggpk.GGPKStream.Read(buffer);
			_Position += read;
			return read;
		}

		public override int ReadByte() {
			if (_Buffer != null)
				return _Buffer.ReadByte();
			Record.Ggpk.GGPKStream.Seek(Record.DataOffset + _Position, SeekOrigin.Begin);
			var value = Record.Ggpk.GGPKStream.ReadByte();
			_Position += 1;
			return value;
		}

		public override long Seek(long offset, SeekOrigin origin) {
			if (_Buffer != null)
				return _Position = _Buffer.Seek(offset, origin);
			var pos = origin switch {
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => _Position + offset,
				SeekOrigin.End => Length + offset,
				_ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null),
			};
			if (pos < 0)
				throw new ArgumentOutOfRangeException(nameof(offset), offset, null);
			return _Position = pos;
		}

		public override void SetLength(long value) {
			if (value == Length)
				return;
			Buffer.SetLength(value);
			Modified = true;
		}

		/// <summary>
		/// Won't affect the actual file before calling <see cref="Flush"/>
		/// </summary>
		public override void Write(byte[] buffer, int offset, int count) {
			Buffer.Write(buffer, offset, count);
			Modified = true;
		}

		/// <summary>
		/// Won't affect the actual file before calling <see cref="Flush"/>
		/// </summary>
		public override void Write(ReadOnlySpan<byte> buffer) {
			Buffer.Write(buffer);
			Modified = true;
		}

		/// <summary>
		/// Won't affect the actual file before calling <see cref="Flush"/>
		/// </summary>
		public override void WriteByte(byte value) {
			Buffer.WriteByte(value);
			Modified = true;
		}

		public override bool CanRead => Record.Ggpk.GGPKStream.CanRead;

		/// <returns><see langword="true"/></returns>
		public override bool CanSeek => true;

		public override bool CanWrite => Record.Ggpk.GGPKStream.CanWrite;

		public override long Length => _Buffer?.Length ?? Record.DataLength;

		protected long _Position;
		public override long Position {
			get => _Buffer?.Position ?? _Position;
			set {
				if (_Buffer != null)
					_Buffer.Position = value;
				_Position = value;
			}
		}

		protected override void Dispose(bool disposing) {
			if (disposing) {
				Flush();
				_Buffer?.Close();
			}
		}

		~GGFileStream() {
			Close(); // which will call Dispose(true);
		}
	}
}