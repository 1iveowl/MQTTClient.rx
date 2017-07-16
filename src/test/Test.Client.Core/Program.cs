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
        private static IDisposable _disposable;
        static void Main(string[] args)
        {
            Start();

            Console.ReadLine();
            _disposable.Dispose();
            Task.Delay(TimeSpan.FromSeconds(1));
            Console.WriteLine("Press any key to exit...");
        }

        static async void Start()
        {
            var mqttService = new MQTTService();

            var mqttClientOptions = new Options
            {
                Server = "test.mosquitto.org",
                Port = 1883
            };

            var topic1 = new TopicFilter
            {
                QualityOfServiceLevel = QoSLevel.ExactlyOnce,
                Topic = "PP/#"
            };

            var topic2 = new TopicFilter
            {
                QualityOfServiceLevel = QoSLevel.ExactlyOnce,
                Topic = "EFM/#"
            };

            ITopicFilter[] topicFilters = {

                topic1,
                topic2
            };

            var MQTTService = mqttService.CreateObservableMQTTServiceAsync(mqttClientOptions, topicFilters);
            
            _disposable = MQTTService.observableMessage.Subscribe(
                msg =>
                {
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
                    Console.WriteLine($"{ex.Message} : inner {ex.InnerException.Message}");
                },
                () =>
                {
                    Console.WriteLine("Completed...");
                });



            await Task.Delay(TimeSpan.FromSeconds(2));

            var newMessage = new MQTTMessage
            {
                Payload = Encoding.UTF8.GetBytes("Hello MQTT"),
                QualityOfServiceLevel = QoSLevel.AtMostOnce,
                Retain = false,
                Topic = "MQTTClientRx/Test"
            };

            await MQTTService.client.PublishAsync(newMessage);

            await MQTTService.client.UnsubscribeAsync(new [] {topic1});

            //await Task.Delay(TimeSpan.FromSeconds(2));
            //await MQTTService.client.UnsubscribeAsync(new[] {topic2});
            //await Task.Delay(TimeSpan.FromSeconds(2));



            

            //await MQTTService.client.SubscribeAsync(new[] { topic2 });

            //await Task.Delay(TimeSpan.FromSeconds(2));
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