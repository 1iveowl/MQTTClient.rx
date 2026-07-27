using MQTTClientRx.Internal;
using MQTTnet;
using MQTTnet.Protocol;
using NetProtocolVersion = MQTTnet.Formatter.MqttProtocolVersion;

namespace MQTTClientRx.Tests;

/// <summary>
/// The mapper casts between this library's enums and MQTTnet's on the assumption that both mirror
/// the MQTT specification's numeric values. That assumption is cheap to hold and expensive to get
/// wrong - a silent renumbering upstream would turn every reason code into a different reason code -
/// so it is asserted rather than trusted.
/// </summary>
public class EnumParityTests
{
    [Fact]
    public void QoS_levels_map_both_ways()
    {
        foreach (var value in Enum.GetValues<QoSLevel>())
        {
            var roundTripped = MqttNetMapper.ToQoSLevel(MqttNetMapper.ToMqttNet(value));
            Assert.Equal(value, roundTripped);
        }
    }

    [Fact]
    public void Connect_result_codes_have_identical_values()
    {
        AssertParity<MqttConnectResultCode, MqttClientConnectResultCode>();
    }

    [Fact]
    public void Subscribe_result_codes_have_identical_values()
    {
        AssertParity<MqttSubscribeResultCode, MqttClientSubscribeResultCode>();
    }

    [Fact]
    public void Unsubscribe_result_codes_have_identical_values()
    {
        AssertParity<MqttUnsubscribeResultCode, MqttClientUnsubscribeResultCode>();
    }

    [Fact]
    public void Publish_reason_codes_have_identical_values()
    {
        AssertParity<MqttPublishReasonCode, MqttClientPublishReasonCode>();
    }

    [Fact]
    public void Disconnect_reason_codes_have_identical_values()
    {
        AssertParity<MqttDisconnectReasonCode, MqttClientDisconnectReason>();
    }

    [Fact]
    public void Retain_handling_values_have_identical_values()
    {
        AssertParity<RetainHandling, MqttRetainHandling>();
    }

    [Fact]
    public void Payload_format_values_have_identical_values()
    {
        AssertParity<PayloadFormat, MqttPayloadFormatIndicator>();
    }

    [Fact]
    public void Protocol_versions_map_to_the_wire_protocol_level()
    {
        Assert.Equal(NetProtocolVersion.V310, MqttNetMapper.ToMqttNet(MqttProtocolVersion.V310));
        Assert.Equal(NetProtocolVersion.V311, MqttNetMapper.ToMqttNet(MqttProtocolVersion.V311));
        Assert.Equal(NetProtocolVersion.V500, MqttNetMapper.ToMqttNet(MqttProtocolVersion.V500));

        // MQTTnet has an Unknown member we deliberately do not expose; everything else must line up.
        Assert.Equal(3, (int)MqttProtocolVersion.V310);
        Assert.Equal(4, (int)MqttProtocolVersion.V311);
        Assert.Equal(5, (int)MqttProtocolVersion.V500);
    }

    /// <summary>
    /// Every member we declare must exist upstream with the same numeric value. The reverse is not
    /// required: MQTTnet may add members we have not mirrored yet.
    /// </summary>
    private static void AssertParity<TOurs, TTheirs>()
        where TOurs : struct, Enum
        where TTheirs : struct, Enum
    {
        var theirs = Enum.GetValues<TTheirs>()
            .ToDictionary(value => value.ToString(), value => Convert.ToInt32(value));

        foreach (var ours in Enum.GetValues<TOurs>())
        {
            var name = ours.ToString();

            Assert.True(
                theirs.TryGetValue(name, out var theirValue),
                $"{typeof(TOurs).Name}.{name} has no counterpart in {typeof(TTheirs).Name}.");

            Assert.Equal(theirValue, Convert.ToInt32(ours));
        }
    }
}
