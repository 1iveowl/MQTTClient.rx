using System.Buffers;
using System.Text;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using NetProtocolVersion = MQTTnet.Formatter.MqttProtocolVersion;
using NetUserProperty = MQTTnet.Packets.MqttUserProperty;

namespace MQTTClientRx.Internal;

/// <summary>
/// Translation between this library's immutable records and MQTTnet's mutable option/packet types.
/// </summary>
/// <remarks>
/// Kept in one place deliberately: MQTTnet has rewritten its surface twice since this library was
/// first written, and confining the coupling to a single file is what makes the next rewrite cheap.
/// </remarks>
internal static class MqttNetMapper
{
    // Reason code enums mirror the MQTT 5.0 specification values on both sides, so the numeric
    // mapping is exact. Unit tests assert this rather than trusting it.
    internal static QoSLevel ToQoSLevel(MqttQualityOfServiceLevel value) => (QoSLevel)(int)value;

    internal static MqttQualityOfServiceLevel ToMqttNet(QoSLevel value) => (MqttQualityOfServiceLevel)(int)value;

    internal static MqttConnectResultCode ToConnectResultCode(MqttClientConnectResultCode value) =>
        (MqttConnectResultCode)(int)value;

    internal static MqttSubscribeResultCode ToSubscribeResultCode(MqttClientSubscribeResultCode value) =>
        (MqttSubscribeResultCode)(int)value;

    internal static MqttUnsubscribeResultCode ToUnsubscribeResultCode(MqttClientUnsubscribeResultCode value) =>
        (MqttUnsubscribeResultCode)(int)value;

    internal static MqttPublishReasonCode ToPublishReasonCode(MqttClientPublishReasonCode value) =>
        (MqttPublishReasonCode)(int)value;

    internal static MqttDisconnectReasonCode ToDisconnectReasonCode(MqttClientDisconnectReason value) =>
        (MqttDisconnectReasonCode)(int)value;

    internal static NetProtocolVersion ToMqttNet(MqttProtocolVersion value) => (NetProtocolVersion)(int)value;

    internal static MqttRetainHandling ToMqttNet(RetainHandling value) => (MqttRetainHandling)(int)value;

    // MQTTnet stores property values as raw bytes and marks the string accessors obsolete in favour
    // of the buffer, so decode explicitly on the way in and encode on the way out.
    private static IReadOnlyList<MqttUserProperty> ToUserProperties(IEnumerable<NetUserProperty>? source) =>
        source is null
            ? []
            : [.. source.Select(property => new MqttUserProperty(property.Name, property.ReadValueAsString()))];

    private static TimeSpan? ToInterval(uint seconds) =>
        seconds == 0 ? null : TimeSpan.FromSeconds(seconds);

    private static uint ToSeconds(TimeSpan? value) =>
        value is null ? 0u : (uint)Math.Clamp(value.Value.TotalSeconds, 0, uint.MaxValue);

    private static ReadOnlyMemory<byte> Utf8(string value) => Encoding.UTF8.GetBytes(value);

    // -- Inbound ----------------------------------------------------------------------------

    /// <summary>
    /// Projects a received message, copying the payload out of the transport buffer.
    /// </summary>
    /// <remarks>
    /// The copy is deliberate. MQTTnet exposes the payload as a <c>ReadOnlySequence&lt;byte&gt;</c>
    /// whose backing memory is only guaranteed for the duration of the receive handler, while Rx
    /// hands the message to subscribers that may buffer, replay or marshal it elsewhere. One
    /// allocation per message is the price of that being safe.
    /// </remarks>
    internal static MqttMessage ToMqttMessage(MqttApplicationMessageReceivedEventArgs args)
    {
        var message = args.ApplicationMessage;

        return new MqttMessage
        {
            Topic = message.Topic,
            Payload = message.Payload.ToArray(),
            QualityOfServiceLevel = ToQoSLevel(message.QualityOfServiceLevel),
            Retain = message.Retain,
            ContentType = message.ContentType,
            PayloadFormatIndicator = message.PayloadFormatIndicator == MqttPayloadFormatIndicator.CharacterData
                ? PayloadFormat.CharacterData
                : null,
            ResponseTopic = message.ResponseTopic,
            CorrelationData = message.CorrelationData ?? ReadOnlyMemory<byte>.Empty,
            MessageExpiryInterval = ToInterval(message.MessageExpiryInterval),
            UserProperties = ToUserProperties(message.UserProperties),
            SubscriptionIdentifiers = message.SubscriptionIdentifiers is { Count: > 0 } identifiers
                ? [.. identifiers]
                : [],
            TopicAlias = message.TopicAlias == 0 ? null : message.TopicAlias,
            Acknowledgement = new MqttAcknowledgement(args)
        };
    }

