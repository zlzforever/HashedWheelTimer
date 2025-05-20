using System.Threading.Tasks;

namespace HWT;

public class TimerTask : ITimerTask
{
    public virtual Task RunAsync(ITimeout timeout)
    {
        return Task.CompletedTask;
    }

    public virtual void Cancel(ITimeout _)
    {
    }
}