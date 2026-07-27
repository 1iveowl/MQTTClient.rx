using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using Microsoft.Reactive.Testing;
using MQTTClientRx.Internal;

namespace MQTTClientRx.Tests;

/// <summary>
/// The reconnect curve is entirely scheduler-driven, so it can be verified exactly in virtual time
/// instead of by waiting out real delays.
/// </summary>
public class ReconnectOperatorTests : ReactiveTest
{
    private static readonly ReconnectPolicy NoJitter = new()
    {
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(8),
        BackoffFactor = 2.0,
        JitterFactor = 0
    };

    [Fact]
    public void Retries_follow_the_backoff_curve()
    {
        var scheduler = new TestScheduler();
        var subscribedAt = new List<long>();
        var source = FailingSource(scheduler, subscribedAt);

        var policy = NoJitter with { MaxAttempts = 4 };
        var observer = scheduler.CreateObserver<int>();

        using var subscription = source
            .RetryWithPolicy(policy, scheduler, new AttemptCounter(), () => 0.5, (_, _) => { })
            .Subscribe(observer);

        scheduler.Start();

        // Initial attempt at t=0, then delays of 1s, 2s, 4s and 8s (clamped) accumulating.
        Assert.Equal(
            [
                0,
                TimeSpan.FromSeconds(1).Ticks,
                TimeSpan.FromSeconds(3).Ticks,
                TimeSpan.FromSeconds(7).Ticks,
                TimeSpan.FromSeconds(15).Ticks
            ],
            subscribedAt);
    }

    [Fact]
    public void Gives_up_after_MaxAttempts_and_reports_the_last_failure()
    {
        var scheduler = new TestScheduler();
        var subscribedAt = new List<long>();
        var source = FailingSource(scheduler, subscribedAt);

        var policy = NoJitter with { MaxAttempts = 2 };
        var observer = scheduler.CreateObserver<int>();

        using var subscription = source
            .RetryWithPolicy(policy, scheduler, new AttemptCounter(), () => 0.5, (_, _) => { })
            .Subscribe(observer);

        scheduler.Start();

        // Initial attempt plus two retries.
        Assert.Equal(3, subscribedAt.Count);

        var terminal = Assert.Single(observer.Messages, message => message.Value.Kind == System.Reactive.NotificationKind.OnError);
        Assert.IsType<MqttClientRxException>(terminal.Value.Exception);
    }

    [Fact]
    public void A_disabled_policy_passes_the_failure_straight_through()
    {
        var scheduler = new TestScheduler();
        var subscribedAt = new List<long>();
        var source = FailingSource(scheduler, subscribedAt);

        var observer = scheduler.CreateObserver<int>();

        using var subscription = source
            .RetryWithPolicy(ReconnectPolicy.None, scheduler, new AttemptCounter(), () => 0.5, (_, _) => { })
            .Subscribe(observer);

        scheduler.Start();

        Assert.Single(subscribedAt);
    }

    [Fact]
    public void A_fatal_reason_code_stops_the_loop_immediately()
    {
        var scheduler = new TestScheduler();
        var subscribedAt = new List<long>();

        var source = Observable.Create<int>(observer =>
        {
            subscribedAt.Add(scheduler.Clock);
            observer.OnError(new MqttConnectFailedException(
                new MqttConnectResult { ResultCode = MqttConnectResultCode.NotAuthorized },
                new Uri("mqtt://broker:1883")));
            return Disposable.Empty;
        });

        var observer = scheduler.CreateObserver<int>();

        using var subscription = source
            .RetryWithPolicy(ReconnectPolicy.Default, scheduler, new AttemptCounter(), () => 0.5, (_, _) => { })
            .Subscribe(observer);

        scheduler.Start();

        Assert.Single(subscribedAt);
        Assert.Contains(observer.Messages, message => message.Value.Kind == System.Reactive.NotificationKind.OnError);
    }

