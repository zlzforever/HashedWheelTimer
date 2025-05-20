using System;
using System.Threading;
using System.Threading.Tasks;

namespace HWT.Sample;

/// <summary>
/// Task fired repeatedly
/// </summary>
class IntervalTimerTask : TimerTask
{
    public override Task RunAsync(ITimeout timeout)
    {
        Console.WriteLine($"IntervalTimerTask is fired at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        timeout.Timer.NewTimeout(this, TimeSpan.FromSeconds(5));
        return Task.FromResult(0);
    }
}

class OneTimeTask(string name) : TimerTask
{
    public override Task RunAsync(ITimeout timeout)
    {
        Console.WriteLine($"{name} is fired at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return Task.CompletedTask;
    }
}

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} : Started");

        var timer = new HashedWheelTimer();

        timer.NewTimeout(new OneTimeTask("A"), TimeSpan.FromSeconds(5));
        timer.NewTimeout(new OneTimeTask("B"), TimeSpan.FromSeconds(4));
        var timeout = timer.NewTimeout(new OneTimeTask("C"), TimeSpan.FromSeconds(3));
        timer.NewTimeout(new OneTimeTask("D"), TimeSpan.FromSeconds(2));
        timer.NewTimeout(new OneTimeTask("E"), TimeSpan.FromSeconds(1));
        timer.NewTimeout(new IntervalTimerTask(), TimeSpan.FromSeconds(1));

        timeout.Cancel();

        Thread.Sleep(1000000000);

        Console.ReadKey();
        timer.Stop();
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} : Stopped");
        Console.ReadKey();
    }
}