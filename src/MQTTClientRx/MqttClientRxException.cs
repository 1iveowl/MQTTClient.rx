namespace MQTTClientRx;

/// <summary>Base type for every error this library surfaces through <see cref="IObserver{T}.OnError"/>.</summary>
public class MqttClientRxException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public MqttClientRxException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public MqttClientRxException(string message) : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner cause.</summary>
    public MqttClientRxException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The broker refused the connection, or the transport failed before a session was established.
/// </summary>
/// <remarks>
/// MQTTnet 5 returns a CONNACK with a failure reason code rather than throwing, so a broker
/// rejecting bad credentials no longer produces an exception of its own. This type carries that
/// reason code onto the error channel where a consumer can actually see it.
/// </remarks>
public sealed class MqttConnectFailedException : MqttClientRxException
{
    /// <summary>Creates an exception describing a rejected CONNECT.</summary>
    public MqttConnectFailedException(MqttConnectResult result, Uri uri)
        : base(BuildMessage(result, uri))
    {
        Result = result;
    }

    /// <summary>Creates an exception describing a transport failure during CONNECT.</summary>
    public MqttConnectFailedException(Uri uri, Exception innerException)
        : base($"Unable to connect to {uri}: {innerException.Message}", innerException)
    {
        Result = new MqttConnectResult { ResultCode = MqttConnectResultCode.UnspecifiedError };
    }

    /// <summary>The CONNACK, when one was received.</summary>
    public MqttConnectResult Result { get; }

    /// <summary>Reason the broker gave for refusing the connection.</summary>
    public MqttConnectResultCode ResultCode => Result.ResultCode;

    private static string BuildMessage(MqttConnectResult result, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(result);

        var reason = result.ReasonString is { Length: > 0 } text ? $" ({text})" : string.Empty;
        return $"Unable to connect to {uri}: {result.ResultCode}{reason}.";
    }
}

/// <summary>The broker refused one or more requested topic filters.</summary>
public sealed class MqttSubscribeFailedException : MqttClientRxException
{
    /// <summary>Creates an exception describing a partially or wholly refused SUBSCRIBE.</summary>
    public MqttSubscribeFailedException(MqttSubscribeResult result)
        : base(BuildMessage(result))
    {
        Result = result;
    }

    /// <summary>The full SUBACK, including the filters that succeeded.</summary>
    public MqttSubscribeResult Result { get; }

    private static string BuildMessage(MqttSubscribeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var failures = string.Join(", ", result.Failures.Select(item => $"'{item.TopicFilter.Topic}' -> {item.ResultCode}"));
        return $"The broker refused {result.Failures.Count()} of {result.Items.Count} topic filter(s): {failures}.";
    }
}

/// <summary>An established connection ended.</summary>
/// <remarks>
/// This is the error that drives reconnection. It reaches subscribers only when the
/// <see cref="ReconnectPolicy"/> declines to retry - otherwise it is consumed internally and the
/// stream continues across the new connection.
/// </remarks>
public sealed class MqttDisconnectedException : MqttClientRxException
{
    /// <summary>Creates an exception describing a connection that ended.</summary>
    public MqttDisconnectedException(MqttDisconnect disconnect)
        : base($"Disconnected from the broker: {disconnect}.", disconnect.Exception ?? new MqttClientRxException("Connection closed."))
    {
        Disconnect = disconnect;
    }

    /// <summary>Details of the disconnect, including the MQTT 5 reason code where available.</summary>
    public MqttDisconnect Disconnect { get; }
}

/// <summary>
/// An operation needing a live connection was attempted while none existed.
/// </summary>
/// <remarks>
/// The connection is owned by the subscription to <see cref="IMqttClientRx.Messages"/>. Publishing or
/// subscribing before anything has subscribed - or after the last subscription was disposed - has no
/// connection to act on.
/// </remarks>
public sealed class MqttNotConnectedException : MqttClientRxException
{
    /// <summary>Creates an exception describing an operation attempted with no connection.</summary>
    public MqttNotConnectedException(string operation)
        : base($"Cannot {operation}: not connected. The connection is established by subscribing to " +
               $"{nameof(IMqttClientRx)}.{nameof(IMqttClientRx.Messages)} and torn down when the last " +
               "subscription is disposed.")
    {
    }
}

/// <summary>The supplied options are not internally consistent, or ask for something the chosen protocol version cannot express.</summary>
public sealed class MqttOptionsException : MqttClientRxException
{
    /// <summary>Creates an exception describing invalid options.</summary>
    public MqttOptionsException(string message) : base(message)
    {
    }
}

/// <summary>
/// An <c>await foreach</c> consumer fell far enough behind to fill the read buffer, under
/// <see cref="BufferOverflowStrategy.Fail"/>.
/// </summary>
/// <remarks>
/// At QoS 1 and 2 with <see cref="AcknowledgementMode.Manual"/> this should be unreachable, because
/// the broker stops sending once <see cref="MqttClientRxOptions.ReceiveMaximum"/> messages are
/// outstanding. Seeing it usually means either QoS 0 traffic, which has no flow control, or a buffer
/// smaller than the receive-maximum window.
/// </remarks>
public sealed class MqttBufferOverflowException : MqttClientRxException
{
    /// <summary>Creates an exception describing a full read buffer.</summary>
    public MqttBufferOverflowException(int bufferCapacity)
        : base($"The read buffer of {bufferCapacity} message(s) overflowed because the consumer could " +
               $"not keep up. Increase {nameof(MqttReadOptions)}.{nameof(MqttReadOptions.BufferCapacity)}, " +
               $"consume faster, or choose a different {nameof(MqttReadOptions.OverflowStrategy)}.")
    {
        BufferCapacity = bufferCapacity;
    }

    /// <summary>The capacity that was exceeded.</summary>
    public int BufferCapacity { get; }
}
