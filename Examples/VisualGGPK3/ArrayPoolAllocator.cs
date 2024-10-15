using System.Buffers;
using Pfim;

namespace VisualGGPK3;

public sealed class ArrayPoolAllocator : IImageAllocator
{
    public static readonly ArrayPoolAllocator Instance = new();
    private ArrayPoolAllocator() { }
    public byte[] Rent(int size) => ArrayPool<byte>.Shared.Rent(size);
    public void Return(byte[] data) => ArrayPool<byte>.Shared.Return(data);
}