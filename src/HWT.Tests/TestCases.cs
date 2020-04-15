using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using HWT;
using Xunit;

namespace HWT.Tests
{
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
            ManualResetEvent barrier = new ManualResetEvent(false);
            var timeout = timer.NewTimeout(new TimerTask(() => { barrier.WaitOne(); }), TimeSpan.FromSeconds(10));
            Assert.False(barrier.WaitOne(3000));
            Assert.False(timeout.Expired);
            timer.Stop();
        }

        [Fact]
        public void ScheduleTimeoutShouldRunAfterDelay()
        {
            var timer = new HashedWheelTimer();
            ManualResetEvent barrier = new ManualResetEvent(false);
            var timeout = timer.NewTimeout(new TimerTask(() => { barrier.Set(); }), TimeSpan.FromSeconds(2));
            Assert.True(barrier.WaitOne(3000));
            Assert.True(timeout.Expired);
            timer.Stop();
        }

        [Fact]
        public void StopTimer()
        {
            var timerProcessed = new HashedWheelTimer();
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

            var timerUnprocessed = new HashedWheelTimer();

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
            var timer = new HashedWheelTimer();
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

            Assert.Throws<InvalidOperationException>(() =>
            {
                timer.NewTimeout(new TimerTask(() => { }), TimeSpan.FromMilliseconds(1));
            });
        }

        [Fact(Timeout = 5000)]
        public void TimerOverflowWheelLength()
        {
            var timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(100), 32);
            int count = 3;
            timer.NewTimeout(new TimerTask2(() =>
            {
                //
                Interlocked.Decrement(ref count);
            }), TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void ExecutionOnTime()
        {
            int tickDuration = 200;
            int timeout = 125;
            int maxTimeout = 2 * (tickDuration + timeout);
            var timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(200));
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
            var timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(100), 32, 2);
            timer.NewTimeout(new TimerTask(() => { }), TimeSpan.FromSeconds(5));
            timer.NewTimeout(new TimerTask(() => { }), TimeSpan.FromSeconds(5));
            Assert.Throws<InvalidOperationException>(() =>
            {
                timer.NewTimeout(new TimerTask(() => { }), TimeSpan.FromMilliseconds(1));
            });
        }

        [Fact(Timeout = 3000)]
        public void NewTimeoutShouldStopThrowingRejectedExecutionExceptionWhenExistingTimeoutIsCancelled()
        {
            var latch = new ManualResetEvent(false);
            var timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(25), 4, 2);
            timer.NewTimeout(new TimerTask(() => { }), TimeSpan.FromSeconds(5));
            timer.NewTimeout(new ManualResetEventTask(latch), TimeSpan.FromMilliseconds(90));
            latch.WaitOne();

            var secondLatch = new ManualResetEvent(false);
            timer.NewTimeout(new ManualResetEventTask(secondLatch), TimeSpan.FromMilliseconds(90));
            secondLatch.WaitOne();

            timer.Stop();
        }

        [Fact]
        public void PendingTimeouts()
        {
            var latch = new ManualResetEvent(false);
            var timer = new HashedWheelTimer();
            var t1 = timer.NewTimeout(new TimerTask(() => { }), TimeSpan.FromMinutes(100));
            var t2 = timer.NewTimeout(new TimerTask(() => { }), TimeSpan.FromMinutes(100));
            timer.NewTimeout(new ManualResetEventTask(latch), TimeSpan.FromMilliseconds(90));

            Assert.Equal(3, timer.PendingTimeouts);
            t1.Cancel();
            t2.Cancel();
            latch.WaitOne();
            Assert.Equal(0, timer.PendingTimeouts);
            timer.Stop();
        }

        [Fact]
        public void Overflow()
        {
            var latch = new ManualResetEvent(false);
            var timer = new HashedWheelTimer();
            var timeout = timer.NewTimeout(new ManualResetEventTask(latch), TimeSpan.FromMilliseconds(500000L));
            Assert.False(latch.WaitOne(1000));
            timeout.Cancel();
            timer.Stop();
        }

        class ManualResetEventTask : ITimerTask
        {
            private readonly ManualResetEvent _event;

            public ManualResetEventTask(ManualResetEvent @event)
            {
                _event = @event;
            }

            public Task RunAsync(ITimeout timeout)
            {
                _event.Set();
                return Task.CompletedTask;
            }
        }

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