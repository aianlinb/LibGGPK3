using System;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LibGGPK3 {
	public static class Extensions {
		public static readonly Func<int, string> FastAllocateString = typeof(string).GetMethod("FastAllocateString", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Func<int, string>>();

		public static async Task<string> GetPatchServer(bool garena = false) {
			var tcp = new Socket(SocketType.Stream, ProtocolType.Tcp);
			if (garena)
				await tcp.ConnectAsync(Dns.GetHostAddresses("login.tw.pathofexile.com"), 12999);
			else
				await tcp.ConnectAsync(Dns.GetHostAddresses("us.login.pathofexile.com"), 12995);
			var b = new byte[256];
			await tcp.SendAsync(new byte[] { 1, 4 }, SocketFlags.None);
			await tcp.ReceiveAsync(b, SocketFlags.None);
			tcp.Close();
			return Encoding.Unicode.GetString(b, 35, b[34] * 2);
		}

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
		public static unsafe void Write(this Stream stream, nint value) {
			stream.Write(new(&value, IntPtr.Size));
		}
		public static unsafe void Write<T>(this Stream stream, T value) where T : unmanaged {
			stream.Write(new(&value, Marshal.SizeOf(value)));
		}
	}
}