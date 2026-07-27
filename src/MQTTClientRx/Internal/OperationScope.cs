namespace MQTTClientRx.Internal;

/// <summary>
/// Bounds a network operation by three things at once: the caller's cancellation token, the client's
/// lifetime, and the configured operation timeout.
/// </summary>
/// <remarks>
/// The timeout is driven by an injected <see cref="System.TimeProvider"/> rather than
/// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>'s system timer. Rx's
/// <c>IScheduler</c> covers the observable pipeline but has no bearing on a
/// <see cref="CancellationTokenSource"/>, so without this these timeouts would be the one part of
/// the library that could only be tested by waiting in real time.
/// </remarks>
internal readonly struct OperationScope : IDisposable
{
    private readonly CancellationTokenSource _timeout;
    private readonly CancellationTokenSource _linked;

    internal OperationScope(
        CancellationToken caller,
        CancellationToken lifetime,
        TimeSpan timeout,
        TimeProvider timeProvider)
    {
        _timeout = new CancellationTokenSource(timeout, timeProvider);

        try
        {
            _linked = CancellationTokenSource.CreateLinkedTokenSource([caller, lifetime, _timeout.Token]);
        }
        catch
        {
            _timeout.Dispose();
            throw;
        }
    }

    internal CancellationToken Token => _linked.Token;

    public void Dispose()
    {
        _linked.Dispose();
        _timeout.Dispose();
    }
}