    internal static MqttConnectResult ToConnectResult(MqttClientConnectResult result) => new()
    {
        ResultCode = ToConnectResultCode(result.ResultCode),
        IsSessionPresent = result.IsSessionPresent,
        AssignedClientIdentifier = result.AssignedClientIdentifier,
        ReasonString = result.ReasonString,
        ServerKeepAlive = result.ServerKeepAlive == 0 ? null : TimeSpan.FromSeconds(result.ServerKeepAlive),
        ServerReference = result.ServerReference,
        SessionExpiryInterval = result.SessionExpiryInterval is { } expiry ? TimeSpan.FromSeconds(expiry) : null,
        ReceiveMaximum = result.ReceiveMaximum,
        MaximumPacketSize = result.MaximumPacketSize,
        MaximumQoS = ToQoSLevel(result.MaximumQoS),
        RetainAvailable = result.RetainAvailable,
        WildcardSubscriptionAvailable = result.WildcardSubscriptionAvailable,
        SharedSubscriptionAvailable = result.SharedSubscriptionAvailable,
        SubscriptionIdentifiersAvailable = result.SubscriptionIdentifiersAvailable,
        UserProperties = ToUserProperties(result.UserProperties)
    };

    internal static MqttDisconnect ToDisconnect(MqttClientDisconnectedEventArgs args) => new()
    {
        ClientWasConnected = args.ClientWasConnected,
        ReasonCode = ToDisconnectReasonCode(args.Reason),
        ReasonString = args.ReasonString,
        Exception = args.Exception,
        ServerReference = args.ConnectResult?.ServerReference,
        UserProperties = ToUserProperties(args.UserProperties)
    };

    internal static MqttSubscribeResult ToSubscribeResult(
        MqttClientSubscribeResult result,
        IReadOnlyList<MqttTopicFilter> requested)
    {
        // Pair each SUBACK code back to the filter the caller asked for, by position: the
        // specification requires the broker to return codes in request order. Fall back to the
        // MQTTnet-reported filter if the counts ever disagree.
        var items = result.Items
            .Select((item, index) => new MqttSubscribeResultItem(
                index < requested.Count ? requested[index] : ToTopicFilter(item.TopicFilter),
                ToSubscribeResultCode(item.ResultCode)))
            .ToArray();

        return new MqttSubscribeResult
        {
            Items = items,
            ReasonString = result.ReasonString,
            UserProperties = ToUserProperties(result.UserProperties)
        };
    }

    internal static MqttUnsubscribeResult ToUnsubscribeResult(MqttClientUnsubscribeResult result) => new()
    {
        Items = [.. result.Items.Select(item => new MqttUnsubscribeResultItem(
            item.TopicFilter,
            ToUnsubscribeResultCode(item.ResultCode)))],
        ReasonString = result.ReasonString,
        UserProperties = ToUserProperties(result.UserProperties)
    };

    internal static MqttPublishResult ToPublishResult(MqttClientPublishResult result) => new()
    {
        ReasonCode = ToPublishReasonCode(result.ReasonCode),
        PacketIdentifier = result.PacketIdentifier,
        ReasonString = result.ReasonString,
        UserProperties = ToUserProperties(result.UserProperties)
    };

