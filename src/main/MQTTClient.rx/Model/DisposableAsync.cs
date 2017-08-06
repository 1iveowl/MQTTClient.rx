using System;
using System.Threading.Tasks;
using IMQTTClientRx.Model;

namespace MQTTClientRx.Model
{
    // https://stackoverflow.com/a/45305859/4140832
    //public class DisposableAsync : IDisposableAsync
    //{
    //    private readonly IDisposable _disposable;
    //    private readonly Func<Task> _asyncDisposalAction;

    //    internal DisposableAsync(IDisposable disposable, Func<Task> asyncDisposalAction)
    //    {
    //        _disposable = disposable;
    //        _asyncDisposalAction = asyncDisposalAction;
    //    }

    //    public async Task DisposeAsync()
    //    {
    //        _disposable.Dispose();
    //        await _asyncDisposalAction();
    //    }
    //}
}
