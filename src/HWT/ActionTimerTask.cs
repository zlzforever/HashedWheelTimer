using System;
using System.Threading.Tasks;

namespace HWT;

public class ActionTimerTask(Action<ITimeout> action) : ITimerTask
{
    public Task RunAsync(ITimeout timeout)
    {
        action(timeout);
        return Task.CompletedTask;
    }
}