using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SystemExtensions;

[assembly: DisableRuntimeMarshalling]

namespace LibBundle3;
/// <summary>
/// Oodle (de)compressions for bundles (needs oo2core native library to work).
/// </summary>
/// <remarks>
/// All methods here are thread-safe, but you must call <see cref="Initialize"/> at least once before first time using any other method <b>*on each thread*</b>
/// </remarks>
public static unsafe class Oodle { // Current Oodle Version: 2.9.12
#pragma warning disable SYSLIB1054 // All parameters are unmanaged types.
	[DllImport("oo2core")]
	private static extern nint OodleLZ_Compress(Compressor compressor, byte* buffer, nint bufferSize, byte* output, CompressionLevel level = CompressionLevel.Normal,
		void* pOptions = null, void* dictionaryBase = null, void* longRangeMatcher = null, void* scratchMem = null, nint scratchSize = 0);
	[DllImport("oo2core")]
	private static extern nint OodleLZ_Decompress(byte* buffer, nint bufferSize, byte* output, nint outputSize, int fuzzSafe = 1, int checkCRC = 0, int verbosity = 0,
		byte* dictionaryBase = null, nint dictionarySize = 0, void* fpCallback = null, void* callbackUserData = null, void* decoderMemory = null, nint decoderMemorySize = 0, int threadPhase = 3);

	[DllImport("oo2core")]
	private static extern nint OodleLZ_GetCompressedBufferSizeNeeded(Compressor compressor/* = Compressor.Invalid*/, nint bufferSize);
	[DllImport("oo2core")]
	private static extern nint OodleLZ_GetCompressScratchMemBound(Compressor compressor, CompressionLevel level, nint bufferSize = -1, void* pOptions = null);
	[DllImport("oo2core")]
	private static extern int OodleLZDecoder_MemorySizeNeeded(Compressor compressor = Compressor.Invalid, nint outputSize = -1);

#pragma warning restore SYSLIB1054

	[ThreadStatic]
	private static Settings settings;
	[ThreadStatic]
	private static byte[]? preAllocatedMemory; // fixed

	/// <summary>
	/// Call this method before first time using any other method <b>*on each thread*</b>
	/// </summary>
	/// <remarks>
	/// You can re-call this method at any time to change the <paramref name="settings"/>.<br />
	/// Re-calling with the same <paramref name="settings"/> does nothing.
	/// </remarks>
	[MemberNotNull(nameof(preAllocatedMemory))]
	public static void Initialize(Settings settings) {
		settings.Validate();
		if (Oodle.settings.ChunkSize >= settings.ChunkSize) {
			if (!settings.EnableCompressing)
				goto end;
			if (Oodle.settings.EnableCompressing
				&& Oodle.settings.CompressionLevel >= settings.CompressionLevel
				&& Oodle.settings.Compressor == settings.Compressor) {
				goto end;
			}
		}

		var l = OodleLZDecoder_MemorySizeNeeded(settings.Compressor, settings.ChunkSize); // max 446680 on Oodle v2.9.12
		if (settings.EnableCompressing) {
			var l2 = (int)OodleLZ_GetCompressScratchMemBound(settings.Compressor, settings.CompressionLevel, settings.ChunkSize);
			if (l2 < 0) // OODLELZ_SCRATCH_MEM_NO_BOUND(-1)
				l = 6 * 1024 * 1024; // 6MB
			else if (l2 > l)
				l = l2;
		}
		if (preAllocatedMemory is null || preAllocatedMemory.Length < l)
			preAllocatedMemory = GC.AllocateUninitializedArray<byte>(l, true); // pinned
	end:
		Oodle.settings = settings;
#pragma warning disable CS8774 // preAllocatedMemory is not null if Initialize has been called before
	}
#pragma warning restore CS8774