    private static MqttTopicFilter ToTopicFilter(MQTTnet.Packets.MqttTopicFilter filter) => new()
    {
        Topic = filter.Topic,
        QualityOfServiceLevel = ToQoSLevel(filter.QualityOfServiceLevel),
        NoLocal = filter.NoLocal,
        RetainAsPublished = filter.RetainAsPublished,
        RetainHandling = (RetainHandling)(int)filter.RetainHandling
    };

    // -- Outbound ---------------------------------------------------------------------------

    internal static MqttApplicationMessage ToMqttNet(MqttMessage message)
    {
        var builder = new MqttApplicationMessageBuilder()
            .WithTopic(message.Topic)
            .WithPayloadSegment(message.Payload)
            .WithQualityOfServiceLevel(ToMqttNet(message.QualityOfServiceLevel))
            .WithRetainFlag(message.Retain);

        if (message.ContentType is { Length: > 0 } contentType)
        {
            builder.WithContentType(contentType);
        }

        if (message.PayloadFormatIndicator is { } format)
        {
            builder.WithPayloadFormatIndicator((MqttPayloadFormatIndicator)(int)format);
        }

        if (message.ResponseTopic is { Length: > 0 } responseTopic)
        {
            builder.WithResponseTopic(responseTopic);
        }

        if (!message.CorrelationData.IsEmpty)
        {
            builder.WithCorrelationData(message.CorrelationData.ToArray());
        }

        if (message.MessageExpiryInterval is not null)
        {
            builder.WithMessageExpiryInterval(ToSeconds(message.MessageExpiryInterval));
        }

        foreach (var property in message.UserProperties)
        {
            builder.WithUserProperty(property.Name, Utf8(property.Value));
        }

        return builder.Build();
    }

    internal static MQTTnet.Packets.MqttTopicFilter ToMqttNet(MqttTopicFilter filter) =>
        new MqttTopicFilterBuilder()
            .WithTopic(filter.Topic)
            .WithQualityOfServiceLevel(ToMqttNet(filter.QualityOfServiceLevel))
            .WithNoLocal(filter.NoLocal)
            .WithRetainAsPublished(filter.RetainAsPublished)
            .WithRetainHandling(ToMqttNet(filter.RetainHandling))
            .Build();

    internal static MqttClientSubscribeOptions ToSubscribeOptions(IReadOnlyList<MqttTopicFilter> filters)
    {
        var builder = new MqttClientSubscribeOptionsBuilder();

        foreach (var filter in filters)
        {
            builder.WithTopicFilter(ToMqttNet(filter));
        }

        return builder.Build();
    }

    internal static MqttClientUnsubscribeOptions ToUnsubscribeOptions(IReadOnlyList<string> topics)
    {
        var builder = new MqttClientUnsubscribeOptionsBuilder();

        foreach (var topic in topics)
        {
            builder.WithTopicFilter(topic);
        }

        return builder.Build();
    }

    internal static MqttClientOptions ToMqttNet(MqttClientRxOptions options)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithProtocolVersion(ToMqttNet(options.ProtocolVersion))
            .WithClientId(options.ClientId ?? Guid.NewGuid().ToString("N"))
            .WithCleanStart(options.CleanStart)
            .WithKeepAlivePeriod(options.KeepAlivePeriod)
            .WithTimeout(options.OperationTimeout);

        ApplyTransport(builder, options);
        ApplyTls(builder, options);
        ApplyCredentials(builder, options);
        ApplyVersion5Options(builder, options);
        ApplyWill(builder, options.Will);

