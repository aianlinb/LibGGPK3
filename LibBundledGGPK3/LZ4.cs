using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SystemExtensions;
using SystemExtensions.Spans;

namespace LibBundledGGPK3 {
	public static class LZ4 {
		private const int MATCHLEN_BITS = 4;
		private const int MATCHLEN_MASK = (1 << MATCHLEN_BITS) - 1;
		private const int MIN_MATCH = 4;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int ReadVariableLength(int initalLength, scoped ref ReadOnlySpan<byte> input) {
			if (initalLength == MATCHLEN_MASK) { // 0b1111
				byte i;
				do {
					initalLength += i = input.ReadAndSlice<byte>();
				} while (i == byte.MaxValue);
			}
			return initalLength;
		}

		/// <param name="input">Data to decompress</param>
		/// <param name="output">Span to store the decompressed data (optional starts with prefix data with <paramref name="prefixSize"/>)</param>
		/// <param name="prefixSize">
		/// Size in bytes of the prefix data at the start of <paramref name="output"/>.<br />
		/// The actual decompressed data will be written to <paramref name="output"/>[<paramref name="prefixSize"/>..].
		/// </param>
		/// <param name="extDict">External dictionary to use for decompression</param>
		/// <returns>Size in bytes of the data written to <paramref name="output"/></returns>
		public static int Decompress(scoped ReadOnlySpan<byte> input, scoped Span<byte> output, int prefixSize = 0, scoped ReadOnlySpan<byte> extDict = default) {
			ref var outStart = ref MemoryMarshal.GetReference(output);
			output = output.Slice(prefixSize);
			while (true) {
                byte token = input.ReadAndSlice<byte>();

				var litLen = ReadVariableLength(token >> MATCHLEN_BITS/*HIGH 4 bits*/, ref input);
				input.CopyToAndSlice(ref output, litLen);
				if (input.IsEmpty)
					return (int)Unsafe.ByteOffset(ref outStart, ref MemoryMarshal.GetReference(output)) - prefixSize;

				var offset = (int)input.ReadAndSlice<ushort>();
				Utils.EnsureLittleEndian(ref offset);

				var matchLen = ReadVariableLength(token & MATCHLEN_MASK/*LOW 4 bits*/, ref input) + MIN_MATCH;
				ref var pMatch = ref Unsafe.SubtractByteOffset(ref MemoryMarshal.GetReference(output), offset);

				{	// Proccess extDict
					var beforeOut = (int)Unsafe.ByteOffset(ref pMatch, ref outStart);
					if (beforeOut > 0) {
						var dictOffset = extDict.Length - beforeOut;
						if (dictOffset < 0)
							ThrowOffsetTooLarge();
						if (matchLen <= beforeOut) {
							extDict.SliceUnchecked(dictOffset, matchLen).CopyToAndSliceDest(ref output);
							continue;
						}
						extDict.SliceUnchecked(dictOffset, beforeOut).CopyToAndSliceDest(ref output);
						matchLen -= beforeOut;
						pMatch = ref outStart;
					}
				}

				while (matchLen > offset) { // Support for overlapping matches
					MemoryMarshal.CreateReadOnlySpan(in pMatch, offset).CopyToAndSliceDest(ref output);
					matchLen -= offset;
					if ((offset *= 2) < 0)
						break; // overflow
				}
				MemoryMarshal.CreateReadOnlySpan(in pMatch, matchLen).CopyToAndSliceDest(ref output);
			}
		}

		[DoesNotReturn]
		[DebuggerNonUserCode]
		private static void ThrowOffsetTooLarge() {
			throw new InvalidDataException("Attempt to seek before the beginning of the output span.\nOr the extDict provided is not long enough.");
		}
	}
}