using System.Threading;

namespace HashedWheelTimer
{
    public class AtomicLong
    {
        private long _value;

        public long IncrementAndGet()
        {
            return Interlocked.Increment(ref _value);
        }

        public long DecrementAndGet()
        {
            return Interlocked.Decrement(ref _value);
        }
    }
}