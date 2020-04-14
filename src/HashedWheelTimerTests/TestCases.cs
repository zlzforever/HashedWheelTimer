using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using HashedWheelTimer;
using Xunit;

namespace HashedWheelTimerTests
{
    public class TestCases
    {
        [Fact]
        public void ScheduleTimeoutShouldNotRunBeforeDelay()
        {
            var timer = new global::HashedWheelTimer.HashedWheelTimer();
            ManualResetEvent barrier = new ManualResetEvent(false);
            var timeout = timer.NewTimeout(new TimerTask(() => { barrier.WaitOne(); }), TimeSpan.FromSeconds(10));
            Assert.False(barrier.WaitOne(3000));
            Assert.False(timeout.Expired);
            timer.Stop();
        }

        [Fact]
        public void ScheduleTimeoutShouldRunAfterDelay()
        {
            var timer = new global::HashedWheelTimer.HashedWheelTimer();
            ManualResetEvent barrier = new ManualResetEvent(false);
            var timeout = timer.NewTimeout(new TimerTask(() => { barrier.Set(); }), TimeSpan.FromSeconds(2));
            Assert.True(barrier.WaitOne(3000));
            Assert.True(timeout.Expired);
            timer.Stop();
        }

        [Fact]
        public void StopTimer()
        {
            var timerProcessed = new global::HashedWheelTimer.HashedWheelTimer();
            int count1 = 3;

            for (int i = 0; i < 3; i++)
            {
                timerProcessed.NewTimeout(new TimerTask(() =>
                {
                    //
                    Interlocked.Decrement(ref count1);
                }), TimeSpan.FromSeconds(1));
            }

            while (count1 > 0)
            {
                Thread.Sleep(100);
            }

            Assert.Empty(timerProcessed.Stop());

            var timerUnprocessed = new global::HashedWheelTimer.HashedWheelTimer();

            for (int i = 0; i < 5; i++)
            {
                timerUnprocessed.NewTimeout(new TimerTask(() => { }), TimeSpan.FromSeconds(5));
            }

            Thread.Sleep(1000);

            Assert.NotEmpty(timerUnprocessed.Stop());
        }

        [Fact]
        public void TimerShouldThrowExceptionAfterShutdownForNewTimeouts()
        {
            var timer = new global::HashedWheelTimer.HashedWheelTimer();
            int count1 = 3;

            for (int i = 0; i < 3; i++)
            {
                timer.NewTimeout(new TimerTask(() => { count1--; }), TimeSpan.FromMilliseconds(1));
            }

            while (count1 > 0)
            {
                Thread.Sleep(100);
            }

            timer.Stop();

            Assert.Throws<ApplicationException>(() =>
            {
                timer.NewTimeout(new TimerTask(() => { }), TimeSpan.FromMilliseconds(1));
            });
        }

        [Fact]
        public void TimerOverflowWheelLength()
        {
            var timer = new global::HashedWheelTimer.HashedWheelTimer(TimeSpan.FromMilliseconds(100), 32);
            int count = 3;
            timer.NewTimeout(new TimerTask2(() =>
            {
                //
                Interlocked.Decrement(ref count);
            }), TimeSpan.FromMilliseconds(100));
            int totalMs = 0;
            while (count > 0 && totalMs <= 5000)
            {
                Thread.Sleep(100);
                totalMs += 100;
            }
        }

        [Fact]
        public void ExecutionOnTime()
        {
            int tickDuration = 200;
            int timeout = 125;
            int maxTimeout = 2 * (tickDuration + timeout);
            var timer = new global::HashedWheelTimer.HashedWheelTimer(TimeSpan.FromMilliseconds(200));
            BlockingCollection<long> queue = new BlockingCollection<long>();

            int scheduledTasks = 100000;
            for (int i = 0; i < scheduledTasks; i++)
            {
                long start = DateTimeHelper.TotalMilliseconds;
                timer.NewTimeout(new TimerTask(() =>
                    {
                        //
                        queue.Add(DateTimeHelper.TotalMilliseconds - start);
                    }),
                    TimeSpan.FromMilliseconds(125));
            }

            for (int i = 0; i < scheduledTasks; i++)
            {
                long delay = queue.Take();
                Assert.True(
                    delay >= timeout && delay < maxTimeout);
            }

            timer.Stop();
        }

        [Fact]
        public void RejectedExecutionExceptionWhenTooManyTimeoutsAreAddedBackToBack()
        {
            var timer = new global::HashedWheelTimer.HashedWheelTimer(TimeSpan.FromMilliseconds(100), 32, 2);
            timer.NewTimeout(new TimerTask(() => { }), TimeSpan.FromSeconds(5));
            timer.NewTimeout(new TimerTask(() => { }), TimeSpan.FromSeconds(5));
            Assert.Throws<InvalidOperationException>(() =>
            {
                timer.NewTimeout(new TimerTask(() => { }), TimeSpan.FromMilliseconds(1));
            });
        }

        // [Fact]
        // public void NewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsCancelled()
        // {
        //     
        // }
        
        class TimerTask2 : ITimerTask
        {
            private Action _action;

            public TimerTask2(Action action)
            {
                _action = action;
            }

            public Task RunAsync(ITimeout timeout)
            {
                timeout.Timer.NewTimeout(this, TimeSpan.FromMilliseconds(100));
                _action();
                return Task.CompletedTask;
            }
        }

        class TimerTask : ITimerTask
        {
            private Action _action;

            public TimerTask(Action action)
            {
                _action = action;
            }

            public Task RunAsync(ITimeout timeout)
            {
                _action();
                return Task.CompletedTask;
            }
        }
    }
}