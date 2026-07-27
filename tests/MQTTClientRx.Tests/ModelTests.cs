using System.Text;
using MQTTClientRx.Internal;

namespace MQTTClientRx.Tests;

public class MqttMessageTests
{
    [Fact]
    public void FromString_round_trips_through_the_payload()
    {
        var message = MqttMessage.FromString("sensors/1", "23.4 °C");

        Assert.Equal("sensors/1", message.Topic);
        Assert.Equal("23.4 °C", message.GetPayloadAsString());
    }

    [Fact]
    public void A_non_default_encoding_is_honoured_both_ways()
    {
        var latin1 = Encoding.Latin1;
        var message = MqttMessage.FromString("t", "Ærø", encoding: latin1);

        Assert.Equal("Ærø", message.GetPayloadAsString(latin1));
        Assert.NotEqual("Ærø", message.GetPayloadAsString());
    }

    [Fact]
    public void An_empty_payload_is_valid()
    {
        var message = new MqttMessage { Topic = "t" };

        Assert.Equal(string.Empty, message.GetPayloadAsString());
        Assert.Empty(message.GetPayloadAsArray());
    }

    [Fact]
    public void Acknowledging_a_locally_constructed_message_does_nothing()
    {
        var message = MqttMessage.FromString("t", "payload");

        Assert.True(message.AcknowledgeAsync().IsCompletedSuccessfully);
    }

    [Fact]
    public void Version_5_properties_default_to_absent()
    {
        var message = new MqttMessage { Topic = "t" };

        Assert.Null(message.ContentType);
        Assert.Null(message.PayloadFormatIndicator);
        Assert.Null(message.ResponseTopic);
        Assert.Null(message.MessageExpiryInterval);
        Assert.Null(message.TopicAlias);
        Assert.Empty(message.UserProperties);
        Assert.Empty(message.SubscriptionIdentifiers);
        Assert.True(message.CorrelationData.IsEmpty);
    }

    [Fact]
    public void ToString_summarises_rather_than_dumping_the_payload()
    {
        var message = MqttMessage.FromString("sensors/1", "0123456789", QoSLevel.AtLeastOnce, retain: true);

        Assert.Equal("sensors/1 [AtLeastOnce, retain] 10 bytes", message.ToString());
    }
}

public class MqttTopicMatcherTests
{
    [Theory]
    [InlineData("sensors/#", "sensors/1/temperature", true)]
    [InlineData("sensors/#", "sensors", true)]
    [InlineData("sensors/+/temperature", "sensors/1/temperature", true)]
    [InlineData("sensors/+/temperature", "sensors/1/2/temperature", false)]
    [InlineData("sensors/1", "sensors/2", false)]
    [InlineData("#", "any/topic", true)]
    [InlineData("#", "$SYS/broker/uptime", false)]
    public void Matches_follows_MQTT_wildcard_rules(string filter, string topic, bool expected)
    {
        Assert.Equal(expected, MqttTopicMatcher.Matches(filter, topic));
    }

    [Theory]
    [InlineData("$share/group/sensors/#", "sensors/#")]
    [InlineData("$share/g/a", "a")]
    [InlineData("sensors/#", "sensors/#")]
    [InlineData("$share/nogroupseparator", "$share/nogroupseparator")]
    public void Shared_subscription_prefixes_are_stripped(string filter, string expected)
    {
        Assert.Equal(expected, MqttTopicMatcher.StripSharedSubscriptionPrefix(filter));
    }

    [Fact]
    public void A_shared_subscription_filter_matches_the_delivered_topic()
    {
        // The broker strips the $share prefix before delivery, so the topic never carries it.
        Assert.True(MqttTopicMatcher.Matches("$share/workers/jobs/+", "jobs/build"));
    }
}

public class SubscriptionRegistryTests
{
    [Fact]
    public void Starts_from_the_configured_filters()
    {
        var registry = new SubscriptionRegistry([MqttTopicFilter.Create("a"), MqttTopicFilter.Create("b")]);

        Assert.Equal(["a", "b"], registry.Snapshot().Select(filter => filter.Topic).Order());
    }

    [Fact]
    public void Re_adding_a_topic_replaces_its_options()
    {
        var registry = new SubscriptionRegistry([MqttTopicFilter.Create("a", QoSLevel.AtMostOnce)]);

        registry.Add([MqttTopicFilter.Create("a", QoSLevel.ExactlyOnce)]);

        var filter = Assert.Single(registry.Snapshot());
        Assert.Equal(QoSLevel.ExactlyOnce, filter.QualityOfServiceLevel);
    }

    [Fact]
    public void Removing_a_topic_stops_it_being_replayed_on_reconnect()
    {
        var registry = new SubscriptionRegistry([MqttTopicFilter.Create("a"), MqttTopicFilter.Create("b")]);

        registry.Remove(["a"]);

        Assert.Equal("b", Assert.Single(registry.Snapshot()).Topic);
    }

    [Fact]
    public void Removing_an_unknown_topic_is_harmless()
    {
        var registry = new SubscriptionRegistry([MqttTopicFilter.Create("a")]);

        registry.Remove(["nope"]);

        Assert.Single(registry.Snapshot());
    }
}
