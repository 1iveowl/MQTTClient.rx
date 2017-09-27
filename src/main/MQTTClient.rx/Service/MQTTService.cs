using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using IMQTTClientRx.Model;
using IMQTTClientRx.Service;
using MQTTClientRx.Extension;
using MQTTClientRx.Model;
using MQTTnet;
using MQTTnet.Core;
using MQTTnet.Core.Client;
using MQTTnet.Core.Protocol;
using MQTTnet.Core.Serializer;

// ReSharper disable PossibleMultipleEnumeration

namespace MQTTClientRx.Service
{
    public class MQTTService : IMQTTService
    {
        public (IObservable<IMQTTMessage> observableMessage, IMQTTClient client)
            CreateObservableMQTTService(
                IClientOptions options,
                IEnumerable<ITopicFilter> topicFilters = null,
                IWillMessage willMessage = null)
        {
            var isConnected = false;

            var client = new MqttClientFactory().CreateMqttClient(UnwrapOptions(options));
            var wrappedClient = new MQTTClient(client);

            var observable = Observable.Create<IMQTTMessage>(
                    async obs =>
                    {
                        var disposableConnect = Observable.FromEventPattern(
                                h => client.Connected += h,
                                h => client.Connected -= h)
                            //.SubscribeOn(Scheduler.CurrentThread)
                            //.ObserveOn(Scheduler.CurrentThread)
                            .Subscribe(
                                async connectEvent =>
                                {
                                    Debug.WriteLine("Connected");
                                    if (topicFilters?.Any() ?? false)
                                    {
                                        try
                                        {
                                            await wrappedClient.SubscribeAsync(topicFilters);
                                        }
                                        catch (Exception ex)
                                        {
                                            obs.OnError(ex);
                                        }
                                    }
                                },
                                obs.OnError,
                                obs.OnCompleted);

                        var disposableMessage = Observable.FromEventPattern<MqttApplicationMessageReceivedEventArgs>(
                                h => client.ApplicationMessageReceived += h,
                                h => client.ApplicationMessageReceived -= h)
                            //.ObserveOn(Scheduler.CurrentThread)
                            //.SubscribeOn(Scheduler.Default)
                            .Subscribe(
                                msgEvent =>
                                {
                                    var message = new MQTTMessage
                                    {
                                        Payload = msgEvent.EventArgs.ApplicationMessage.Payload,
                                        Retain = msgEvent.EventArgs.ApplicationMessage.Retain,
                                        QualityOfServiceLevel =
                                            (QoSLevel)msgEvent.EventArgs.ApplicationMessage.QualityOfServiceLevel,
                                        Topic = msgEvent.EventArgs.ApplicationMessage.Topic
                                    };

                                    obs.OnNext(message);
                                },
                                obs.OnError,
                                obs.OnCompleted);

                        var disposableDisconnect = Observable.FromEventPattern(
                                h => client.Disconnected += h,
                                h => client.Disconnected -= h)
                            //.SubscribeOn(Scheduler.CurrentThread)
                            //.ObserveOn(Scheduler.CurrentThread)
                            .Subscribe(
                                disconnectEvent =>
                                {
                                    if (!isConnected) return;
                                    Debug.WriteLine("Disconnected");
                                    obs.OnCompleted();
                                },
                                obs.OnError,
                                obs.OnCompleted);

                        if (!isConnected)
                        {
                            try
                            {
                                await client.ConnectAsync(WrapWillMessage(willMessage));
                                isConnected = true;
                            }
                            catch (Exception ex)
                            {
                                isConnected = false;
                                obs.OnError(ex);
                                
                            }
                        }

                        return new CompositeDisposable(
                            Disposable.Create(async () => { await CleanUp(client); }),
                            disposableMessage,
                            disposableConnect,
                            disposableDisconnect);
                    })
                .FinallyAsync(async () => { await CleanUp(client); })
                .Publish().RefCount();

            return (observable, wrappedClient);
        }

        private async Task CleanUp(IMqttClient client)
        {
            if (client.IsConnected)
            {
                var disconnectTask = client.DisconnectAsync();
                var timeOutTask = Task.Delay(TimeSpan.FromSeconds(5));

                var result = await Task.WhenAny(disconnectTask, timeOutTask).ConfigureAwait(false);

                Debug.WriteLine($"Disconnected Successfully: {result == disconnectTask}");
            }
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
                    Certificates = wrappedOptions.Certificates?.ToList()
                },
                UserName = wrappedOptions.UserName,
                Password = wrappedOptions.Password,
                KeepAlivePeriod = wrappedOptions.KeepAlivePeriod == default(TimeSpan)
                    ? TimeSpan.FromSeconds(5)
                    : wrappedOptions.KeepAlivePeriod,
                DefaultCommunicationTimeout = wrappedOptions.DefaultCommunicationTimeout == default(TimeSpan)
                    ? TimeSpan.FromSeconds(10)
                    : wrappedOptions.DefaultCommunicationTimeout,
                ProtocolVersion = UnwrapProtocolVersion(wrappedOptions.ProtocolVersion),
                ConnectionType = UnwrapConnectionType(wrappedOptions.ConnectionType)
            };
        }

        private MqttApplicationMessage WrapWillMessage(IWillMessage message)
        {
            if (message != null)
            {
                return new MqttApplicationMessage(
                    message.Topic,
                    message.Payload,
                    (MqttQualityOfServiceLevel) message.QualityOfServiceLevel,
                    message.Retain);
            }
            return null;
        }

        private static MqttProtocolVersion UnwrapProtocolVersion(ProtocolVersion protocolVersion)
        {
            switch (protocolVersion)
            {
                case ProtocolVersion.ver310: return MqttProtocolVersion.V310;
                case ProtocolVersion.ver311: return MqttProtocolVersion.V311;
                default: throw new ArgumentException(protocolVersion.ToString());
            }
        }

        private static MqttConnectionType UnwrapConnectionType(ConnectionType connectionType)
        {
            switch (connectionType)
            {
                case ConnectionType.Tcp: return MqttConnectionType.Tcp;
                case ConnectionType.WebSocket: return MqttConnectionType.Ws;
                default: throw new ArgumentOutOfRangeException(nameof(connectionType), connectionType, null);
            }
        }
    }
}