    [Fact]
    public void Disposing_stops_further_retries()
    {
        var scheduler = new TestScheduler();
        var subscribedAt = new List<long>();
        var source = FailingSource(scheduler, subscribedAt);

        var observer = scheduler.CreateObserver<int>();

        var subscription = source
            .RetryWithPolicy(ReconnectPolicy.Default, scheduler, new AttemptCounter(), () => 0.5, (_, _) => { })
            .Subscribe(observer);

        // Let the first retry land, then walk away.
        scheduler.AdvanceTo(TimeSpan.FromSeconds(2).Ticks);
        var beforeDispose = subscribedAt.Count;

        subscription.Dispose();
        scheduler.AdvanceTo(TimeSpan.FromMinutes(5).Ticks);

        Assert.Equal(beforeDispose, subscribedAt.Count);
    }

    [Fact]
    public void Retry_notifications_carry_the_attempt_number_and_the_next_delay()
    {
        var scheduler = new TestScheduler();
        var source = FailingSource(scheduler, []);
        var notified = new List<(int Attempt, TimeSpan Delay)>();

        var policy = NoJitter with { MaxAttempts = 3 };
        var observer = scheduler.CreateObserver<int>();

        using var subscription = source
            .RetryWithPolicy(
                policy,
                scheduler,
                new AttemptCounter(),
                () => 0.5,
                (context, delay) => notified.Add((context.Attempt, delay)))
            .Subscribe(observer);

        scheduler.Start();

        Assert.Equal(
            [(1, TimeSpan.FromSeconds(1)), (2, TimeSpan.FromSeconds(2)), (3, TimeSpan.FromSeconds(4))],
            notified);
    }

    [Fact]
    public void A_successful_connection_resets_the_backoff_curve()
    {
        var scheduler = new TestScheduler();
        var attempts = new AttemptCounter();

        attempts.Increment();
        attempts.Increment();
        Assert.Equal(2, attempts.Current);

        attempts.Reset();

        var delays = new List<TimeSpan>();
        var source = FailingSource(scheduler, []);
        var observer = scheduler.CreateObserver<int>();

        using var subscription = source
            .RetryWithPolicy(
                NoJitter with { MaxAttempts = 1 },
                scheduler,
                attempts,
                () => 0.5,
                (_, delay) => delays.Add(delay))
            .Subscribe(observer);

        scheduler.Start();

        // Back to the initial delay rather than resuming near the ceiling.
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(delays));
    }

    [Fact]
    public void A_retry_that_stays_connected_is_not_disposed_by_the_attempt_that_failed()
    {
        // The first attempt fails synchronously, so the retry is subscribed *inside* that Subscribe
        // call when the scheduler runs inline. If the operator parks the subscription in its slot
        // after Subscribe returns, that late assignment overwrites - and disposes - the live retry,
        // and the stream goes silent while looking perfectly healthy.
        var live = new Subject<int>();
        var attempts = 0;

        var source = Observable.Create<int>(observer =>
        {
            attempts++;

            if (attempts == 1)
            {
                observer.OnError(new MqttClientRxException("first attempt fails"));
                return Disposable.Empty;
            }

            return live.Subscribe(observer);
        });

        var received = new List<int>();

        using var subscription = source
            .RetryWithPolicy(
                new ReconnectPolicy { InitialDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero, JitterFactor = 0 },
                ImmediateScheduler.Instance,
                new AttemptCounter(),
                () => 0.5,
                (_, _) => { })
            .Subscribe(received.Add);

        Assert.Equal(2, attempts);
        Assert.True(live.HasObservers, "the retry's subscription was disposed by the failed attempt");

        live.OnNext(42);

        Assert.Equal([42], received);
    }

    private static IObservable<int> FailingSource(TestScheduler scheduler, List<long> subscribedAt) =>
        Observable.Create<int>(observer =>
        {
            subscribedAt.Add(scheduler.Clock);
            observer.OnError(new MqttClientRxException("connection lost"));
            return Disposable.Empty;
        });
}