        return builder.Build();
    }

    private static void ApplyTransport(MqttClientOptionsBuilder builder, MqttClientRxOptions options)
    {
        if (options.ConnectionType == ConnectionType.WebSocket)
        {
            builder.WithWebSocketServer(webSocket => webSocket.WithUri(options.Uri.ToString()));
            return;
        }

        var port = options.Uri.IsDefaultPort ? DefaultTcpPort(options) : options.Uri.Port;
        builder.WithTcpServer(options.Uri.Host, port);
    }

    private static int DefaultTcpPort(MqttClientRxOptions options) =>
        options.Tls?.UseTls == true || options.Uri.Scheme.Equals("mqtts", StringComparison.OrdinalIgnoreCase)
            ? 8883
            : 1883;

    private static void ApplyTls(MqttClientOptionsBuilder builder, MqttClientRxOptions options)
    {
        if (options.Tls is not { UseTls: true } tls)
        {
            return;
        }

        builder.WithTlsOptions(target =>
        {
            target
                .UseTls(true)
                .WithAllowUntrustedCertificates(tls.AllowUntrustedCertificates)
                .WithIgnoreCertificateChainErrors(tls.IgnoreCertificateChainErrors)
                .WithIgnoreCertificateRevocationErrors(tls.IgnoreCertificateRevocationErrors)
                .WithSslProtocols(tls.SslProtocols)
                .WithRevocationMode(tls.RevocationMode)
                .WithTargetHost(tls.TargetHost ?? options.Uri.Host);

            if (tls.ClientCertificates.Count > 0)
            {
                target.WithClientCertificates(tls.ClientCertificates);
            }

            if (tls.TrustChain is { Count: > 0 } chain)
            {
                target.WithTrustChain(chain);
            }

            if (tls.CertificateValidationHandler is { } validate)
            {
                target.WithCertificateValidationHandler(args =>
                    validate(args.Certificate, args.Chain, args.SslPolicyErrors));
            }
        });
    }

    private static void ApplyCredentials(MqttClientOptionsBuilder builder, MqttClientRxOptions options)
    {
        if (options.Credentials is { } credentials)
        {
            builder.WithCredentials(credentials.UserName, credentials.Password.ToArray());
        }
    }

    private static void ApplyVersion5Options(MqttClientOptionsBuilder builder, MqttClientRxOptions options)
    {
        // Validate() has already rejected these under 3.1.x, so reaching here means MQTT 5.0.
        if (options.SessionExpiryInterval is not null)
        {
            builder.WithSessionExpiryInterval(ToSeconds(options.SessionExpiryInterval));
        }

        if (options.ReceiveMaximum is { } receiveMaximum)
        {
            builder.WithReceiveMaximum(receiveMaximum);
        }

        if (options.MaximumPacketSize is { } maximumPacketSize)
        {
            builder.WithMaximumPacketSize(maximumPacketSize);
        }

        if (options.TopicAliasMaximum is { } topicAliasMaximum)
        {
            builder.WithTopicAliasMaximum(topicAliasMaximum);
        }

        foreach (var property in options.UserProperties)
        {
            builder.WithUserProperty(property.Name, Utf8(property.Value));
        }
    }

    private static void ApplyWill(MqttClientOptionsBuilder builder, MqttWillMessage? will)
    {
        if (will is null)
        {
            return;
        }

        builder
            .WithWillTopic(will.Topic)
            .WithWillPayload(will.Payload.ToArray())
            .WithWillQualityOfServiceLevel(ToMqttNet(will.QualityOfServiceLevel))
            .WithWillRetain(will.Retain);

        if (will.DelayInterval is not null)
        {
            builder.WithWillDelayInterval(ToSeconds(will.DelayInterval));
        }

        if (will.MessageExpiryInterval is not null)
        {
            builder.WithWillMessageExpiryInterval(ToSeconds(will.MessageExpiryInterval));
        }

        if (will.ContentType is { Length: > 0 } contentType)
        {
            builder.WithWillContentType(contentType);
        }

        if (will.PayloadFormatIndicator is { } format)
        {
            builder.WithWillPayloadFormatIndicator((MqttPayloadFormatIndicator)(int)format);
        }

        if (will.ResponseTopic is { Length: > 0 } responseTopic)
        {
            builder.WithWillResponseTopic(responseTopic);
        }

        if (!will.CorrelationData.IsEmpty)
        {
            builder.WithWillCorrelationData(will.CorrelationData.ToArray());
        }

        foreach (var property in will.UserProperties)
        {
            builder.WithWillUserProperty(property.Name, Utf8(property.Value));
        }
    }
}
