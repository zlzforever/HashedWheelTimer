using System.Threading.Tasks;

namespace HWT
{
    /// <summary>
    /// A task which is executed after the delay specified with Timer.NewTimeout(TimerTask, long, TimeUnit).
    /// </summary>
    public interface ITimerTask
    {
        /// <summary>
        /// Executed after the delay specified with Timer.NewTimeout(TimerTask, long, TimeUnit)
        /// </summary>
        /// <param name="timeout">timeout a handle which is associated with this task</param>
        Task RunAsync(ITimeout timeout);
    }
}