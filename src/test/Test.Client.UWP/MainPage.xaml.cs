using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using IMQTTClientRx.Model;
using MQTTClientRx.Service;
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
        public MainPage()
        {
            this.InitializeComponent();

        }

        private void Button_OnClick(object sender, RoutedEventArgs e)
        {
            var mqttService = new MQTTService();

            var mqttClientOptions = new Options
            {
                Server = "test.mosquitto.org",
                //Server = "broker.hivemq.com",
                Port = 1883,
                ConnectionType = ConnectionType.Tcp
            };

            var topic1 = new TopicFilter
            {
                QualityOfServiceLevel = QoSLevel.AtLeastOnce,
                //Topic = "PP/#"
                Topic = "#"
            };

            ITopicFilter[] topicFilters = {

                topic1,
            };

            var MQTTService = mqttService.CreateObservableMQTTService(mqttClientOptions, topicFilters);

            _disposable = MQTTService.observableMessage.ObserveOnDispatcher().Subscribe(
                msg =>
                {
                    textBlock.Text = Encoding.UTF8.GetString(msg.Payload);
                },
                ex =>
                {
                    textBlock.Text = "Exception";
                },
                () => {});
        }

        private void Stop_OnClick(object sender, RoutedEventArgs e)
        {
            _disposable.Dispose();
        }
    }
}
