namespace MQTTClientRx.Tests;

public class OptionsValidationTests
{
    private static MqttClientRxOptions Valid => new() { Uri = new Uri("mqtt://broker:1883") };

    [Fact]
    public void Defaults_to_MQTT_5()
    {
        Assert.Equal(MqttProtocolVersion.V500, Valid.ProtocolVersion);
    }

    [Fact]
    public void Accepts_a_plain_tcp_configuration()
    {
        Valid.Validate();
    }

    [Theory]
    [InlineData("mqtt://broker:1883", ConnectionType.WebSocket)]
    [InlineData("ws://broker:8000/mqtt", ConnectionType.Tcp)]
    [InlineData("wss://broker/mqtt", ConnectionType.Tcp)]
    [InlineData("mqtts://broker:8883", ConnectionType.WebSocket)]
    public void Rejects_a_scheme_that_contradicts_the_connection_type(string uri, ConnectionType connectionType)
    {
        var options = Valid with { Uri = new Uri(uri), ConnectionType = connectionType };

        var exception = Assert.Throws<MqttOptionsException>(options.Validate);
        Assert.Contains("ConnectionType", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ws://broker:8000/mqtt")]
    [InlineData("wss://broker/mqtt")]
    public void Accepts_websocket_schemes_with_the_websocket_connection_type(string uri)
    {
        (Valid with { Uri = new Uri(uri), ConnectionType = ConnectionType.WebSocket }).Validate();
    }

    [Fact]
    public void Rejects_a_non_positive_operation_timeout()
    {
        var options = Valid with { OperationTimeout = TimeSpan.Zero };

        Assert.Throws<MqttOptionsException>(options.Validate);
    }

    // -- MQTT 5.0 features under an earlier protocol version --------------------------------

    public static TheoryData<string, MqttClientRxOptions> Version5OnlyOptions => new()
    {
        { nameof(MqttClientRxOptions.SessionExpiryInterval), Valid with { SessionExpiryInterval = TimeSpan.FromMinutes(5) } },
        { nameof(MqttClientRxOptions.ReceiveMaximum), Valid with { ReceiveMaximum = 10 } },
        { nameof(MqttClientRxOptions.MaximumPacketSize), Valid with { MaximumPacketSize = 1024 } },
        { nameof(MqttClientRxOptions.TopicAliasMaximum), Valid with { TopicAliasMaximum = 5 } },
        { nameof(MqttClientRxOptions.UserProperties), Valid with { UserProperties = [new MqttUserProperty("a", "b")] } },
        {
            "Will.DelayInterval",
            Valid with { Will = MqttWillMessage.FromString("t", "bye") with { DelayInterval = TimeSpan.FromSeconds(30) } }
        },
        {
            "TopicFilter.NoLocal",
            Valid with { TopicFilters = [MqttTopicFilter.Create("a/#") with { NoLocal = true }] }
        },
        {
            "TopicFilter.RetainHandling",
            Valid with { TopicFilters = [MqttTopicFilter.Create("a/#") with { RetainHandling = RetainHandling.DoNotSendOnSubscribe }] }
        }
    };

    [Theory]
    [MemberData(nameof(Version5OnlyOptions))]
    public void Rejects_version_5_features_under_3_1_1(string feature, MqttClientRxOptions options)
    {
        var downgraded = options with { ProtocolVersion = MqttProtocolVersion.V311 };

        var exception = Assert.Throws<MqttOptionsException>(downgraded.Validate);

        Assert.Contains("MQTT 5.0", exception.Message, StringComparison.Ordinal);
        Assert.Contains(feature.Split('.')[^1], exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Version5OnlyOptions))]
    public void Accepts_version_5_features_under_5_0(string feature, MqttClientRxOptions options)
    {
        _ = feature;

        options.Validate();
    }

    [Fact]
    public void Accepts_a_plain_3_1_1_configuration()
    {
        var options = Valid with
        {
            ProtocolVersion = MqttProtocolVersion.V311,
            TopicFilters = [MqttTopicFilter.Create("sensors/#", QoSLevel.AtLeastOnce)],
            Will = MqttWillMessage.FromString("status", "offline")
        };

        options.Validate();
    }

    [Fact]
    public void The_constructor_validates_eagerly()
    {
        var options = Valid with { ProtocolVersion = MqttProtocolVersion.V311, ReceiveMaximum = 10 };

        // Surfacing this at construction beats surfacing it on whichever thread happens to subscribe.
        Assert.Throws<MqttOptionsException>(() => new MqttClientRx(options));
    }
}
