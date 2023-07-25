using System;
using System.Runtime.InteropServices;

namespace LibBundle3 {
	public static class Oodle {
#pragma warning disable CA1401
		[DllImport("oo2core")]
		public static unsafe extern nint OodleLZ_Decompress(byte* buffer, nint bufferSize, byte* output, nint decompressedSize, int fuzzSafe = 1, int checkCRC = 0, int verbose = 0, byte* v_decBufBase = null, nint decBufSize = 0, void* fpCallback = null, void* callbackUserData = null, void* decoderMemory = null, nint decoderMemorySize = 0, int threadPhase = 3);

		[DllImport("oo2core")]
		public static unsafe extern nint OodleLZ_Compress(Compressor compressor, byte* buffer, nint bufferSize, byte* outputBuffer, CompressionLevel level, void* pOptions = null, void* dictionaryBase = null, void* longRangeMatcher = null, void* scratchMem = null, nint scratchSize = 0);

		public enum Compressor {
			Invalid = -1,
			/// <summary>
			/// None = memcpy, pass through uncompressed bytes
			/// </summary>
			None = 3,

			// NEW COMPRESSORS :
			/// <summary>
			/// Fast decompression and high compression ratios, amazing!
			/// </summary>
			Kraken = 8,
			/// <summary>
			/// Leviathan = Kraken's big brother with higher compression, slightly slower decompression.
			/// </summary>
			Leviathan = 13,
			/// <summary>
			/// Mermaid is between Kraken &amp; Selkie - crazy fast, still decent compression.
			/// </summary>
			Mermaid = 9,
			/// <summary>
			/// Selkie is a super-fast relative of Mermaid.  For maximum decode speed.
			/// </summary>
			Selkie = 11,
			/// <summary>
			/// Hydra, the many-headed beast = Leviathan, Kraken, Mermaid, or Selkie (see $OodleLZ_About_Hydra)
			/// </summary>
			Hydra = 12,

			// DEPRECATED :
			[Obsolete("no longer supported as of Oodle 2.9.0")] BitKnit = 10,
			[Obsolete("DEPRECATED but still supported")] LZB16 = 4,
			[Obsolete("no longer supported as of Oodle 2.9.0")] LZNA = 7,
			[Obsolete("no longer supported as of Oodle 2.9.0")] LZH = 0,
			[Obsolete("no longer supported as of Oodle 2.9.0")] LZHLW = 1,
			[Obsolete("no longer supported as of Oodle 2.9.0")] LZNIB = 2,
			[Obsolete("no longer supported as of Oodle 2.9.0")] LZBLW = 5,
			[Obsolete("no longer supported as of Oodle 2.9.0")] LZA = 6,

			Count = 14,
			Force32 = 0x40000000
		}

		public enum CompressionLevel {
			None = 0,	  // don't compress, just copy raw bytes
			SuperFast = 1, // super fast mode, lower compression ratio
			VeryFast = 2,  // fastest LZ mode with still decent compression ratio
			Fast = 3,	  // fast - good for daily use
			Normal = 4,		// standard medium speed LZ mode

			Optimal1 = 5,  // optimal parse level 1 (faster optimal encoder)
			Optimal2 = 6,  // optimal parse level 2 (recommended baseline optimal encoder)
			Optimal3 = 7,  // optimal parse level 3 (slower optimal encoder)
			Optimal4 = 8,  // optimal parse level 4 (very slow optimal encoder)
			Optimal5 = 9,  // optimal parse level 5 (don't care about encode speed, maximum compression)

			HyperFast1 = -1,   // faster than SuperFast, less compression
			HyperFast2 = -2, // faster than HyperFast1, less compression
			HyperFast3 = -3, // faster than HyperFast2, less compression
			HyperFast4 = -4, // fastest, less compression

			// aliases :
			HyperFast = HyperFast1, // alias hyperfast base level
			Optimal = Optimal2,   // alias optimal standard level
			Max = Optimal5,   // maximum compression level
			Min = HyperFast4, // fastest compression level

			Force32 = 0x40000000,
			Invalid = Force32
		}
	}
}