using System;

namespace HashedWheelTimer
{
    public static class DateTimeHelper
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0);

        public static long TotalMilliseconds => (long) DateTime.UtcNow.Subtract(Epoch).TotalMilliseconds;
    }
}