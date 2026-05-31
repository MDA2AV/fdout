using System.Collections.Concurrent;

namespace fdout;

/// <summary>Internal bounded pool of plain rings, ATTACH_WQ-shared io-wq.</summary>
internal sealed class RingPool : IDisposable
{
    private readonly ConcurrentStack<Ring> _free = new();
    private readonly Ring[]                _all;
    private readonly SemaphoreSlim         _gate;

    public RingPool(int size, uint depth)
    {
        if (size <= 0)
        {
            size = Environment.ProcessorCount;
        }

        _all    = new Ring[size];
        _all[0] = Ring.Create(depth, -1);
        for (int i = 1; i < size; i++)
        {
            _all[i] = Ring.Create(depth, _all[0].Fd);
        }

        foreach (Ring r in _all)
        {
            _free.Push(r);
        }

        _gate = new SemaphoreSlim(size, size);
    }

    public Ring Rent()
    {
        _gate.Wait();
        _free.TryPop(out Ring? r);
        return r!;
    }

    public void Return(Ring r)
    {
        _free.Push(r);
        _gate.Release();
    }

    public void Dispose()
    {
        foreach (Ring r in _all)
        {
            r.Dispose();
        }

        _gate.Dispose();
    }
}
