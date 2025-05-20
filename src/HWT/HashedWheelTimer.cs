using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HWT;

public class HashedWheelTimer : IDisposable
{
    private readonly ILogger _logger;
    private static readonly AtomicInteger InstanceCounter = new();
    private static readonly AtomicBoolean WarnedTooManyInstances = new();
    private static readonly int InstanceCountLimit = 64;
    private readonly Worker _worker;
    // private Task _workerThread;

    private const long WorkerStateInit = 0L;
    private const long WorkerStateStarted = 1L;
    private const long WorkerStateShutdown = 2L;
    private long _workerState; // 0 - init, 1 - started, 2 - shut down

    private readonly long _tickDuration;
    private readonly HashedWheelBucket[] _wheel;
    private readonly int _mask;
    private readonly CountdownEvent _startTimeInitialized = new(1);
    private readonly ConcurrentQueue<HashedWheelTimeout> _timeouts = new();
    private readonly ConcurrentQueue<HashedWheelTimeout> _cancelledTimeouts = new();
    private readonly AtomicLong _pendingTimeouts = new();
    private readonly long _maxPendingTimeouts;
    private readonly TaskFactory _taskFactory;

    public ILogger Logger => _logger;

    public long StartTime { get; private set; }

    public long PendingTimeouts => _pendingTimeouts.Value;

    public TaskFactory TaskFactory => _taskFactory;

    internal void EnqueueCanceledTimeout(HashedWheelTimeout timeout)
    {
        _cancelledTimeouts.Enqueue(timeout);
    }

    internal void DecrementPendingTimeouts()
    {
        _pendingTimeouts.DecrementAndGet();
    }

    public HashedWheelTimer(TimeSpan duration) : this(null, Task.Factory, duration)
    {
    }

    public HashedWheelTimer(TimeSpan duration, int ticksPerWheel) : this(null, Task.Factory, duration, ticksPerWheel)
    {
    }

    public HashedWheelTimer(TimeSpan duration, int ticksPerWheel, int maxPendingTimeouts) : this(null, Task.Factory,
        duration, ticksPerWheel, maxPendingTimeouts)
    {
    }

    public HashedWheelTimer() : this(null)
    {
    }

    public HashedWheelTimer(ILogger<HashedWheelTimer> logger) : this(logger, Task.Factory,
        TimeSpan.FromMilliseconds(100))
    {
    }

    public HashedWheelTimer(ILogger<HashedWheelTimer> logger,
        TaskFactory taskFactory) : this(logger, taskFactory, TimeSpan.FromMilliseconds(100))
    {
    }

    public HashedWheelTimer(ILogger<HashedWheelTimer> logger,
        TaskFactory taskFactory,
        TimeSpan tickDuration) : this(logger, taskFactory, tickDuration, 512)
    {
    }

