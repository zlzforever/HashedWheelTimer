using System;
using System.Threading.Tasks;
using HashedWheelTimer;

namespace HashedWheelTimerSample
{
    /// <summary>
    /// Task fired repeatedly
    /// </summary>
    class IntervalTimerTask : ITimerTask
    {
        public Task RunAsync(ITimeout timeout)
        {
            Console.WriteLine($"IntervalTimerTask is fired at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            timeout.Timer.NewTimeout(this, TimeSpan.FromSeconds(2));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Task only be fired for one time
    /// </summary>
    class OneTimeTask : ITimerTask
    {
        readonly string _userData;

        public OneTimeTask(string data)
        {
            _userData = data;
        }

        public Task RunAsync(ITimeout timeout)
        {
            Console.WriteLine($"{_userData} is fired at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            return Task.CompletedTask;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine($"{DateTime.UtcNow.Ticks / 10000000L} : Started");

            HashedWheelTimer.HashedWheelTimer timer = new HashedWheelTimer.HashedWheelTimer(TimeSpan.FromMilliseconds(100)
                , 100000
                , 0);

            timer.NewTimeout(new OneTimeTask("A"), TimeSpan.FromSeconds(5));
            timer.NewTimeout(new OneTimeTask("B"), TimeSpan.FromSeconds(4));
            var timeout = timer.NewTimeout(new OneTimeTask("C"), TimeSpan.FromSeconds(3));
            timer.NewTimeout(new OneTimeTask("D"), TimeSpan.FromSeconds(2));
            timer.NewTimeout(new OneTimeTask("E"), TimeSpan.FromSeconds(1));

            timeout.Cancel();

            timer.NewTimeout(new IntervalTimerTask(), TimeSpan.FromSeconds(5));


            System.Threading.Thread.Sleep(7000);

            Console.ReadKey();
            timer.Stop();
            Console.WriteLine($"{DateTime.UtcNow.Ticks / 10000000L} : Stopped");
            Console.ReadKey();
        }
    }
}