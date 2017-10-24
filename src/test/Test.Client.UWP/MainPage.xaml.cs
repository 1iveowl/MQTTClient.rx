using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using IMQTTClientRx.Model;
using IMQTTClientRx.Service;
using MQTTClientRx.Service;
using MQTTnet.Core.Diagnostics;
using Test.Client.UWP.Model;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Test.Client.UWP
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private IDisposable _disposable;
        private IMQTTService _mqttService;
        public MainPage()
        {
            this.InitializeComponent();
            
            MqttNetTrace.TraceMessagePublished += async (s, e) =>
            {

                await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var textItem = new TextBlock { Text = $">> [{DateTime.Now:hh:mm:ss.fff tt}] [{e.ThreadId}] [{e.Source}] [{e.Level}]: {e.Message}", TextWrapping = TextWrapping.Wrap, FontSize = 12};
                    panel.Children.Add(textItem);

                    if (e.Exception != null)
                    {
                        var textException = new TextBlock { Text = $"   {e.Exception}" };
                        panel.Children.Add(textException);
                    }
                });
                //var textItem = new TextBlock { Text = $">> [{e.ThreadId}] [{e.Source}] [{e.Level}]: {e.Message}" };
                //panel.Children.Add(textItem);

                //if (e.Exception != null)
                //{
                //    var textException = new TextBlock { Text = $"   {e.Exception}" };
                //    panel.Children.Add(textException);
                //}
            };

        }



        private void Button_OnClick(object sender, RoutedEventArgs e)
        {
            _mqttService = new MQTTService();

            var mqttClientOptions = new Options
            {
                Server = "test.mosquitto.org",
                //Server = "192.168.0.41",
                Port = 1883,
                ConnectionType = ConnectionType.Tcp,
                CleanSession = true,
                ClientId = Guid.NewGuid().ToString().Replace("-", string.Empty),
                UseTls = false
            };

            var topic1 = new TopicFilter
            {
                QualityOfServiceLevel = QoSLevel.AtLeastOnce,
                //Topic = "PP/#"
                Topic = "/#"
            };

            ITopicFilter[] topicFilters = {

                topic1,
            };

            var MQTTService = _mqttService.CreateObservableMQTTServiceAsync(mqttClientOptions, topicFilters).Result;

            _disposable = MQTTService
                .observableMessage
                .SubscribeOnDispatcher()
                //.Throttle(TimeSpan.FromMilliseconds(100), Scheduler.Default)
                .ObserveOnDispatcher().Subscribe(
                    msg =>
                    {
                        textBlock.Text = Encoding.UTF8.GetString(msg.Payload);
                    },
                    ex =>
                    {
                        textBlock.Text = "Exception";
                    },
                    () => { });

            //var MQTTService = await mqttService.CreateObservableMQTTServiceAsync(mqttClientOptions, topicFilters);


        }

        private void Stop_OnClick(object sender, RoutedEventArgs e)
        {
            _disposable.Dispose();
        }

        private void Disconnect_OnClick(object sender, RoutedEventArgs e)
        {
            _mqttService.DisconnectAsync();
        }
    }
}