    public HashedWheelTimer(ILogger<HashedWheelTimer> logger,
        TaskFactory taskFactory,
        TimeSpan tickDuration,
        int ticksPerWheel) : this(logger, taskFactory, tickDuration, ticksPerWheel, 0L)
    {
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="taskFactory"></param>
    /// <param name="tickDuration"></param>
    /// <param name="ticksPerWheel"></param>
    /// <param name="maxPendingTimeouts"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public HashedWheelTimer(ILogger<HashedWheelTimer> logger,
        TaskFactory taskFactory,
        TimeSpan tickDuration,
        int ticksPerWheel,
        long maxPendingTimeouts)
    {
        if (logger == null)
        {
            _logger = NullLogger.Instance;
        }

        _taskFactory = taskFactory ?? throw new ArgumentNullException(nameof(taskFactory));

        if (ticksPerWheel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerWheel), "must be greater than 0: ");
        }

        // Normalize ticksPerWheel to power of two and initialize the wheel.
        _wheel = CreateWheel(ticksPerWheel);
        _mask = _wheel.Length - 1;

        // Convert tickDuration to nanos.
        var duration = MillisecondToTick(tickDuration.TotalMilliseconds);
        if (duration >= long.MaxValue / _wheel.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(duration)
                , $"{duration} (expected: 0 < tickDuration in ms < {long.MaxValue / _wheel.Length})");
        }


        if (duration < 10000)
        {
            _logger.LogWarning("Configured tickDuration {0} smaller then {1}, using 1ms.",
                duration, 10000);
            _tickDuration = 10000;
        }
        else
        {
            _tickDuration = duration;
        }

        _worker = new(this);
        _maxPendingTimeouts = maxPendingTimeouts;

        if (InstanceCounter.IncrementAndGet() > InstanceCountLimit
            && WarnedTooManyInstances.CompareAndSet(false, true))
        {
            ReportTooManyInstances();
        }
    }

    public void Dispose()
    {
        var ws = Interlocked.Exchange(ref _workerState, WorkerStateShutdown);
        if (ws != WorkerStateShutdown)
        {
            InstanceCounter.DecrementAndGet();
        }
    }

    /// <summary>
    ///  Starts the background thread explicitly.  The background thread will start automatically on demand 
    ///  even if you did not call this method.
    /// </summary>
    private void Start()
    {
        var workerState = Interlocked.Read(ref _workerState);
        switch (workerState)
        {
            case WorkerStateInit:
            {
                if (Interlocked.CompareExchange(ref _workerState,
                        WorkerStateStarted, WorkerStateInit) == WorkerStateInit)
                {
                    _worker.Execute();
                }

                break;
            }
            case WorkerStateStarted:
                break;
            case WorkerStateShutdown:
                throw new InvalidOperationException("cannot be started once stopped");
            default:
                throw new InvalidOperationException("Invalid WorkerState");
        }

        // Wait until the startTime is initialized by the worker.
        while (StartTime == 0)
        {
            _startTimeInitialized.Wait();
        }
    }

    public IEnumerable<HashedWheelTimeout> Stop()
    {
        // if (Task.CurrentId == _workerThread.Id)
        // {
        //     throw new ApplicationException("HashedWheelTimer.stop() cannot be called from ITimerTask");
        // }

        // workerState == WorkerStateStarted then set to WorkerStateShutdown

        if (!CompareAndSetWorkState(WorkerStateShutdown, WorkerStateStarted))
        {
            // if workerState is 0
            if (Interlocked.Exchange(ref _workerState, WorkerStateShutdown) != WorkerStateShutdown)
            {
                InstanceCounter.DecrementAndGet();
                // leak != null
            }

            return [];
        }

        try
        {
            while (!_worker.IsStoped)
            {
                Thread.Sleep(100);
            }
        }
        catch (TaskCanceledException)
        {
            Thread.CurrentThread.Abort();
        }
        finally
        {
            InstanceCounter.DecrementAndGet();
        }

        var unprocessed = _worker.UnprocessedTimeouts;
        ICollection<HashedWheelTimeout> cancelled = new HashSet<HashedWheelTimeout>();
        foreach (var timeout in unprocessed)
        {
            if (timeout.Cancel())
            {
                cancelled.Add(timeout);
            }
        }

        return cancelled;
    }

    private bool CompareAndSetWorkState(long value, long comparand)
    {
        var originState = Interlocked.CompareExchange(ref _workerState, value, comparand);
        return originState == comparand;
    }

    /// <summary>
    /// 一个 tick 是 100 纳秒
    /// </summary>
    /// <param name="milliseconds"></param>
    /// <returns></returns>
    private long MillisecondToTick(double milliseconds)
    {
        return (long)(milliseconds * 10000);
    }

    public HashedWheelTimeout NewTimeout(Func<ITimeout, Task> task, TimeSpan span)
    {
        return NewTimeout(new FuncTimerTask(task), span);
    }

    public HashedWheelTimeout NewTimeout(Action<ITimeout> task, TimeSpan span)
    {
        return NewTimeout(new ActionTimerTask(task), span);
    }

    /// <summary>
    /// Schedules the specified TimerTask for one-time execution after the specified delay.
    /// </summary>
    /// <param name="task"></param>
    /// <param name="delay"></param>
    /// <returns>a handle which is associated with the specified task</returns>
    public HashedWheelTimeout NewTimeout(ITimerTask task, TimeSpan delay)
    {
        if (task == null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        if (Interlocked.Read(ref _workerState) == WorkerStateShutdown)
        {
            throw new InvalidOperationException("cannot be started once stopped");
        }

        var pendingTimeoutsCount = _pendingTimeouts.IncrementAndGet();
        if (_maxPendingTimeouts > 0 && pendingTimeoutsCount > _maxPendingTimeouts)
        {
            _pendingTimeouts.DecrementAndGet();
            throw new InvalidOperationException(
                $"Number of pending timeouts ({pendingTimeoutsCount}) is greater than or equal to maximum allowed pending  timeouts ({_maxPendingTimeouts})");
        }

        Start();

        // Add the timeout to the timeout queue which will be processed on the next tick.
        // During processing all the queued HashedWheelTimeouts will be added to the correct HashedWheelBucket.
        var currentTicks = DateTime.Now.Ticks;
        var delayTicks = MillisecondToTick(delay.TotalMilliseconds);
        var intervalTicks = currentTicks - StartTime;
        var deadline = intervalTicks + delayTicks;

        // Guard against overflow.
        if (delay.TotalMilliseconds > 0 && deadline < 0)
        {
            deadline = long.MaxValue;
        }

        var timeout = new HashedWheelTimeout(this, task, deadline);
        _timeouts.Enqueue(timeout);
        return timeout;
    }

    private static HashedWheelBucket[] CreateWheel(int ticksPerWheel)
    {
        switch (ticksPerWheel)
        {
            case <= 0:
                throw new ArgumentOutOfRangeException(nameof(ticksPerWheel), "must be greater than 0");
            case > 1073741824:
                throw new ArgumentOutOfRangeException(nameof(ticksPerWheel), "may not be greater than 2^30");
        }

        ticksPerWheel = NormalizeTicksPerWheel(ticksPerWheel);
        var wheel = new HashedWheelBucket[ticksPerWheel];
        for (var i = 0; i < wheel.Length; i++)
        {
            wheel[i] = new HashedWheelBucket();
        }

        return wheel;
    }

    private static int NormalizeTicksPerWheel(int ticksPerWheel)
    {
        var normalizedTicksPerWheel = 1;
        while (normalizedTicksPerWheel < ticksPerWheel)
        {
            normalizedTicksPerWheel <<= 1;
        }

        return normalizedTicksPerWheel;
    }

    private void ReportTooManyInstances()
    {
        var resourceType = nameof(HashedWheelTimer);
        _logger.LogError("You are creating too many " + resourceType + " instances. " +
                         resourceType + " is a shared resource that must be reused across the JVM, " +
                         "so that only a few instances are created.");
    }

    private class Worker(HashedWheelTimer timer)
    {
        private readonly HashSet<HashedWheelTimeout> _unprocessedTimeouts = new();
        private long _tick;
        private long _stoped;

        public IReadOnlyCollection<HashedWheelTimeout> UnprocessedTimeouts => _unprocessedTimeouts;

        public bool IsStoped => Interlocked.Read(ref _stoped) == 1;

        public void Execute()
        {
            timer._taskFactory.StartNew(async () => await StartAsync(), TaskCreationOptions.LongRunning)
                .ConfigureAwait(false);
        }

        private async Task StartAsync()
        {
            // Initialize the startTime.
            timer.StartTime = DateTime.Now.Ticks;
            if (timer.StartTime == 0)
            {
                // We use 0 as an indicator for the uninitialized value here, so make sure it's not 0 when initialized.
                timer.StartTime = 1;
            }

            timer._startTimeInitialized.Signal();

            do
            {
                var deadline = await WaitForNextTickAsync();
                if (deadline > 0)
                {
                    var idx = (int)(_tick & timer._mask);
                    ProcessCancelledTasks();
                    var bucket = timer._wheel[idx];
                    TransferTimeoutsToBuckets();
                    bucket.ExpireTimeouts(deadline);
                    _tick++;
                }
            } while (Interlocked.Read(ref timer._workerState) == WorkerStateStarted);

            // Fill the unprocessedTimeouts so we can return them from stop() method.
            foreach (var bucket in timer._wheel)
            {
                bucket.ClearTimeouts(_unprocessedTimeouts);
            }

            for (;;)
            {
                if (!timer._timeouts.TryDequeue(out var timeout) || timeout == null)
                {
                    break;
                }

                if (!timeout.Cancelled)
                {
                    _unprocessedTimeouts.Add(timeout);
                }
            }

            ProcessCancelledTasks();

            Interlocked.Exchange(ref _stoped, 1);
        }

        private void TransferTimeoutsToBuckets()
        {
            // transfer only max. 100000 timeouts per tick to prevent a thread to stale the workerThread when it just
            // adds new timeouts in a loop.
            for (var i = 0; i < 100000; i++)
            {
                if (!timer._timeouts.TryDequeue(out var timeout) || timeout == null)
                {
                    // all processed
                    break;
                }

                if (timeout.State == HashedWheelTimeout.StCancelled)
                {
                    // Was cancelled in the meantime.
                    continue;
                }

                var calculated = timeout.Deadline / timer._tickDuration;
                timeout.RemainingRounds = (calculated - _tick) / timer._wheel.Length;

                var ticks = Math.Max(calculated, _tick); // Ensure we don't schedule for past.
                var stopIndex = (int)(ticks & timer._mask);

                var bucket = timer._wheel[stopIndex];
                bucket.AddTimeout(timeout);
            }
        }

        private void ProcessCancelledTasks()
        {
            for (;;)
            {
                if (!timer._cancelledTimeouts.TryDequeue(out var timeout) || timeout == null)
                {
                    // all processed
                    break;
                }

                try
                {
                    timeout.RemoveAfterCancellation();
                }
                catch (Exception e)
                {
                    timer._logger.LogError(e, "An exception was thrown while process a cancellation task");
                }
            }
        }

        private async Task<long> WaitForNextTickAsync()
        {
            // TODO: 要用 tick 计算
            var deadline = timer._tickDuration * (_tick + 1);

            for (;;)
            {
                var currentTime = DateTime.Now.Ticks - timer.StartTime;
                var sleepTicks = deadline - currentTime + 10000;

                if (sleepTicks <= 0)
                {
                    if (currentTime == long.MinValue)
                    {
                        return -long.MaxValue;
                    }

                    return currentTime;
                }

                await Task.Delay(TimeSpan.FromTicks(sleepTicks));
            }
        }
    }
}