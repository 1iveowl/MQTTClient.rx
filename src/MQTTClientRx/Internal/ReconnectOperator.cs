using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace MQTTClientRx.Internal;

/// <summary>Tracks consecutive failed connection attempts, reset by each successful connect.</summary>
internal sealed class AttemptCounter
{
    private int _value;

    internal int Current => Volatile.Read(ref _value);

    internal int Increment() => Interlocked.Increment(ref _value);

    internal void Reset() => Interlocked.Exchange(ref _value, 0);
}

internal static class ReconnectOperator
{
    /// <summary>
    /// Resubscribes to <paramref name="source"/> after a failure, following
    /// <paramref name="policy"/>'s backoff curve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every delay goes through <paramref name="scheduler"/>, so the whole curve - including the
    /// give-up decision - runs in virtual time under a <c>TestScheduler</c> rather than needing
    /// minutes of wall clock to verify.
    /// </para>
    /// <para>
    /// Jitter is supplied by <paramref name="nextJitter"/> rather than drawn internally, for the
    /// same reason.
    /// </para>
    /// </remarks>
    /// <param name="source">The connection-backed sequence to keep alive.</param>
    /// <param name="policy">Backoff and give-up rules.</param>
    /// <param name="scheduler">Drives retry delays.</param>
    /// <param name="attempts">Shared counter, reset by the caller on a successful connect.</param>
    /// <param name="nextJitter">Produces a value in [0,1) used to spread each delay.</param>
    /// <param name="onRetryScheduled">Notified when a retry is scheduled, before the delay elapses.</param>
    internal static IObservable<T> RetryWithPolicy<T>(
        this IObservable<T> source,
        ReconnectPolicy policy,
        IScheduler scheduler,
        AttemptCounter attempts,
        Func<double> nextJitter,
        Action<MqttReconnectContext, TimeSpan> onRetryScheduled)
    {
        if (!policy.Enabled)
        {
            return source;
        }

        return Observable.Create<T>(observer =>
        {
            // Two slots, deliberately. Sharing one would let a scheduler that runs work inline
            // overwrite - and therefore dispose - the subscription the scheduled work just created.
            var timerSlot = new SerialDisposable();
            var subscriptionSlot = new SerialDisposable();
            var stopped = new BooleanDisposable();

            void SubscribeToSource()
            {
                if (stopped.IsDisposed)
                {
                    return;
                }

                // The slot is claimed *before* subscribing, not after. A source that errors
                // synchronously runs OnSourceError inside Subscribe, and if the scheduler then runs
                // the retry inline the new subscription lands in the slot first - so assigning the
                // outer Subscribe's return value afterwards would overwrite and dispose it, leaving
                // the retry silently dead. Assigning through a SingleAssignmentDisposable makes the
                // late arrival dispose itself instead.
                var subscription = new SingleAssignmentDisposable();
                subscriptionSlot.Disposable = subscription;

                subscription.Disposable = source.Subscribe(
                    observer.OnNext,
                    OnSourceError,
                    observer.OnCompleted);
            }

            void OnSourceError(Exception exception)
            {
                if (stopped.IsDisposed)
                {
                    return;
                }

                var context = new MqttReconnectContext(attempts.Increment(), exception);

                if (!policy.AllowsRetry(context))
                {
                    observer.OnError(exception);
                    return;
                }

                var delay = policy.GetDelay(context.Attempt, nextJitter());
                onRetryScheduled(context, delay);

                timerSlot.Disposable = scheduler.Schedule(delay, SubscribeToSource);
            }

            SubscribeToSource();

            return new CompositeDisposable(stopped, subscriptionSlot, timerSlot);
        });
    }
}
