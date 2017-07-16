# MQTT Client Rx

[![NuGet Badge](https://buildstats.info/nuget/SocketLite.PCL)](https://www.nuget.org/packages/SocketLite.PCL)

[![NuGet](https://img.shields.io/badge/nuget-2.0.30_(Profile_111)-yellow.svg)](https://www.nuget.org/packages/SocketLite.PCL/2.0.20)

*Please star this project if you find it useful. Thank you!*

## Credits
This project is based on [MQTTnet](https://github.com/chkr1011/MQTTnet) by Christian Kratky. Without his work, this library would not be. All this library really is, is a Rx wrapper around MQTTnet. 

## Why this library
[MQTT](http://mqtt.org/) and [Reactive Extensions](http://reactivex.io/) (aka. ReactiveX or just Rx) are perfect for each other! 

Rx is an API for asynchronous programming
with observable streams. MQTT is a protocol that produces asynchronous streams.

That is why I created this library!

## How to use
Using the library is reallly easy. Rx makes is easy to write code in a declarative manager that is IMHO more elegant to use than Events. But first some house keeping. 

### House keeping
The library is very flexible and is created uing Interface Driven Development. To use this library you need to create a set of classes that each implement these four interfaces: 
- `MQTTMessage`
- `IClientOptions`
- `ITopicFilter`
- `IWillMessage`.

Like this:
```csharp
internal class MQTTMessage : IMQTTMessage
{
    public string Topic { get; internal set; }
    public byte[] Payload { get; internal set; }
    public QoSLevel QualityOfServiceLevel { get; internal set; }
    public bool Retain { get; internal set; }
}
```

```csharp
internal class Options : IClientOptions
{
    public string Server { get; internal set; }
    public int? Port { get; internal set; }
    public string UserName { get; internal set; }
    public string Password { get; internal set; }
    public string ClientId { get; internal set; }
    public bool CleanSession { get; internal set; }
    public TimeSpan KeepAlivePeriod { get; internal set; }
    public TimeSpan DefaultCommunicationTimeout { get; internal set; }
}
```

```csharp
internal class TopicFilter : ITopicFilter
{
    public string Topic { get; internal set; }
    public QoSLevel QualityOfServiceLevel { get; internal set; }
}
```

```csharp
internal class WillMessage : IWillMessage
{
    public string Topic { get; internal set; }
    public byte[] Payload { get; internal set; }
    public QoSLevel QualityOfServiceLevel { get; internal set; }
    public bool Retain { get; internal set; }
}
```
Implemting these classes is easily done, and by not using concrete classes in the library.

### Observing MQTT
Here is an example of using the MQTT Client Rx:
#### Using
```csharp
using IMQTTClientRx.Model;
using MQTTClientRx.Service;
```

```csharp
var mqttService = new MQTTService();

var mqttClientOptions = new Options
{
    Server = "test.mosquitto.org", //Test server
    Port = 1883
};

var topic1 = new TopicFilter
{
    QualityOfServiceLevel = QoSLevel.AtMostOnce,
    Topic = "PP/#" // You might want to try something else if there is nothing published to this topiv
};

var topic2 = new TopicFilter
{
    QualityOfServiceLevel = QoSLevel.AtMostOnce,
    Topic = "EFM/#" // You might want to try something else if there is nothing published to this topiv
};

ITopicFilter[] topicFilters = {

    topic1,
    topic2
};

var MQTTService = mqttService.CreateObservableMQTTServiceAsync(mqttClientOptions, topicFilters); //The topic filters are optional

_disposable = MQTTService.observableMessage.Subscribe(
    msg =>
    {
        // Just some colour coding to make it easier to see what topic is what
        if (msg.Topic.Contains("PP"))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Blue;
        }
        
        Console.WriteLine($"{Encoding.UTF8.GetString(msg.Payload)}, " +
                            $"{msg.QualityOfServiceLevel.ToString()}, " +
                            $"Retain: {msg.Retain}, " +
                            $"Topic: {msg.Topic}");
    },
    ex =>
    {
        // If an exception happens add code here
        Console.WriteLine($"{ex.Message} : inner {ex.InnerException.Message}");
    },
    () =>
    {
        // When the observable completes this will run
        // Example: The observable will complete if the connection is ended by the serter
        Console.WriteLine("Completed...");
    });;
```
#### Subscribing to more Topic Filters

Subscribing to other Topic Filters is easy:
```csharp
MQTTService.client.SubscribeAsync(new[] { "My/Filter" });
```
You can subscribe to Topic filters after creating the MQTTService.
#### Unsubscribing to Topic Filteres
Unsubscribing to other Topic Filters is easy:
```csharp
MQTTService.client.UnsubscribeAsync(new[] { "My/OtherFilter" });
```
You can unsubscribe to Topic filters after creating the MQTTService.
#### Publish
Publishing is easy too:
```csharp
var newMessage = new MQTTMessage
{
    Payload = Encoding.UTF8.GetBytes("Hello MQTT"),
    QualityOfServiceLevel = QoSLevel.AtMostOnce,
    Retain = false,
    Topic = "MQTTClientRx/Test"
};

await MQTTService.client.PublishAsync(newMessage);
```
You can publishmessages after creating the MQTTService.
