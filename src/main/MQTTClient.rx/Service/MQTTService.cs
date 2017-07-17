using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using IMQTTClientRx.Model;
using IMQTTClientRx.Service;
using MQTTClientRx.Model;
using MQTTnet;
using MQTTnet.Core;
using MQTTnet.Core.Client;
using MQTTnet.Core.Protocol;
// ReSharper disable PossibleMultipleEnumeration

namespace MQTTClientRx.Service
{
    public class MQTTService : IMQTTService
    {
        public (IObservable<IMQTTMessage> observableMessage, IMQTTClient client) 
            CreateObservableMQTTServiceAsync(
                IClientOptions options, 
                IEnumerable<ITopicFilter> topicFilters = null,
                IWillMessage willMessage = null)
        {
            var client = new MqttClientFactory().CreateMqttClient(UnwrapOptions(options));

            var wrappedClient = new MQTTClient(client);

            var observable = Observable.Create<IMQTTMessage>(
                async obs =>
                {
                    var disposableMessage = Observable.FromEventPattern<MqttApplicationMessageReceivedEventArgs>(
                            h => client.ApplicationMessageReceived += h,
                            h => client.ApplicationMessageReceived -= h)
                        .Subscribe(
                            msgEvent =>
                            {
                                var message = new MQTTMessage
                                {
                                    Payload = msgEvent.EventArgs.ApplicationMessage.Payload,
                                    Retain = msgEvent.EventArgs.ApplicationMessage.Retain,
                                    QualityOfServiceLevel =
                                        (QoSLevel) msgEvent.EventArgs.ApplicationMessage.QualityOfServiceLevel,
                                    Topic = msgEvent.EventArgs.ApplicationMessage.Topic
                                };

                                obs.OnNext(message);
                            },
                            obs.OnError,
                            obs.OnCompleted);

                    var disposableConnect = Observable.FromEventPattern(
                            h => client.Connected += h,
                            h => client.Connected -= h)
                        .Subscribe(
                            async connectEvent =>
                            {

                                Debug.WriteLine("Connected");
                                if (topicFilters?.Any() ?? false)
                                {
                                    await wrappedClient.SubscribeAsync(topicFilters);
                                }
                            },
                            obs.OnError,
                            obs.OnCompleted);
                    
                    var disposableDisconnect = Observable.FromEventPattern(
                            h => client.Disconnected += h,
                            h => client.Disconnected -= h)
                        .Subscribe(
                            disconnectEvent =>
                            {
                                Debug.WriteLine("Disconnected");
                                obs.OnCompleted();
                            },
                            obs.OnError,
                            obs.OnCompleted);

                    await client.ConnectAsync(WrapWillMessage(willMessage));

                    return new CompositeDisposable(
                        CleanUp(client), 
                        disposableMessage, 
                        disposableConnect,
                        disposableDisconnect);
                }).Publish().RefCount();
            
            return (observable, wrappedClient);
        }

        private IDisposable CleanUp(MqttClient client)
        {
            return Disposable.Create(
                async () =>
                {
                    if (client.IsConnected)
                    {
                        var disconnectTask = client.DisconnectAsync();
                        var timeOutTask = Task.Delay(TimeSpan.FromSeconds(5));

                        var result = await Task.WhenAny(disconnectTask, timeOutTask).ConfigureAwait(false);
                        
                        Debug.WriteLine($"Disconnected Successfully: {result == disconnectTask}");
                    }
                });
        }
        
        private static MqttClientOptions UnwrapOptions(IClientOptions wrappedOptions)
        {
            return new MqttClientOptions
            {
                Server = wrappedOptions.Server,
                CleanSession = wrappedOptions.CleanSession,
                ClientId = wrappedOptions.ClientId ?? Guid.NewGuid().ToString().Replace("-", string.Empty),
                Port = wrappedOptions.Port,
                TlsOptions =
                {
                    UseTls = wrappedOptions.UseTls,
                    CheckCertificateRevocation = wrappedOptions.CheckCertificateRevocation,
                    Certificates = wrappedOptions.Certificates?.ToList() ?? new List<byte[]>()
                },
                UserName = wrappedOptions.UserName,
                Password = wrappedOptions.Password,
                KeepAlivePeriod = wrappedOptions.KeepAlivePeriod == default(TimeSpan) ? TimeSpan.FromSeconds(5) : wrappedOptions.KeepAlivePeriod,
                DefaultCommunicationTimeout = wrappedOptions.DefaultCommunicationTimeout == default(TimeSpan) ? TimeSpan.FromSeconds(10) : wrappedOptions.DefaultCommunicationTimeout
            };
        }

        private MqttApplicationMessage WrapWillMessage(IWillMessage message)
        {
            if (message != null)
            {
                return new MqttApplicationMessage(
                    message.Topic, 
                    message.Payload,
                    (MqttQualityOfServiceLevel)message.QualityOfServiceLevel, 
                    message.Retain);
            }
            return null;
        }
    }
}
