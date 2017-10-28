using System;
using System.Text;
using System.Threading.Tasks;
using IMQTTClientRx.Model;
using MQTTClientRx.Service;
using Test.Client.Core.Model;
using MQTTClientRx.Extension;
using MQTTClientRx.Model;

namespace Test.Client.Core
{
    class Program
    {
        private static IDisposable _disp1;
        private static IDisposable _disp2;
        static async Task Main(string[] args)
        {
            await Start();

            Console.ReadLine();
            //await _disp1.DisposeAsync();
            //await _disp2.DisposeAsync();
            await Task.Delay(TimeSpan.FromSeconds(1));
            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }

        static async Task Start()
        {
            var mqttService = new MQTTService();

            var mqttClientOptions = new Options
            {
                //Server = "test.mosquitto.org",
                //Server = "broker.hivemq.com",
                Server = "broker.mqttdashboard.com",
                //Port = 1883,
                Port = 8000,
                Url = "broker.mqttdashboard.com",
                Path = "mqtt",
                //ConnectionType = ConnectionType.Tcp,
                ConnectionType = ConnectionType.WebSocket
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
                //Topic = "EFM/#"
                Topic = "MQTTClientRx/Test"
            };

            ITopicFilter[] topicFilters = {

                topic1,
                //topic2
            };

            var MQTTService = mqttService.CreateObservableMQTTService(mqttClientOptions, topicFilters);

            _disp1 = MQTTService.observableMessage.Subscribe(
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
                    Console.WriteLine($"{ex.Message} : inner {ex.InnerException.Message}");
                },
                () =>
                {
                    Console.WriteLine("Completed...");
                });

            await Task.Delay(TimeSpan.FromSeconds(5));

            var newMessage = new MQTTMessage
            {
                Payload = Encoding.UTF8.GetBytes("Hello MQTT EO"),
                QualityOfServiceLevel = QoSLevel.ExactlyOnce,
                Retain = false,
                Topic = "MQTTClientRx/Test"
            };

            await MQTTService.client.PublishAsync(newMessage);

            _disp2 = MQTTService.observableMessage.Subscribe(
                msg =>
                {
                    if (msg.Topic.Contains("EFM"))
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                    }

                    Console.WriteLine($"{Encoding.UTF8.GetString(msg.Payload)}, " +
                                      $"{msg.QualityOfServiceLevel.ToString()}, " +
                                      $"Retain: {msg.Retain}, " +
                                      $"Topic: {msg.Topic}");
                },
                ex =>
                {
                    Console.WriteLine($"{ex.Message} : inner {ex.InnerException.Message}");
                },
                () =>
                {
                    Console.WriteLine("Completed...");
                });

            await Task.Delay(TimeSpan.FromSeconds(2));

            await MQTTService.client.UnsubscribeAsync(new[] {topic2});

            await Task.Delay(TimeSpan.FromSeconds(5));

            //await MQTTService.client.UnsubscribeAsync(new [] {topic1});

            //await Task.Delay(TimeSpan.FromSeconds(2));



            //await MQTTService.client.SubscribeAsync(new[] { topic2 });


            await Task.Delay(TimeSpan.FromSeconds(2));
            
            _disp2.Dispose();

            await Task.Delay(TimeSpan.FromSeconds(2));

            _disp1.Dispose();

            await Task.Delay(TimeSpan.FromSeconds(30));
            //var topic2a = new TopicFilter
            //{
            //    QualityOfServiceLevel = QoSLevel.ExactlyOnce,
            //    Topic = "EFM/#"
            //};
            //Console.ForegroundColor = ConsoleColor.DarkBlue;
            //await MQTTService.client.UnsubscribeAsync(new[] { topic2 });
            //await MQTTService.client.SubscribeAsync(new[] { topic2a });

        }
    }
}