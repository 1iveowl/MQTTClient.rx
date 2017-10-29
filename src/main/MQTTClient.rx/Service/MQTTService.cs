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

        public bool IsConnected { get; private set; }

        public (IObservable<IMQTTMessage> observableMessage, IMQTTClient client)
            CreateObservableMQTTService(
                IClientOptions options,
                IEnumerable<ITopicFilter> topicFilters = null,
                IWillMessage willMessage = null)
        {
            IsConnected = false;

            _client = new MqttClientFactory().CreateMqttClient();
            _wrappedClient = new MQTTClient(_client, this);

            IsConnected = false;

            var observable = Observable.Create<IMQTTMessage>(
                    async obs =>
                    {
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
                                            ConvertToQoSLevel(msgEvent.EventArgs.ApplicationMessage.QualityOfServiceLevel),
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
                            try
                            {
                                await _client.ConnectAsync(UnwrapOptions(options, willMessage));
                                IsConnected = true;
                            }
                            catch (Exception ex)
                            {
                                IsConnected = false;
                                obs.OnError(ex);

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

                IsConnected = false;
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
                    Server = wrappedOptions.Uri.Host,
                    CleanSession = wrappedOptions.CleanSession,
                    ClientId = wrappedOptions.ClientId ?? Guid.NewGuid().ToString().Replace("-", string.Empty),
                    Port = wrappedOptions.Uri.Port,
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
                    Uri = wrappedOptions.Uri.AbsoluteUri,
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
                    ConvertToQualityOfServiceLevel(message.QualityOfServiceLevel),
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

        private static QoSLevel ConvertToQoSLevel(MqttQualityOfServiceLevel qos)
        {
            switch (qos)
            {
                case MqttQualityOfServiceLevel.AtMostOnce: return QoSLevel.AtMostOnce;
                case MqttQualityOfServiceLevel.AtLeastOnce: return QoSLevel.AtLeastOnce;
                case MqttQualityOfServiceLevel.ExactlyOnce: return QoSLevel.ExactlyOnce;
                default:
                    throw new ArgumentOutOfRangeException(nameof(qos), qos, null);
            }
        }

        private static MqttQualityOfServiceLevel ConvertToQualityOfServiceLevel(QoSLevel qos)
        {
            switch (qos)
            {
                case QoSLevel.AtMostOnce: return MqttQualityOfServiceLevel.AtMostOnce;
                case QoSLevel.AtLeastOnce: return MqttQualityOfServiceLevel.AtLeastOnce;
                case QoSLevel.ExactlyOnce: return MqttQualityOfServiceLevel.ExactlyOnce;
                default:
                    throw new ArgumentOutOfRangeException(nameof(qos), qos, null);
            }
        }
    }
}