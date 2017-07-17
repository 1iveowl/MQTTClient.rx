# MQTT Client Rx

[![NuGet Badge](https://buildstats.info/nuget/MQTTClientRx)](https://www.nuget.org/packages/MQTTClientRx)

[![System.Reactive](http://img.shields.io/badge/Rx-v3.1.1-ff69b4.svg)](http://reactivex.io/) 

*Please star this project if you find it useful. Thank you!*

## Credits
This project is based on [MQTTnet](https://github.com/chkr1011/MQTTnet) by Christian Kratky. Without his work, this library would not be. All this library really is, is a Rx wrapper around MQTTnet. 

## Why this library
[MQTT](http://mqtt.org/) and [Reactive Extensions](http://reactivex.io/) (aka. ReactiveX or just Rx) are a perfect for each other! Rx is an API for asynchronous programming
with observable streams, while MQTT is a protocol that produces asynchronous streams.

## How to use
Using the library is easy. Rx makes is easy to write code in a declarative manager which in IMHO is more elegant that the alternatives. 


### House keeping
First some house keeping. The library is very flexible and is created using Interface Driven Development. 

To use this library you need to start by creating four classes that each implement these four interfaces: 
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

	// TlsOptions
    public bool UseTls { get; internal set; }
    public bool CheckCertificateRevocation { get; internal set; }
    public IEnumerable<byte[]> Certificates { get; internal set; }
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
Implemting these classes is easily done, and by not using concrete classes in the library you have the flexibility to how to use and implement these classes. Also you can encapsulate as you see fit. It might seem a bit counter intiitive at first, but there is added benefit to the flexibility this provides as your project grow.

### Observing MQTT
Now you are ready to start using this library.

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
    Topic = "PP/#" // You might want to try something else if there is nothing is published to this topic in the test server at the time of testing this.
};

var topic2 = new TopicFilter
{
    QualityOfServiceLevel = QoSLevel.AtMostOnce,
    Topic = "EFM/#" // You might want to try something else if there is nothing is published to this topic in the test server at the time of testing this.
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
        // If an exception happens they can be manager here
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
MQTTService.client.SubscribeAsync(new[] { "My/NewFilter" });
```
You can subscribe to Topic filters after creating the MQTTService.
#### Unsubscribing to Topic Filteres
Unsubscribing to other Topic Filters is easy:
```csharp
MQTTService.client.UnsubscribeAsync(new[] { "My/NewFilter" });
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
You can publish messages after creating the MQTTService.
