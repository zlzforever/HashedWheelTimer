using System;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace HWT;

/// <summary>
/// A handle associated with a TimerTask that is returned by a HashedWheelTimer
/// </summary>
public sealed class HashedWheelTimeout : ITimeout
{
    private const int StInit = 0;
    public const int StCancelled = 1;
    private const int StExpired = 2;
    private volatile int _state;

    public int State => _state;

    /// <summary>
    /// Tick
    /// </summary>
    public long Deadline { get; }

    /// <summary>
    /// remainingRounds will be calculated and set by Worker.transferTimeoutsToBuckets() before the
    /// HashedWheelTimeout will be added to the correct HashedWheelBucket.
    /// </summary>
    public long RemainingRounds { get; set; }

    // This will be used to chain timeouts in HashedWheelTimerBucket via a double-linked-list.
    // As only the workerThread will act on it there is no need for synchronization / volatile.
    public HashedWheelTimeout Next { get; set; }
    public HashedWheelTimeout Prev { get; set; }

    /// <summary>
    /// The bucket to which the timeout was added
    /// </summary>
    public HashedWheelBucket Bucket { get; set; }

    /// <summary>
    /// Returns the Timer that created this handle.
    /// </summary>
    public HashedWheelTimer Timer { get; }

    /// <summary>
    /// Returns the TimerTask which is associated with this handle.
    /// </summary>
    public ITimerTask TimerTask { get; }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="timer"></param>
    /// <param name="timerTask"></param>
    /// <param name="deadline">Tick</param>
    public HashedWheelTimeout(HashedWheelTimer timer, ITimerTask timerTask, long deadline)
    {
        Timer = timer;
        TimerTask = timerTask;
        Deadline = deadline;
        Interlocked.Exchange(ref _state, StInit);
    }

    /// <summary>
    /// Returns true if and only if the TimerTask associated
    /// with this handle has been expired
    /// </summary>
    public bool Expired => _state == StExpired;

    /// <summary>
    /// Returns true if and only if the TimerTask associated
    /// with this handle has been cancelled
    /// </summary>
    public bool Cancelled => _state == StCancelled;


    /// <summary>
    /// Attempts to cancel the {@link TimerTask} associated with this handle.
    /// If the task has been executed or cancelled already, it will return with no side effect.
    /// </summary>
    /// <returns>True if the cancellation completed successfully, otherwise false</returns>
    public bool Cancel()
    {
        // only update the state it will be removed from HashedWheelBucket on next tick.
        if (!CompareAndSetState(StInit, StCancelled))
        {
            return false;
        }

        // If a task should be canceled we put this to another queue which will be processed on each tick.
        // So this means that we will have a GC latency of max. 1 tick duration which is good enough. This way
        // we can make again use of our MpscLinkedQueue and so minimize the locking / overhead as much as possible.
        Timer.EnqueueCanceledTimeout(this);
        return true;
    }

    private void Remove()
    {
        if (Bucket != null)
        {
            Bucket.Remove(this);
        }

        Timer.DecrementPendingTimeouts();
    }

    public void RemoveAfterCancellation()
    {
        Remove();
        TimerTask.Cancel(this);
    }

    public void Expire()
    {
        if (!CompareAndSetState(StInit, StExpired))
        {
            return;
        }

        try
        {
            Remove();
            Timer.TaskFactory.StartNew(async obj =>
            {
                var timeout = (HashedWheelTimeout)obj;
                try
                {
                    await TimerTask.RunAsync(timeout);
                }
                catch (Exception e)
                {
                    Timer.Logger.LogError(e, "An exception was thrown by {TimerTask}", TimerTask.GetType().Name);
                }
            }, this).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Timer.Logger.LogError(e, "Error while executing timer task");
        }
    }

    public override string ToString()
    {
        var currentTime = DateTimeHelper.TotalMilliseconds;
        var remaining = Deadline - currentTime + Timer.StartTime;

        var buf = new StringBuilder(192)
            .Append(GetType().Name)
            .Append('(')
            .Append("deadline: ");
        if (remaining > 0)
        {
            buf.Append(remaining)
                .Append(" ms later");
        }
        else if (remaining < 0)
        {
            buf.Append(-remaining)
                .Append(" ms ago");
        }
        else
        {
            buf.Append("now");
        }

        if (Cancelled)
        {
            buf.Append(", cancelled");
        }

        return buf.Append(", task: ")
            .Append(TimerTask)
            .Append(')')
            .ToString();
    }

    private bool CompareAndSetState(int expected, int state)
    {
        var originalState = Interlocked.CompareExchange(ref _state,
            state, expected);
        return originalState == expected;
    }
}