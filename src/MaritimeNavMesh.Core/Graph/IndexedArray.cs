using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

namespace MaritimeNavMesh.Core.Graph;

public interface IIndexedArray<out T>
{
    int Length { get; }
    T this[int index] { get; }
    T[] ToArray();
}

public sealed class ManagedArray<T>(T[] data) : IIndexedArray<T>
{
    private readonly T[] _data = data;

    public int Length => _data.Length;
    public T this[int index] => _data[index];
    public T[] ToArray() => [.. _data];
}

public sealed unsafe class MappedGraphBuffer : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _view;
    private byte* _pointer;

    public MappedGraphBuffer(string path)
    {
        _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, mapName: null, capacity: 0, MemoryMappedFileAccess.Read);
        _view = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _pointer);
    }

    public byte* Pointer => _pointer;

    public MappedArray<T> Slice<T>(long offsetBytes, int length) where T : unmanaged => new(this, offsetBytes, length);
    public void Dispose()
    {
        if (_pointer is not null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _pointer = null;
        }
        _view.Dispose();
        _mmf.Dispose();
    }
}

public readonly unsafe struct MappedArray<T>(MappedGraphBuffer owner, long offsetBytes, int length) : IIndexedArray<T> where T : unmanaged
{
    private readonly MappedGraphBuffer _owner = owner;
    private readonly long _offsetBytes = offsetBytes;
    public int Length { get; } = length;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Length)
                throw new IndexOutOfRangeException();
            return Unsafe.ReadUnaligned<T>(_owner.Pointer + _offsetBytes + (index * sizeof(T)));
        }
    }

    public T[] ToArray()
    {
        var result = new T[Length];
        for (int i = 0; i < Length; i++)
            result[i] = this[i];
        return result;
    }
}
