using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HWT.Tests;

public class TestCases
{
    [Fact]
    public void StopBeforeStart()
    {
        var timer = new HashedWheelTimer();
        Assert.Empty(timer.Stop());
    }

    [Fact]
    public void ScheduleTimeoutShouldNotRunBeforeDelay()
    {
        var timer = new HashedWheelTimer();
        var barrier = new CountdownEvent(1);
        var timeout = timer.NewTimeout((_) => { barrier.Wait(); }, TimeSpan.FromSeconds(10));
        Assert.False(barrier.Wait(3000));
        Assert.False(timeout.Expired);
        timer.Stop();
    }

    [Fact]
    public void ScheduleTimeoutShouldRunAfterDelay()
    {
        var timer = new HashedWheelTimer();
        var barrier = new CountdownEvent(1);
        var timeout = timer.NewTimeout(_ => { barrier.Signal(); }, TimeSpan.FromSeconds(2));
        Assert.True(barrier.Wait(3000));
        Assert.True(timeout.Expired);
        timer.Stop();
    }

    [Fact]
    public void StopTimer()
    {
        var timerProcessed = new HashedWheelTimer();
        var latch = new CountdownEvent(1);

        for (var i = 0; i < 3; i++)
        {
            timerProcessed.NewTimeout(_ =>
            {
                //
                latch.Signal();
            }, TimeSpan.FromSeconds(1));
        }

        latch.Wait();

        Assert.Empty(timerProcessed.Stop());

        var timerUnprocessed = new HashedWheelTimer();

        for (var i = 0; i < 5; i++)
        {
            timerUnprocessed.NewTimeout(_ => { }, TimeSpan.FromSeconds(150));
        }

        Assert.NotEmpty(timerUnprocessed.Stop());
    }

    [Fact]
    public void TimerShouldThrowExceptionAfterShutdownForNewTimeouts()
    {
        var timer = new HashedWheelTimer();
        var count1 = 3;

        for (var i = 0; i < 3; i++)
        {
            timer.NewTimeout(_ => { count1--; }, TimeSpan.FromMilliseconds(1));
        }

        while (count1 > 0)
        {
            Thread.Sleep(100);
        }

        timer.Stop();

        Assert.Throws<InvalidOperationException>(() => { timer.NewTimeout(_ => { }, TimeSpan.FromMilliseconds(1)); });
    }

