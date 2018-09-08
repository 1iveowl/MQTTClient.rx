using System;
using System.Text;
using System.Threading.Tasks;
using IMQTTClientRx.Model;
using MQTTClientRx.Service;
using Test.Client.Core.Model;

namespace Test.Client.Core
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await Start();

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }

        static async Task Start()
        {
            var mqttService = new MQTTService();

            var mqttClientOptions = new Options
            {
                Uri = new Uri("mqtt://test.mosquitto.org:1883"),
                //UseTls = true,
                //IgnoreCertificateChainErrors = true,
                //IgnoreCertificateRevocationErrors = true,
                //AllowUntrustedCertificates = true,

                //Uri = new Uri("ws://broker.mqttdashboard.com:8000/mqtt"),
                //Server = "broker.mqttdashboard.com",
                ////Port = 1883,
                //Port = 8000,
                //Url = "broker.mqttdashboard.com",
                //Path = "mqtt",
                ConnectionType = ConnectionType.Tcp,
                //ConnectionType = ConnectionType.WebSocket
            };

            var topic1 = new TopicFilter
            {
                QualityOfServiceLevel = QoSLevel.ExactlyOnce,
                //Topic = "PP/#"
                Topic = "/#"
            };

            var topic2 = new TopicFilter
            {
                QualityOfServiceLevel = QoSLevel.AtLeastOnce,
                Topic = "EFM/#"
                //Topic = "MQTTClientRx/Test"
            };

            var topic3 = new TopicFilter
            {
                QualityOfServiceLevel = QoSLevel.AtLeastOnce,
                Topic = "MQTTClientRx"
            };


            ITopicFilter[] topicFilters = 
            {

                topic1,
                topic2,
                topic3,
            };

            var MQTTService = mqttService.CreateObservableMQTTClient(mqttClientOptions, willMessage:null, topicFilters:topicFilters);

            var disposableMessage = MQTTService.observableMessage.Subscribe(
                msg =>
                {
                    if (msg.Topic.Contains("EFM"))
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
                    Console.WriteLine($"{ex?.Message} : inner {ex?.InnerException?.Message}");
                },
                () =>
                {
                    Console.WriteLine("Completed...");
                });

            await MQTTService.client.ConnectAsync();


            //await Task.Delay(TimeSpan.FromSeconds(2));

            //Console.ForegroundColor = ConsoleColor.Yellow;
            //Console.WriteLine($"Unsubscribe: {topic1.Topic}");
            //await MQTTService.client.UnsubscribeAsync(topic2);

            //Console.ForegroundColor = ConsoleColor.Blue;

            await Task.Delay(TimeSpan.FromSeconds(2));

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Unsubscribe: {topic1.Topic}");
            await MQTTService.client.UnsubscribeAsync(topic1);
            Console.ForegroundColor = ConsoleColor.Blue;

            await Task.Delay(TimeSpan.FromSeconds(2));

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Unsubscribe: {topic1.Topic}");
            await MQTTService.client.SubscribeAsync(topic3);
            Console.ForegroundColor = ConsoleColor.Blue;

            var newMessage = new MQTTMessage
            {
                Payload = Encoding.UTF8.GetBytes("Hello MQTT EO"),
                QualityOfServiceLevel = QoSLevel.AtLeastOnce,
                Retain = false,
                Topic = "MQTTClientRx"
            };

            await Task.Delay(TimeSpan.FromSeconds(2));

            await MQTTService.client.PublishAsync(newMessage);

            await Task.Delay(TimeSpan.FromSeconds(2));

            await MQTTService.client.DisconnectAsync();

            disposableMessage?.Dispose();
        }
    }
}