	/// <summary>
	/// Get the minimum size needed for the output buffer when compressing data with size <see cref="Settings.ChunkSize"/>,
	/// usually slightly larger than <see cref="Settings.ChunkSize"/>.
	/// </summary>
	/// <remarks>
	/// <para>Call <see cref="Initialize"/> at least once before first time using this method <b>*on each thread*</b></para>
	/// <para>Note this is actually larger than the maximum size of a compressed chunk, it includes overrun padding.</para>
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int GetCompressedBufferSize() => checked((int)OodleLZ_GetCompressedBufferSizeNeeded(settings.Compressor, settings.ChunkSize));
	/// <summary>
	/// Get the minimum size needed for the output buffer of Compress(), usually slightly larger than <paramref name="uncompressedSize"/>.
	/// </summary>
	/// <param name="uncompressedSize">Size of original uncompressed data</param>
	/// <remarks>
	/// <para>Call <see cref="Initialize"/> at least once before first time using this method <b>*on each thread*</b></para>
	/// <para>Note this is actually larger than the maximum size of a compressed chunk, it includes overrun padding.</para>
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int GetCompressedBufferSize(nint uncompressedSize) => checked((int)OodleLZ_GetCompressedBufferSizeNeeded(settings.Compressor, uncompressedSize));

	/// <param name="buffer">The uncompressed data to compress</param>
	/// <param name="output">Buffer to save the compressed output, must be at least <see cref="GetCompressedBufferSize(nint)"/> bytes</param>
	/// <remarks>
	/// Call <see cref="Initialize"/> at least once before first time using this method <b>*on each thread*</b>
	/// </remarks>
	public static int Compress(ReadOnlySpan<byte> buffer, Span<byte> output) {
		if (output.Length < GetCompressedBufferSize(buffer.Length))
			ThrowHelper.Throw<ArgumentException>("The output length must be at least GetCompressedBufferSize(buffer.Length) bytes", nameof(output) + ".Length");
		fixed (byte* pBuffer = buffer, pOutput = output)
			return (int)Compress(pBuffer, buffer.Length, pOutput);
	}
	/// <param name="buffer">Pointer of the uncompressed data with size <see cref="Settings.ChunkSize"/></param>
	/// <param name="output">Buffer to save the compressed output, must be at least <see cref="GetCompressedBufferSize()"/> bytes</param>
	/// <remarks>
	/// Call <see cref="Initialize"/> at least once before first time using this method <b>*on each thread*</b>
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static nint Compress(byte* buffer, byte* output) => Compress(buffer, settings.ChunkSize, output);
	/// <param name="buffer">Pointer of the uncompressed data</param>
	/// <param name="bufferSize">Size of the uncompressed data</param>
	/// <param name="output">Buffer to save the compressed output, must be at least <see cref="GetCompressedBufferSize(nint)"/> bytes</param>
	/// <remarks>
	/// Call <see cref="Initialize"/> at least once before first time using this method <b>*on each thread*</b>
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static nint Compress(byte* buffer, nint bufferSize, byte* output) {
		return OodleLZ_Compress(settings.Compressor, buffer, bufferSize, output, settings.CompressionLevel,
			scratchMem: Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(preAllocatedMemory!)), scratchSize: preAllocatedMemory!.Length);
		// preAllocatedMemory is pinned so AsPointer is safe
	}

	/// <param name="buffer">The compressed data to decompress</param>
	/// <param name="output">Buffer to save the decompressed output, must have exactly the same length with the original uncompressed data</param>
	/// <remarks>
	/// Call <see cref="Initialize"/> at least once before first time using this method <b>*on each thread*</b>
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Decompress(ReadOnlySpan<byte> buffer, Span<byte> output) {
		fixed (byte* pBuffer = buffer, pOutput = output)
			return (int)Decompress(pBuffer, buffer.Length, pOutput, output.Length);
	}
	/// <summary>
	/// Decompress data with exactly uncompressed size <see cref="Settings.ChunkSize"/>
	/// </summary>
	/// <param name="buffer">Pointer of the compressed data</param>
	/// <param name="bufferSize">Size of the compressed data</param>
	/// <param name="output">Buffer to save the decompressed output with size at least <see cref="Settings.ChunkSize"/></param>
	/// <remarks>
	/// Call <see cref="Initialize"/> at least once before first time using this method <b>*on each thread*</b>
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static nint Decompress(byte* buffer, nint bufferSize, byte* output) => Decompress(buffer, bufferSize, output, settings.ChunkSize);
	/// <param name="buffer">Pointer of the compressed data</param>
	/// <param name="bufferSize">Size of the compressed data</param>
	/// <param name="output">Buffer to save the decompressed output</param>
	/// <param name="uncompressedSize">Size of the original uncompressed data</param>
	/// <remarks>
	/// Call <see cref="Initialize"/> at least once before first time using this method <b>*on each thread*</b>
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static nint Decompress(byte* buffer, nint bufferSize, byte* output, nint uncompressedSize) {
		return OodleLZ_Decompress(buffer, bufferSize, output, uncompressedSize,
			decoderMemory: Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(preAllocatedMemory!)), decoderMemorySize: preAllocatedMemory!.Length);
		// preAllocatedMemory is pinned so AsPointer is safe
	}

	/// <summary>
	/// Release the pre-allocated memory from <see cref="Initialize"/> of the current thread
	/// </summary>
	/// <remarks>
	/// The resources will be released automatically when the thread ends, so calling this method is not necessary.<br />
	/// After calling this, you must call <see cref="Initialize"/> again if you want to use any other method.
	/// </remarks>
	public static void Release() {
		settings.ChunkSize = 0;
		settings.EnableCompressing = false;
		preAllocatedMemory = null;
	}

	/// <summary>
	/// Selection of compression algorithm.
	/// </summary>
	/// <remarks>
	/// <para>Each compressor provides a different balance of speed vs compression ratio.</para>
	/// <para>New Oodle users should only use the new sea monster family of compressors (Kraken, Leviathan, Mermaid, Selkie, Hydra).</para>
	/// <para>The sea monsters are all fuzz safe and use whole-block quantum (not the 16k quantum).</para>
	/// </remarks>
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
		/// Hydra, the many-headed beast = Leviathan, Kraken, Mermaid, or Selkie
		/// </summary>
		Hydra = 12,

		// DEPRECATED :
		[Obsolete("No longer supported as of Oodle 2.9.0")] BitKnit = 10,
		[Obsolete("DEPRECATED but still supported")] LZB16 = 4,
		[Obsolete("No longer supported as of Oodle 2.9.0")] LZNA = 7,
		[Obsolete("No longer supported as of Oodle 2.9.0")] LZH = 0,
		[Obsolete("No longer supported as of Oodle 2.9.0")] LZHLW = 1,
		[Obsolete("No longer supported as of Oodle 2.9.0")] LZNIB = 2,
		[Obsolete("No longer supported as of Oodle 2.9.0")] LZBLW = 5,
		[Obsolete("No longer supported as of Oodle 2.9.0")] LZA = 6,

		Count = 14,
		Force32 = 0x40000000
	}
	/// <summary>
	/// Selection of compression encoder complexity
	/// </summary>
	/// <remarks>
	/// <para>Higher numerical value = slower compression, but smaller compressed data.</para>
	/// <para>The compressed stream is always decodable with the same decompressors. <see cref="CompressionLevel"/> controls the amount of work the encoder does to find the best compressed bit stream. <see cref="CompressionLevel"/> does not primary affect decode speed, it trades off encode speed for compressed bit stream quality.</para>
	/// <para>It's recommended to start with <see cref="Normal"/>, then try up or down if you want faster encoding or smaller output files.</para>
	/// <para>The Optimal levels are good for distribution when you compress rarely and decompress often; they provide very high compression ratios but are slow to encode. <see cref="Optimal2"/> is the recommended level to start with of the optimal levels. Optimal4 and 5 are not recommended for common use, they are very slow and provide the maximum compression ratio, but the gain over <see cref="Optimal3"/> is usually small.</para>
	/// <para>The HyperFast levels have negative numeric <see cref="CompressionLevel"/> values. They are faster than <see cref="SuperFast"/> for when you're encoder CPU time constrained or want something closer to symmetric compression vs decompression time. The HyperFast levels are currently only available in <see cref="Compressor.Kraken"/>, <see cref="Compressor.Mermaid"/> and <see cref="Compressor.Selkie"/>. Higher levels of HyperFast are faster to encode, eg. <see cref="HyperFast4"/> is the fastest.</para>
	/// </remarks>
	public enum CompressionLevel {
		/// <summary>
		/// don't compress, just copy raw bytes
		/// </summary>
		None = 0,
		/// <summary>
		/// super fast mode, lower compression ratio
		/// </summary>
		SuperFast = 1,
		/// <summary>
		/// fastest LZ mode with still decent compression ratio
		///	</summary>
		VeryFast = 2,
		/// <summary>
		/// fast - good for daily use
		/// </summary>
		Fast = 3,
		/// <summary>
		/// standard medium speed LZ mode
		/// </summary>
		Normal = 4,

		/// <summary>
		/// optimal parse level 1 (faster optimal encoder)
		/// </summary>
		Optimal1 = 5,
		/// <summary>
		/// optimal parse level 2 (recommended baseline optimal encoder)
		/// </summary>
		Optimal2 = 6,
		/// <summary>
		/// optimal parse level 3 (slower optimal encoder)
		/// </summary>
		Optimal3 = 7,
		/// <summary>
		/// optimal parse level 4 (very slow optimal encoder)
		/// </summary>
		Optimal4 = 8,
		/// <summary>
		/// optimal parse level 5 (don't care about encode speed, maximum compression)
		/// </summary>
		Optimal5 = 9,

		/// <summary>
		/// faster than SuperFast, less compression
		/// </summary>
		HyperFast1 = -1,
		/// <summary>
		/// faster than HyperFast1, less compression
		/// </summary>
		HyperFast2 = -2,
		/// <summary>
		/// faster than HyperFast2, less compression
		/// </summary>
		HyperFast3 = -3,
		/// <summary>
		/// fastest, less compression
		/// </summary>
		HyperFast4 = -4,

		// aliases :
		/// <summary>
		/// alias hyperfast base level
		/// </summary>
		HyperFast = HyperFast1,
		/// <summary>
		/// alias optimal standard level
		/// </summary>
		Optimal = Optimal2,
		/// <summary>
		/// maximum compression level
		/// </summary>
		Max = Optimal5,
		/// <summary>
		/// fastest compression level
		/// </summary>
		Min = HyperFast4,

		Force32 = 0x40000000,
		Invalid = Force32
	}

	/// <summary>
	/// Use the parameterless constructor to get default settings.
	/// </summary>
	public struct Settings {
#pragma warning disable CS1734
		/// <summary>
		/// Max size of the uncompressed data which will be passed to <paramref name="bufferSize"/> of <see cref="Compress(byte*, byte*)"/>
		/// or <paramref name="uncompressedSize"/> of <see cref="Decompress(byte*, nint, byte*, nint)"/>
		/// </summary>
		/// <remarks>
		/// Passing size larger than this value to other methods will reduce the performance
		/// due to the additional memory allocation during compression/decompression.
		/// </remarks>
#pragma warning restore CS1734
		public int ChunkSize = 256 * 1024;
		/// <summary>
		/// The algorithm to use for compressing
		/// </summary>
		public Compressor Compressor = Compressor.Leviathan;
		/// <summary>
		/// See <see cref="Oodle.CompressionLevel"/>
		/// </summary>
		public CompressionLevel CompressionLevel = CompressionLevel.Normal;
		/// <summary>
		/// Allocates necessary memory for compressing.
		/// Default is <see langword="true"/>,
		/// pass <see langword="false"/> if you'll never use any overload of Compress() to save memory.
		/// </summary>
		public bool EnableCompressing = true;
		/// <summary>
		/// Initialize with default settings
		/// </summary>
		public Settings() { }

		/// <summary>
		/// Validate the properties of this instance
		/// </summary>
		/// <exception cref="ArgumentOutOfRangeException" />
		internal readonly void Validate() {
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ChunkSize);
			if (unchecked((uint)Compressor > (uint)Compressor.Leviathan)) // TODO: Disallow DEPRECATED
				ThrowHelper.ThrowArgumentOutOfRange(Compressor, "Invalid compressor type");
			if (CompressionLevel is > CompressionLevel.Max or < CompressionLevel.Min)
				ThrowHelper.ThrowArgumentOutOfRange(CompressionLevel, "CompressionLevel is not defined");
		}
	}
}