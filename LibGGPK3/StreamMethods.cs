using System.IO;

namespace LibGGPK3 {
	public static class StreamMethods {
		public static unsafe short ReadInt16(this Stream stream) {
			var b = new byte[2];
			stream.Read(b, 0, 2);
			fixed (byte* p = b)
				return *(short*)p;
		}

		public static unsafe int ReadInt32(this Stream stream) {
			var b = new byte[4];
			stream.Read(b, 0, 4);
			fixed (byte* p = b)
				return *(int*)p;
		}

		public static unsafe long ReadInt64(this Stream stream) {
			var b = new byte[8];
			stream.Read(b, 0, 8);
			fixed (byte* p = b)
				return *(long*)p;
		}

		public static unsafe void Write(this Stream stream, byte value) {
			stream.WriteByte(value);
		}

		public static unsafe void Write(this Stream stream, sbyte value) {
			stream.Write(new(&value, 1));
		}

		public static unsafe void Write(this Stream stream, short value) {
			stream.Write(new(&value, 2));
		}

		public static unsafe void Write(this Stream stream, ushort value) {
			stream.Write(new(&value, 2));
		}

		public static unsafe void Write(this Stream stream, int value) {
			stream.Write(new(&value, 4));
		}

		public static unsafe void Write(this Stream stream, uint value) {
			stream.Write(new(&value, 4));
		}

		public static unsafe void Write(this Stream stream, long value) {
			stream.Write(new(&value, 8));
		}

		public static unsafe void Write(this Stream stream, ulong value) {
			stream.Write(new(&value, 8));
		}
	}
}