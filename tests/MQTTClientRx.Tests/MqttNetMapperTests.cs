using System.Buffers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MQTTClientRx.Internal;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using NetProtocolVersion = MQTTnet.Formatter.MqttProtocolVersion;

namespace MQTTClientRx.Tests;

public class MqttNetMapperTests
{
    private static MqttClientRxOptions Base => new() { Uri = new Uri("mqtt://broker.example:1883") };

    [Fact]
    public void Maps_the_protocol_version_and_keep_alive()
    {
        var options = MqttNetMapper.ToMqttNet(Base with
        {
            ProtocolVersion = MqttProtocolVersion.V311,
            KeepAlivePeriod = TimeSpan.FromSeconds(42)
        });

        Assert.Equal(NetProtocolVersion.V311, options.ProtocolVersion);
        Assert.Equal(TimeSpan.FromSeconds(42), options.KeepAlivePeriod);
    }

    [Fact]
    public void Generates_a_client_id_when_none_is_supplied()
    {
        var first = MqttNetMapper.ToMqttNet(Base);
        var second = MqttNetMapper.ToMqttNet(Base);

        Assert.NotEmpty(first.ClientId);
        Assert.NotEqual(first.ClientId, second.ClientId);
    }

    [Fact]
    public void Honours_an_explicit_client_id()
    {
        Assert.Equal("sensor-7", MqttNetMapper.ToMqttNet(Base with { ClientId = "sensor-7" }).ClientId);
    }

