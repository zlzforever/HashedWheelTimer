using System.Threading;

namespace HWT;

public class AtomicBoolean
{
    private int _value;

    public bool CompareAndSet(bool v1, bool v2)
    {
        return Interlocked.CompareExchange(ref _value, v1 ? 1 : 0, v2 ? 1 : 0) == 0;
    }
}