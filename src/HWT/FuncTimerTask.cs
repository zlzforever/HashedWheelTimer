using System;
using System.Threading.Tasks;

namespace HWT;

public class FuncTimerTask(Func<ITimeout, Task> func) : ITimerTask
{
    public async Task RunAsync(ITimeout timeout)
    {
        await func(timeout);
    }
}