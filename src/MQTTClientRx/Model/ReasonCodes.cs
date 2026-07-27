namespace MQTTClientRx;

// Reason codes defined by the MQTT 5.0 specification. The numeric values are the wire values and
// are identical to MQTTnet's, which is what lets the mapper cast rather than switch. EnumParityTests
// asserts that rather than trusting it.

/// <summary>CONNACK reason code. Values match the MQTT 5.0 specification.</summary>
public enum MqttConnectResultCode
{
#pragma warning disable CS1591
    Success = 0,
    UnspecifiedError = 128,
    MalformedPacket = 129,
    ProtocolError = 130,
    ImplementationSpecificError = 131,
    UnsupportedProtocolVersion = 132,
    ClientIdentifierNotValid = 133,
    BadUserNameOrPassword = 134,
    NotAuthorized = 135,
    ServerUnavailable = 136,
    ServerBusy = 137,
    Banned = 138,
    BadAuthenticationMethod = 140,
    TopicNameInvalid = 144,
    PacketTooLarge = 149,
    QuotaExceeded = 151,
    PayloadFormatInvalid = 153,
    RetainNotSupported = 154,
    QoSNotSupported = 155,
    UseAnotherServer = 156,
    ServerMoved = 157,
    ConnectionRateExceeded = 159
#pragma warning restore CS1591
}

/// <summary>SUBACK reason code. Values match the MQTT 5.0 specification.</summary>
public enum MqttSubscribeResultCode
{
#pragma warning disable CS1591
    GrantedQoS0 = 0,
    GrantedQoS1 = 1,
    GrantedQoS2 = 2,
    UnspecifiedError = 128,
    ImplementationSpecificError = 131,
    NotAuthorized = 135,
    TopicFilterInvalid = 143,
    PacketIdentifierInUse = 145,
    QuotaExceeded = 151,
    SharedSubscriptionsNotSupported = 158,
    SubscriptionIdentifiersNotSupported = 161,
    WildcardSubscriptionsNotSupported = 162
#pragma warning restore CS1591
}

/// <summary>UNSUBACK reason code. Values match the MQTT 5.0 specification.</summary>
public enum MqttUnsubscribeResultCode
{
#pragma warning disable CS1591
    Success = 0,
    NoSubscriptionExisted = 17,
    UnspecifiedError = 128,
    ImplementationSpecificError = 131,
    NotAuthorized = 135,
    TopicFilterInvalid = 143,
    PacketIdentifierInUse = 145
#pragma warning restore CS1591
}

/// <summary>PUBACK / PUBREC reason code. Values match the MQTT 5.0 specification.</summary>
public enum MqttPublishReasonCode
{
#pragma warning disable CS1591
    Success = 0,
    NoMatchingSubscribers = 16,
    UnspecifiedError = 128,
    ImplementationSpecificError = 131,
    NotAuthorized = 135,
    TopicNameInvalid = 144,
    PacketIdentifierInUse = 145,
    QuotaExceeded = 151,
    PayloadFormatInvalid = 153
#pragma warning restore CS1591
}

/// <summary>DISCONNECT reason code. Values match the MQTT 5.0 specification.</summary>
public enum MqttDisconnectReasonCode
{
#pragma warning disable CS1591
    NormalDisconnection = 0,
    DisconnectWithWillMessage = 4,
    UnspecifiedError = 128,
    MalformedPacket = 129,
    ProtocolError = 130,
    ImplementationSpecificError = 131,
    NotAuthorized = 135,
    ServerBusy = 137,
    ServerShuttingDown = 139,
    BadAuthenticationMethod = 140,
    KeepAliveTimeout = 141,
    SessionTakenOver = 142,
    TopicFilterInvalid = 143,
    TopicNameInvalid = 144,
    ReceiveMaximumExceeded = 147,
    TopicAliasInvalid = 148,
    PacketTooLarge = 149,
    MessageRateTooHigh = 150,
    QuotaExceeded = 151,
    AdministrativeAction = 152,
    PayloadFormatInvalid = 153,
    RetainNotSupported = 154,
    QosNotSupported = 155,
    UseAnotherServer = 156,
    ServerMoved = 157,
    SharedSubscriptionsNotSupported = 158,
    ConnectionRateExceeded = 159,
    MaximumConnectTime = 160,
    SubscriptionIdentifiersNotSupported = 161,
    WildcardSubscriptionsNotSupported = 162
#pragma warning restore CS1591
}