    [Fact(Timeout = 5000)]
    public async Task TimerOverflowWheelLength()
    {
        var timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(100), 32);
        var count = 3;
        timer.NewTimeout(new TimerTask2(() =>
        {
            //
            Interlocked.Decrement(ref count);
        }), TimeSpan.FromMilliseconds(100));
        await Task.CompletedTask;
    }

    [Fact]
    public void ExecutionOnTime()
    {
        var tickDuration = 200;
        var timeout = 125;
        var maxTimeout = 2 * (tickDuration + timeout);
        var timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(200));
        var queue = new BlockingCollection<long>();

        var scheduledTasks = 100000;
        for (var i = 0; i < scheduledTasks; i++)
        {
            var start = DateTimeHelper.TotalMilliseconds;
            timer.NewTimeout(_ =>
                {
                    //
                    queue.Add(DateTimeHelper.TotalMilliseconds - start);
                },
                TimeSpan.FromMilliseconds(125));
        }

        for (var i = 0; i < scheduledTasks; i++)
        {
            var delay = queue.Take();
            Assert.True(
                delay >= timeout && delay < maxTimeout);
        }

        timer.Stop();
    }

    [Fact]
    public void RejectedExecutionExceptionWhenTooManyTimeoutsAreAddedBackToBack()
    {
        var timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(100), 32, 2);
        timer.NewTimeout(_ => { }, TimeSpan.FromSeconds(5));
        timer.NewTimeout(_ => { }, TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(() => { timer.NewTimeout(_ => { }, TimeSpan.FromMilliseconds(1)); });
    }

    [Fact(Timeout = 3000)]
    public async Task NewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsCancelled()
    {
        var latch = new CountdownEvent(1);
        var timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(25), 4, 2);
        timer.NewTimeout(_ => { }, TimeSpan.FromSeconds(5));
        timer.NewTimeout(new CountdownEventTask(latch), TimeSpan.FromMilliseconds(90));
        latch.Wait();

        var secondLatch = new CountdownEvent(1);
        timer.NewTimeout(new CountdownEventTask(secondLatch), TimeSpan.FromMilliseconds(90));
        secondLatch.Wait();

        timer.Stop();
        await Task.CompletedTask;
    }

    [Fact]
    public void NewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsExecuted()
    {
        var latch = new CountdownEvent(1);
        // var timer = new HashedWheelTimer(Executors.defaultThreadFactory(), 25,
        //     TimeUnit.MILLISECONDS, 4, true, 2);
        var timer = new HashedWheelTimer(null, Task.Factory, TimeSpan.FromMilliseconds(25), 4, 2);
        timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromSeconds(5));
        timer.NewTimeout(CreateCountDownLatchTimerTask(latch), TimeSpan.FromMilliseconds(90));

        latch.Wait();

        var secondLatch = new CountdownEvent(1);
        timer.NewTimeout(CreateCountDownLatchTimerTask(secondLatch), TimeSpan.FromMilliseconds(90));

        secondLatch.Wait();
        timer.Stop();
    }

    [Fact]
    public void PendingTimeouts()
    {
        var latch = new CountdownEvent(1);
        var timer = new HashedWheelTimer();
        var t1 = timer.NewTimeout(_ => { }, TimeSpan.FromMinutes(100));
        var t2 = timer.NewTimeout(_ => { }, TimeSpan.FromMinutes(100));
        timer.NewTimeout(new CountdownEventTask(latch), TimeSpan.FromMilliseconds(90));

        Assert.Equal(3, timer.PendingTimeouts);
        t1.Cancel();
        t2.Cancel();
        latch.Wait();
        Assert.Equal(0, timer.PendingTimeouts);
        timer.Stop();
    }

    [Fact]
    public void Overflow()
    {
        var latch = new CountdownEvent(1);
        var timer = new HashedWheelTimer();
        var timeout = timer.NewTimeout(new CountdownEventTask(latch), TimeSpan.FromMilliseconds(500000L));
        Assert.False(latch.Wait(1000));
        timeout.Cancel();
        timer.Stop();
    }

    [Fact]
    public void StopTimerCancelsPendingTasks()
    {
        var timerUnprocessed = new HashedWheelTimer();
        for (var i = 0; i < 5; i++)
        {
            timerUnprocessed.NewTimeout(new ActionTimerTask(_ => { })
                , TimeSpan.FromSeconds(5));
        }

        Thread.Sleep(1000); // sleep for a second


        foreach (var timeout in timerUnprocessed.Stop())
        {
            Assert.True(timeout.Cancelled, "All unprocessed tasks should be canceled");
        }
    }

    [Fact]
    public void PendingTimeoutsShouldBeCountedCorrectlyWhenTimeoutCancelledWithinGoalTick()
    {
        var timer = new HashedWheelTimer();
        var barrier = new CountdownEvent(1);
        // A total of 11 timeouts with the same delay are submitted, and they will be processed in the same tick.
        timer.NewTimeout(new ActionTimerTask(_ =>
            {
                barrier.Signal();
                Thread.Sleep(1000);
            })
            , TimeSpan.FromMilliseconds(200));
        var timeouts = new List<HashedWheelTimeout>();
        for (var i = 0; i < 10; i++)
        {
            timeouts.Add(timer.NewTimeout(CreateNoOpTimerTask(), TimeSpan.FromMilliseconds(200)));
        }

        barrier.Wait();
        // The simulation here is that the timeout has been transferred to a bucket and is canceled before it is
        // actually expired in the goal tick.
        foreach (var timeout in timeouts)
        {
            timeout.Cancel();
        }

        Thread.Sleep(2000);
        Assert.Equal(0, timer.PendingTimeouts);
        timer.Stop();
    }

    class CountdownEventTask(CountdownEvent @event) : TimerTask
    {
        public override Task RunAsync(ITimeout timeout)
        {
            @event.Signal();
            return Task.CompletedTask;
        }
    }

    private static ITimerTask CreateNoOpTimerTask()
    {
        return new ActionTimerTask(_ => { });
    }

    private static ITimerTask CreateCountDownLatchTimerTask(CountdownEvent latch)
    {
        return new ActionTimerTask(_ => { latch.Signal(); });
    }

    class TimerTask2(Action action) : TimerTask
    {
        public override Task RunAsync(ITimeout timeout)
        {
            timeout.Timer.NewTimeout(this, TimeSpan.FromMilliseconds(100));
            action();
            return Task.CompletedTask;
        }
    }
}