    [Fact]
    public void Maps_a_tcp_endpoint_with_the_uri_port()
    {
        var options = MqttNetMapper.ToMqttNet(Base with { Uri = new Uri("mqtt://broker.example:1884") });

        var channel = Assert.IsType<MqttClientTcpOptions>(options.ChannelOptions);
        Assert.Contains("1884", channel.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mqtt://broker.example", false, 1883)]
    [InlineData("mqtts://broker.example", false, 8883)]
    [InlineData("mqtt://broker.example", true, 8883)]
    public void Falls_back_to_the_conventional_port_when_the_uri_omits_one(string uri, bool useTls, int expectedPort)
    {
        var options = Base with
        {
            Uri = new Uri(uri),
            Tls = useTls ? MqttTlsOptions.Insecure : null
        };

        var mapped = MqttNetMapper.ToMqttNet(options);
        var channel = Assert.IsType<MqttClientTcpOptions>(mapped.ChannelOptions);

        Assert.Contains(expectedPort.ToString(), channel.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Maps_a_websocket_endpoint()
    {
        var options = MqttNetMapper.ToMqttNet(Base with
        {
            Uri = new Uri("ws://broker.example:8000/mqtt"),
            ConnectionType = ConnectionType.WebSocket
        });

        var channel = Assert.IsType<MqttClientWebSocketOptions>(options.ChannelOptions);
        Assert.Equal("ws://broker.example:8000/mqtt", channel.Uri);
    }

    [Fact]
    public void Maps_tls_settings()
    {
        var options = MqttNetMapper.ToMqttNet(Base with
        {
            Uri = new Uri("mqtts://broker.example:8883"),
            Tls = new MqttTlsOptions
            {
                AllowUntrustedCertificates = true,
                IgnoreCertificateChainErrors = true,
                TargetHost = "override.example"
            }
        });

        var channel = Assert.IsType<MqttClientTcpOptions>(options.ChannelOptions);

        Assert.True(channel.TlsOptions.UseTls);
        Assert.True(channel.TlsOptions.AllowUntrustedCertificates);
        Assert.True(channel.TlsOptions.IgnoreCertificateChainErrors);
        Assert.Equal("override.example", channel.TlsOptions.TargetHost);
    }

    [Fact]
    public void Revocation_checking_is_off_by_default_and_reaches_MQTTnet()
    {
        var mapped = MqttNetMapper.ToMqttNet(Base with
        {
            Uri = new Uri("mqtts://broker.example:8883"),
            Tls = new MqttTlsOptions()
        });

        var channel = Assert.IsType<MqttClientTcpOptions>(mapped.ChannelOptions);

        // MQTTnet defaults this to Online, which rejects valid certificates whenever the OCSP
        // responder is unreachable. We follow SslStream's own default instead.
        Assert.Equal(X509RevocationMode.NoCheck, channel.TlsOptions.RevocationMode);
    }

    [Fact]
    public void Revocation_checking_can_be_turned_back_on()
    {
        var mapped = MqttNetMapper.ToMqttNet(Base with
        {
            Uri = new Uri("mqtts://broker.example:8883"),
            Tls = new MqttTlsOptions { RevocationMode = X509RevocationMode.Online }
        });

        var channel = Assert.IsType<MqttClientTcpOptions>(mapped.ChannelOptions);
        Assert.Equal(X509RevocationMode.Online, channel.TlsOptions.RevocationMode);
    }

    [Fact]
    public void Defaults_the_tls_target_host_to_the_uri_host()
    {
        var options = MqttNetMapper.ToMqttNet(Base with
        {
            Uri = new Uri("mqtts://broker.example:8883"),
            Tls = new MqttTlsOptions()
        });

        var channel = Assert.IsType<MqttClientTcpOptions>(options.ChannelOptions);
        Assert.Equal("broker.example", channel.TlsOptions.TargetHost);
    }

    [Fact]
    public void Omits_tls_when_it_is_not_requested()
    {
        var options = MqttNetMapper.ToMqttNet(Base);
        var channel = Assert.IsType<MqttClientTcpOptions>(options.ChannelOptions);

        Assert.False(channel.TlsOptions.UseTls);
    }

    [Fact]
    public void Maps_version_5_connect_properties()
    {
        var options = MqttNetMapper.ToMqttNet(Base with
        {
            SessionExpiryInterval = TimeSpan.FromMinutes(10),
            ReceiveMaximum = 32,
            MaximumPacketSize = 65536,
            TopicAliasMaximum = 8,
            UserProperties = [new MqttUserProperty("tenant", "acme")]
        });

        Assert.Equal(600u, options.SessionExpiryInterval);
        Assert.Equal(32, options.ReceiveMaximum);
        Assert.Equal(65536u, options.MaximumPacketSize);
        Assert.Equal(8, options.TopicAliasMaximum);

        var property = Assert.Single(options.UserProperties);
        Assert.Equal("tenant", property.Name);
        Assert.Equal("acme", property.ReadValueAsString());
    }

    [Fact]
    public void Maps_the_will_message_including_version_5_fields()
    {
        var options = MqttNetMapper.ToMqttNet(Base with
        {
            Will = MqttWillMessage.FromString("status/1", "offline", QoSLevel.AtLeastOnce, retain: true) with
            {
                DelayInterval = TimeSpan.FromSeconds(30),
                ContentType = "text/plain",
                UserProperties = [new MqttUserProperty("reason", "lwt")]
            }
        });

        Assert.Equal("status/1", options.WillTopic);
        Assert.Equal("offline", Encoding.UTF8.GetString(options.WillPayload));
        Assert.Equal(MqttQualityOfServiceLevel.AtLeastOnce, options.WillQualityOfServiceLevel);
        Assert.True(options.WillRetain);
        Assert.Equal(30u, options.WillDelayInterval);
        Assert.Equal("text/plain", options.WillContentType);
        Assert.Equal("reason", Assert.Single(options.WillUserProperties).Name);
    }

    [Fact]
    public void Maps_topic_filter_subscription_options()
    {
        var filter = MqttNetMapper.ToMqttNet(new MqttTopicFilter
        {
            Topic = "sensors/#",
            QualityOfServiceLevel = QoSLevel.ExactlyOnce,
            NoLocal = true,
            RetainAsPublished = true,
            RetainHandling = RetainHandling.DoNotSendOnSubscribe
        });

        Assert.Equal("sensors/#", filter.Topic);
        Assert.Equal(MqttQualityOfServiceLevel.ExactlyOnce, filter.QualityOfServiceLevel);
        Assert.True(filter.NoLocal);
        Assert.True(filter.RetainAsPublished);
        Assert.Equal(MqttRetainHandling.DoNotSendOnSubscribe, filter.RetainHandling);
    }

    [Fact]
    public void Maps_an_outbound_message_including_version_5_properties()
    {
        var mapped = MqttNetMapper.ToMqttNet(new MqttMessage
        {
            Topic = "commands/reboot",
            Payload = Encoding.UTF8.GetBytes("now"),
            QualityOfServiceLevel = QoSLevel.AtLeastOnce,
            Retain = true,
            ContentType = "application/json",
            PayloadFormatIndicator = PayloadFormat.CharacterData,
            ResponseTopic = "replies/1",
            CorrelationData = new byte[] { 1, 2, 3 },
            MessageExpiryInterval = TimeSpan.FromSeconds(90),
            UserProperties = [new MqttUserProperty("origin", "test")]
        });

        Assert.Equal("commands/reboot", mapped.Topic);
        Assert.Equal("now", Encoding.UTF8.GetString(mapped.Payload.ToArray()));
        Assert.Equal(MqttQualityOfServiceLevel.AtLeastOnce, mapped.QualityOfServiceLevel);
        Assert.True(mapped.Retain);
        Assert.Equal("application/json", mapped.ContentType);
        Assert.Equal(MqttPayloadFormatIndicator.CharacterData, mapped.PayloadFormatIndicator);
        Assert.Equal("replies/1", mapped.ResponseTopic);
        Assert.Equal(new byte[] { 1, 2, 3 }, mapped.CorrelationData);
        Assert.Equal(90u, mapped.MessageExpiryInterval);
        Assert.Equal("origin", Assert.Single(mapped.UserProperties).Name);
    }

    [Fact]
    public void Subscribe_results_are_paired_back_to_the_filters_that_were_requested()
    {
        var requested = new[]
        {
            MqttTopicFilter.Create("granted/#", QoSLevel.AtLeastOnce),
            MqttTopicFilter.Create("refused/#", QoSLevel.ExactlyOnce)
        };

        var raw = new MqttClientSubscribeResult(
            packetIdentifier: 1,
            items:
            [
                new MqttClientSubscribeResultItem(
                    MqttNetMapper.ToMqttNet(requested[0]),
                    MqttClientSubscribeResultCode.GrantedQoS1),
                new MqttClientSubscribeResultItem(
                    MqttNetMapper.ToMqttNet(requested[1]),
                    MqttClientSubscribeResultCode.NotAuthorized)
            ],
            reasonString: "partial",
            userProperties: []);

        var result = MqttNetMapper.ToSubscribeResult(raw, requested);

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Items.Count);

        Assert.Equal("granted/#", result.Items[0].TopicFilter.Topic);
        Assert.True(result.Items[0].IsSuccess);
        Assert.Equal(QoSLevel.AtLeastOnce, result.Items[0].GrantedQoS);

        Assert.Equal("refused/#", result.Items[1].TopicFilter.Topic);
        Assert.False(result.Items[1].IsSuccess);
        Assert.Null(result.Items[1].GrantedQoS);

        Assert.Equal("refused/#", Assert.Single(result.Failures).TopicFilter.Topic);
        Assert.Equal("partial", result.ReasonString);
    }
}
