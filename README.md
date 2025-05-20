# HashedWheelTimer

Hashed Wheel Timer implementation based C#

[![.NET](https://github.com/zlzforever/HashedWheelTimer/actions/workflows/dotnet.yml/badge.svg?branch=master)](https://github.com/zlzforever/HashedWheelTimer/actions/workflows/dotnet.yml)

## What is it?

Hashed Wheel Timer is an approximate timer with configurable accuracy, which could be used for very efficient
single-threaded execution of scheduled tasks.

This implementation assumes single-writer principle and timers firing on processing thread.

Low (or NO) garbage.

Could be used with .net framework, dotnet core.

## How to get?

```
dotnet add package HashedWheelTimer.NET --version 0.10.3
```

## How to use?

```
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
```
