using MQTTClientRx.Tests.Infrastructure;

namespace MQTTClientRx.Tests;

public class AsyncEnumerableTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Yields_messages_in_order()
    {
        await using var client = new FakeMqttClientRx();

        var received = new List<string>();
        var reading = Task.Run(async () =>
        {
            await foreach (var message in client.ReadAllAsync())
            {
                received.Add(message.GetPayloadAsString());
            }
        });

        await WaitForSubscriberAsync(client);

        client.Emit(MqttMessage.FromString("t", "one"));
        client.Emit(MqttMessage.FromString("t", "two"));
        client.Emit(MqttMessage.FromString("t", "three"));
        client.Complete();

        await reading.WaitAsync(Timeout);

        Assert.Equal(["one", "two", "three"], received);
    }

    [Fact]
    public async Task Enumerating_is_what_subscribes()
    {
        await using var client = new FakeMqttClientRx();

        var enumerable = client.ReadAllAsync();
        Assert.False(client.HasSubscribers);

        // The iterator body does not run until the first MoveNextAsync.
        await using var enumerator = enumerable.GetAsyncEnumerator();
        var moving = enumerator.MoveNextAsync();

        await WaitForSubscriberAsync(client);
        Assert.True(client.HasSubscribers);

        client.Emit(MqttMessage.FromString("t", "first"));

        Assert.True(await moving.AsTask().WaitAsync(Timeout));
        Assert.Equal("first", enumerator.Current.GetPayloadAsString());
    }

    [Fact]
    public async Task Abandoning_the_enumeration_unsubscribes()
    {
        await using var client = new FakeMqttClientRx();

        await using (var enumerator = client.ReadAllAsync().GetAsyncEnumerator())
        {
            var moving = enumerator.MoveNextAsync();
            await WaitForSubscriberAsync(client);

            client.Emit(MqttMessage.FromString("t", "first"));
            await moving.AsTask().WaitAsync(Timeout);
        }

        Assert.False(client.HasSubscribers);
    }

    [Fact]
    public async Task A_stream_error_surfaces_to_the_consumer()
    {
        await using var client = new FakeMqttClientRx();

        var reading = Task.Run(async () =>
        {
            await foreach (var _ in client.ReadAllAsync())
            {
            }
        });

        await WaitForSubscriberAsync(client);

        client.Fail(new MqttConnectFailedException(
            new MqttConnectResult { ResultCode = MqttConnectResultCode.NotAuthorized },
            new Uri("mqtt://broker:1883")));

        await Assert.ThrowsAsync<MqttConnectFailedException>(() => reading.WaitAsync(Timeout));
    }

    // -- Acknowledgement ---------------------------------------------------------------------

    [Fact]
    public async Task A_message_is_acknowledged_only_once_the_consumer_asks_for_the_next_one()
    {
        await using var client = new FakeMqttClientRx();
        var acknowledged = new List<string>();

        await using var enumerator = client.ReadAllAsync().GetAsyncEnumerator();

        var first = enumerator.MoveNextAsync();
        await WaitForSubscriberAsync(client);

        client.Emit(AcknowledgeableMessage.Create("t", "one", acknowledged));
        client.Emit(AcknowledgeableMessage.Create("t", "two", acknowledged));

        Assert.True(await first.AsTask().WaitAsync(Timeout));
        Assert.Equal("one", enumerator.Current.GetPayloadAsString());

        // The consumer is still "processing" message one, so nothing may be acknowledged yet.
        Assert.Empty(acknowledged);

        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout));
        Assert.Equal("two", enumerator.Current.GetPayloadAsString());

        // Asking for message two is the proof that message one was processed.
        Assert.Equal(["one"], acknowledged);
    }

    [Fact]
    public async Task A_loop_body_that_throws_leaves_its_message_unacknowledged()
    {
        await using var client = new FakeMqttClientRx();
        var acknowledged = new List<string>();

        var reading = Task.Run(async () =>
        {
            await foreach (var message in client.ReadAllAsync())
            {
                if (message.GetPayloadAsString() == "poison")
                {
                    throw new InvalidOperationException("processing failed");
                }
            }
        });

        await WaitForSubscriberAsync(client);

        client.Emit(AcknowledgeableMessage.Create("t", "good", acknowledged));
        client.Emit(AcknowledgeableMessage.Create("t", "poison", acknowledged));

        await Assert.ThrowsAsync<InvalidOperationException>(() => reading.WaitAsync(Timeout));

        // "good" was processed and acknowledged; "poison" was not, so the broker will redeliver it.
        Assert.Equal(["good"], acknowledged);
    }

    [Fact]
    public async Task Acknowledging_twice_is_a_no_op_rather_than_a_fault()
    {
        var acknowledged = new List<string>();
        var message = AcknowledgeableMessage.Create("t", "one", acknowledged);

        await message.AcknowledgeAsync();
        await message.AcknowledgeAsync();

        // MQTTnet throws on a second acknowledgement. A consumer acking in their own handler while
        // ReadAllAsync also acks on pull must not blow up the stream.
        Assert.Equal(["one"], acknowledged);
    }

    [Fact]
    public async Task Acknowledging_a_copy_does_not_re_acknowledge_the_original()
    {
        var acknowledged = new List<string>();
        var message = AcknowledgeableMessage.Create("t", "one", acknowledged);
        var copy = message with { Retain = true };

        await message.AcknowledgeAsync();
        await copy.AcknowledgeAsync();

        // `with` shares the acknowledgement handle, so once-only survives record copying.
        Assert.Equal(["one"], acknowledged);
    }

    [Fact]
    public async Task Acknowledgement_can_be_turned_off()
    {
        await using var client = new FakeMqttClientRx();
        var acknowledged = new List<string>();

        var options = MqttReadOptions.Default with { AcknowledgeWhenConsumed = false };

        await using var enumerator = client.ReadAllAsync(options).GetAsyncEnumerator();

        var first = enumerator.MoveNextAsync();
        await WaitForSubscriberAsync(client);

        client.Emit(AcknowledgeableMessage.Create("t", "one", acknowledged));
        client.Emit(AcknowledgeableMessage.Create("t", "two", acknowledged));

        await first.AsTask().WaitAsync(Timeout);
        await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout);

        Assert.Empty(acknowledged);
    }

    // -- Overflow ----------------------------------------------------------------------------

    [Fact]
    public async Task An_overrun_buffer_fails_loudly_by_default()
    {
        await using var client = new FakeMqttClientRx();

        var options = MqttReadOptions.Default with { BufferCapacity = 2 };
        await using var enumerator = client.ReadAllAsync(options).GetAsyncEnumerator();

        var first = enumerator.MoveNextAsync();
        await WaitForSubscriberAsync(client);

        for (var i = 0; i < 10; i++)
        {
            client.Emit(MqttMessage.FromString("t", $"m{i}"));
        }

        await first.AsTask().WaitAsync(Timeout);

        var overflow = await Assert.ThrowsAsync<MqttBufferOverflowException>(async () =>
        {
            while (await enumerator.MoveNextAsync())
            {
            }
        });

        Assert.Equal(2, overflow.BufferCapacity);
    }

    [Fact]
    public async Task DropOldest_keeps_the_freshest_messages()
    {
        await using var client = new FakeMqttClientRx();

        var options = MqttReadOptions.Default with
        {
            BufferCapacity = 2,
            OverflowStrategy = BufferOverflowStrategy.DropOldest
        };

        await using var enumerator = client.ReadAllAsync(options).GetAsyncEnumerator();

        var first = enumerator.MoveNextAsync();
        await WaitForSubscriberAsync(client);

        // The reader has not started draining, so all five contend for two slots.
        for (var i = 1; i <= 5; i++)
        {
            client.Emit(MqttMessage.FromString("t", $"m{i}"));
        }

        client.Complete();

        var drained = new List<string> { (await first.AsTask().WaitAsync(Timeout)) ? enumerator.Current.GetPayloadAsString() : "?" };

        while (await enumerator.MoveNextAsync())
        {
            drained.Add(enumerator.Current.GetPayloadAsString());
        }

        Assert.Equal(["m4", "m5"], drained);
    }

    [Fact]
    public async Task DropNewest_keeps_the_oldest_messages()
    {
        await using var client = new FakeMqttClientRx();

        var options = MqttReadOptions.Default with
        {
            BufferCapacity = 2,
            OverflowStrategy = BufferOverflowStrategy.DropNewest
        };

        await using var enumerator = client.ReadAllAsync(options).GetAsyncEnumerator();

        var first = enumerator.MoveNextAsync();
        await WaitForSubscriberAsync(client);

        for (var i = 1; i <= 5; i++)
        {
            client.Emit(MqttMessage.FromString("t", $"m{i}"));
        }

        client.Complete();

        var drained = new List<string> { (await first.AsTask().WaitAsync(Timeout)) ? enumerator.Current.GetPayloadAsString() : "?" };

        while (await enumerator.MoveNextAsync())
        {
            drained.Add(enumerator.Current.GetPayloadAsString());
        }

        Assert.Equal(["m1", "m2"], drained);
    }

    [Fact]
    public void A_buffer_capacity_below_one_is_rejected()
    {
        var options = MqttReadOptions.Default with { BufferCapacity = 0 };

        Assert.Throws<MqttOptionsException>(() => options.Validate());
    }

    [Fact]
    public async Task Cancellation_ends_the_enumeration()
    {
        await using var client = new FakeMqttClientRx();
        using var cancellation = new CancellationTokenSource();

        var reading = Task.Run(async () =>
        {
            await foreach (var _ in client.ReadAllAsync(cancellation.Token))
            {
            }
        });

        await WaitForSubscriberAsync(client);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading.WaitAsync(Timeout));
        Assert.False(client.HasSubscribers);
    }

    private static Task WaitForSubscriberAsync(FakeMqttClientRx client) =>
        Poll.UntilAsync(() => client.HasSubscribers, Timeout, "the enumeration to subscribe");
}
