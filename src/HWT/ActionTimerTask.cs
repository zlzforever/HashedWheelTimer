using System;
using System.Threading.Tasks;

namespace HWT;

public class ActionTimerTask(Action<ITimeout> action) : TimerTask
{
    public override Task RunAsync(ITimeout timeout)
    {
        action(timeout);
        return Task.CompletedTask;
    }
}