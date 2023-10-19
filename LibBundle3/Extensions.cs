using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace LibBundle3 {
	internal static class Extensions {
		public static string ExpandPath(in string path) {
			if (path.StartsWith('~')) {
				if (path.Length == 1) { // ~
					var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.None);
					if (!string.IsNullOrEmpty(userProfile))
						return Environment.ExpandEnvironmentVariables(userProfile);
				} else if (path[1] == '/' || path[1] == Path.DirectorySeparatorChar) { // ~/...
					var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.None);
					if (!string.IsNullOrEmpty(userProfile))
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
						using var p = Process.Start(new ProcessStartInfo(bash) {
							CreateNoWindow = true,
							ErrorDialog = true,
							RedirectStandardInput = true,
							RedirectStandardOutput = true,
							WindowStyle = ProcessWindowStyle.Hidden
						});
						p!.StandardInput.WriteLine("echo " + path);
						var tmp = p.StandardOutput.ReadLine();
						p.Kill();
						if (!string.IsNullOrEmpty(tmp))
							return tmp;
					}
				} catch { }
			}
			return Environment.ExpandEnvironmentVariables(path);
		}

		[SkipLocalsInit]
		public static unsafe T Read<T>(this Stream stream) where T : unmanaged {
			T value;
			stream.ReadExactly(new(&value, sizeof(T)));
			return value;
		}

		public static unsafe void Write<T>(this Stream stream, in T value) where T : unmanaged {
			fixed (T* p = &value)
				stream.Write(new(p, sizeof(T)));
		}

		public static IReadOnlyList<T> AsReadOnly<T>(this IList<T> list) {
			return list is IReadOnlyList<T> irl ? irl : new ReadOnlyListWrapper<T>(list);
		}

		private sealed class ReadOnlyListWrapper<T> : IReadOnlyList<T> {
			private readonly IList<T> list;
			public ReadOnlyListWrapper(in IList<T> list) {
				this.list = list;
			}
			public T this[int index] => list[index];
			public int Count => list.Count;
			public IEnumerator<T> GetEnumerator() => list.GetEnumerator();
			IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
		}
	}
}