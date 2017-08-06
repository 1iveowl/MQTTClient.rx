using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using MQTTClientRx.Model;

namespace MQTTClientRx.Extension
{
    internal static class InternalExtensionMethods
    {
        internal static IObservable<T> FinallyAsync<T>(this IObservable<T> source, Func<Task> finalAsync)
        {
            return source
                .Materialize()
                .SelectMany(async n =>
                {
                    switch (n.Kind)
                    {
                        case NotificationKind.OnCompleted:
                            Debug.WriteLine("------ OnCompleted -----");
                            await finalAsync();
                            return n;
                        case NotificationKind.OnError:
                            Debug.WriteLine("------ OnError -----");
                            await finalAsync();
                            return n;
                        case NotificationKind.OnNext:
                            return n;
                        default:throw new NotImplementedException();
                    }
                })
                .Dematerialize();
        }
    }

    //public static class ExtensionMethods
    //{
    //    public static DisposableAsync ToAsyncDisposal(this IDisposable disposable, Func<Task> asyncDisposalAction)
    //    {
    //        return new DisposableAsync(disposable, asyncDisposalAction);
    //    }
    //}
}
