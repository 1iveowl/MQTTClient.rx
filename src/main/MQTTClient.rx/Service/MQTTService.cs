using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
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
using MQTTnet.Core.Adapter;
using MQTTnet.Core.Client;
using MQTTnet.Core.Protocol;
using MQTTnet.Core.Serializer;

// ReSharper disable PossibleMultipleEnumeration

namespace MQTTClientRx.Service
{
    public class MQTTService : IMQTTService
    {
        private IMqttClient _client;
        private IMQTTClient _wrappedClient;
        private IObserver<IMQTTMessage> _mqttObserver;

        public bool IsConnected { get; private set; }

        public async Task<(IObservable<IMQTTMessage> observableMessage, IMQTTClient client)>
            CreateObservableMQTTServiceAsync(
                IClientOptions options,
                IEnumerable<ITopicFilter> topicFilters = null,
                IWillMessage willMessage = null)
        {
            await InitializeClient();

            if (IsConnected)
            {
                await CleanUp(_client);
            }

            IsConnected = false;

            var observable = Observable.Create<IMQTTMessage>(
                    async obs =>
                    {
                        _mqttObserver = obs.AsObserver();

                        var disposableConnect = Observable.FromEventPattern(
                                h => _client.Connected += h,
                                h => _client.Connected -= h)
                            .Subscribe(
                                async connectEvent =>
                                {
                                    Debug.WriteLine("Connected");
                                    if (topicFilters?.Any() ?? false)
                                    {
                                        try
                                        {
                                            await _wrappedClient.SubscribeAsync(topicFilters);
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
                                h => _client.ApplicationMessageReceived += h,
                                h => _client.ApplicationMessageReceived -= h)
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
                                h => _client.Disconnected += h,
                                h => _client.Disconnected -= h)
                            .Subscribe(
                                disconnectEvent =>
                                {
                                    if (!IsConnected) return;
                                    Debug.WriteLine("Disconnected");
                                    obs.OnCompleted();
                                },
                                obs.OnError,
                                obs.OnCompleted);

                        if (!IsConnected)
                        {
                            var resultException = await ConnectClientAlreadyInitializedAsync(options, willMessage);

                            if (resultException != null)
                            {
                                obs.OnError(resultException);
                            }
                        }

                        return new CompositeDisposable(
                            Disposable.Create(async () => { await CleanUp(_client); }),
                            disposableMessage,
                            disposableConnect,
                            disposableDisconnect);
                    })
                .FinallyAsync(async () => { await CleanUp(_client); })
                .Publish().RefCount();

            return (observable, _wrappedClient);
        }

        public async Task ConnectAsync(IClientOptions options, IWillMessage willMessage)
        {
            await InitializeClient();

            if (IsConnected)
            {
                throw new Exception("MQTT client already connected. Disconnect before making new connection.");
            }

            await ConnectClientAlreadyInitializedAsync(options, willMessage);

        }

        private async Task<Exception> ConnectClientAlreadyInitializedAsync(IClientOptions options, IWillMessage willMessage)
        {
            try
            {
                await _client.ConnectAsync(UnwrapOptions(options, willMessage));
                IsConnected = true;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                return ex;
            }
            return null;
        }

        public async Task DisconnectAsync()
        {
            if (_client != null && _wrappedClient != null && _client.IsConnected)
            {
                _mqttObserver?.OnCompleted();

                await CleanUp(_client);
            }
        }

        private async Task CleanUp(IMqttClient client)
        {
            if (client.IsConnected)
            {
                var disconnectTask = client.DisconnectAsync();
                var timeOutTask = Task.Delay(TimeSpan.FromSeconds(5));

                var result = await Task.WhenAny(disconnectTask, timeOutTask).ConfigureAwait(false);

                Debug.WriteLine(result == timeOutTask
                    ? "Disconnect Timed Out"
                    : "Disconnected Successfully");

                _mqttObserver = null;
            }
        }

        private static MqttClientOptions UnwrapOptions(IClientOptions wrappedOptions, IWillMessage willMessage)
        {
            //var wrappedWillMessage = WrapWillMessage(willMessage);

            if (wrappedOptions.ConnectionType == ConnectionType.Tcp)
            {
                return new MqttClientTcpOptions()
                {
                    WillMessage = WrapWillMessage(willMessage),
                    Server = wrappedOptions.Server,
                    CleanSession = wrappedOptions.CleanSession,
                    ClientId = wrappedOptions.ClientId ?? Guid.NewGuid().ToString().Replace("-", string.Empty),
                    Port = wrappedOptions.Port,
                    TlsOptions =
                    {
                        UseTls = wrappedOptions.UseTls,
                        Certificates = wrappedOptions.Certificates?.ToList(),
                        IgnoreCertificateChainErrors = wrappedOptions.IgnoreCertificateChainErrors,
                        IgnoreCertificateRevocationErrors = wrappedOptions.IgnoreCertificateRevocationErrors,
                        AllowUntrustedCertificates = wrappedOptions.AllowUntrustedCertificates
                    },
                    UserName = wrappedOptions.UserName,
                    Password = wrappedOptions.Password,
                    KeepAlivePeriod = wrappedOptions.KeepAlivePeriod == default(TimeSpan)
                        ? TimeSpan.FromSeconds(5)
                        : wrappedOptions.KeepAlivePeriod,
                    DefaultCommunicationTimeout = wrappedOptions.DefaultCommunicationTimeout == default(TimeSpan)
                        ? TimeSpan.FromSeconds(10)
                        : wrappedOptions.DefaultCommunicationTimeout,
                    ProtocolVersion = UnwrapProtocolVersion(wrappedOptions.ProtocolVersion)
                };
            }
            else
            {
                return new MqttClientWebSocketOptions()
                {
                    WillMessage = WrapWillMessage(willMessage),
                    Uri = wrappedOptions.Url,
                    CleanSession = wrappedOptions.CleanSession,
                    ClientId = wrappedOptions.ClientId ?? Guid.NewGuid().ToString().Replace("-", string.Empty),
                    TlsOptions =
                    {
                        UseTls = wrappedOptions.UseTls,
                        Certificates = wrappedOptions.Certificates?.ToList(),
                        IgnoreCertificateChainErrors = wrappedOptions.IgnoreCertificateChainErrors,
                        IgnoreCertificateRevocationErrors = wrappedOptions.IgnoreCertificateRevocationErrors,
                        AllowUntrustedCertificates = wrappedOptions.AllowUntrustedCertificates
                    },
                    UserName = wrappedOptions.UserName,
                    Password = wrappedOptions.Password,
                    KeepAlivePeriod = wrappedOptions.KeepAlivePeriod == default(TimeSpan)
                        ? TimeSpan.FromSeconds(5)
                        : wrappedOptions.KeepAlivePeriod,
                    DefaultCommunicationTimeout = wrappedOptions.DefaultCommunicationTimeout == default(TimeSpan)
                        ? TimeSpan.FromSeconds(10)
                        : wrappedOptions.DefaultCommunicationTimeout,
                    ProtocolVersion = UnwrapProtocolVersion(wrappedOptions.ProtocolVersion)
                };
            }

        }

        private static MqttApplicationMessage WrapWillMessage(IWillMessage message)
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

        private static MqttClientTcpOptions UnwrapConnectionType(ConnectionType connectionType)
        {
            switch (connectionType)
            {
                default: throw new ArgumentOutOfRangeException(nameof(connectionType), connectionType, null);
            }
        }

        private async Task InitializeClient()
        {
            if (_client == null)
            {
                _client = new MqttClientFactory().CreateMqttClient();
            }
            //else
            //{
            //    if (_client.IsConnected)
            //    {
            //        await CleanUp(_client);
            //    }
            //}

            if (_wrappedClient == null)
            {
                _wrappedClient = new MQTTClient(_client);
            }
        }
    }
}