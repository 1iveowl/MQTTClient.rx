using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using IMQTTClientRx.Model;
using IMQTTClientRx.Service;
using MQTTClientRx.Extension;
using MQTTClientRx.Model;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet.Serializer;

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

            var factory = new MqttFactory();

            _client = factory.CreateMqttClient();

            _wrappedClient = new MQTTClient(_client, this);

            IsConnected = false;

            var observable = Observable.Create<IMQTTMessage>(
                    obs =>
                    {
                        var disposableConnect = Observable.FromEventPattern<MqttClientConnectedEventArgs>(
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

                        var disposableDisconnect = Observable.FromEventPattern<MqttClientDisconnectedEventArgs>(
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

                        var observableAsyncConnect = Observable.FromAsync(async cancellationToken =>
                        {
                            if (!IsConnected)
                            {
                                try
                                {
                                    var opt = UnwrapOptions(options, willMessage);
                                    var connectResult = await _client.ConnectAsync(opt);

                                    IsConnected = connectResult.IsSessionPresent;

                                    if (!IsConnected)
                                    {
                                        obs.OnError(new Exception("Unable to connect"));
                                    }

                                }
                                catch (Exception ex)
                                {
                                    IsConnected = false;
                                    obs.OnError(ex);

                                }
                            }
                        });

                        var disposableAsyncConnect = observableAsyncConnect.Subscribe(_ =>
                        {

                        },
                        obs.OnError,
                        obs.OnCompleted);


                        //if (!IsConnected)
                        //{
                        //    try
                        //    {
                        //        var opt = UnwrapOptions(options, willMessage);
                        //        var connectResult = await _client.ConnectAsync(opt);

                        //        IsConnected = true; //connectResult.IsSessionPresent;

                        //        //if (!IsConnected)
                        //        //{
                        //        //    obs.OnError(new Exception("Unable to connect"));
                        //        //}

                        //    }
                        //    catch (Exception ex)
                        //    {
                        //        IsConnected = false;
                        //        obs.OnError(ex);

                        //    }
                        //}

                        return new CompositeDisposable(
                            Disposable.Create(async () => { await CleanUp(_client); }),
                            disposableMessage,
                            disposableConnect,
                            disposableDisconnect,
                            disposableAsyncConnect);
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

        private static IMqttClientOptions UnwrapOptions(IClientOptions wrappedOptions, IWillMessage willMessage)
        {
            var optionsBuilder = new MqttClientOptionsBuilder();

            if (wrappedOptions.ConnectionType == ConnectionType.Tcp)
            {
                optionsBuilder.WithTcpServer(wrappedOptions.Uri.Host, wrappedOptions.Uri.Port);
            }
            else
            {
                optionsBuilder.WithWebSocketServer(wrappedOptions.Uri.AbsoluteUri);
            }

            if (wrappedOptions.UseTls)
            {
                optionsBuilder
                    .WithTls(new MqttClientOptionsBuilderTlsParameters
                    {
                        AllowUntrustedCertificates = wrappedOptions.AllowUntrustedCertificates,
                        Certificates = UnwrapCertificates(wrappedOptions.Certificates),
                        IgnoreCertificateChainErrors = wrappedOptions.IgnoreCertificateChainErrors,
                    });
            }

            return optionsBuilder
                .WithWillMessage(WrapWillMessage(willMessage))
                .WithCleanSession(wrappedOptions.CleanSession)
                .WithClientId(wrappedOptions.ClientId ?? Guid.NewGuid().ToString().Replace("-", string.Empty))

                .WithProtocolVersion(UnwrapProtocolVersion(wrappedOptions.ProtocolVersion))
                .WithCommunicationTimeout(wrappedOptions.DefaultCommunicationTimeout == default(TimeSpan)
                    ? TimeSpan.FromSeconds(10)
                    : wrappedOptions.DefaultCommunicationTimeout)
                .WithKeepAlivePeriod(wrappedOptions.KeepAlivePeriod == default(TimeSpan)
                    ? TimeSpan.FromSeconds(5)
                    : wrappedOptions.KeepAlivePeriod)
                .WithCredentials(wrappedOptions.UserName, wrappedOptions.Password)
                .Build();
       }

        private static MqttApplicationMessage WrapWillMessage(IWillMessage message)
        {
            if (message != null)
            {
                var applicationMessage = new MqttApplicationMessageBuilder();

                applicationMessage
                    .WithTopic(message.Topic)
                    .WithPayload(message.Payload)
                    .WithRetainFlag(message.Retain);

                ConvertToQualityOfServiceLevel(applicationMessage, message.QualityOfServiceLevel);

                return applicationMessage.Build();
            }

            return null;
        }

        private static byte[][] UnwrapCertificates(IEnumerable<byte[]> certificates)
        {
            return certificates?.ToArray();
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

        private static void ConvertToQualityOfServiceLevel(MqttApplicationMessageBuilder builder, QoSLevel qos)
        {
            switch (qos)
            {
                case QoSLevel.AtMostOnce:
                    builder.WithAtMostOnceQoS();
                    break;
                case QoSLevel.AtLeastOnce:
                    builder.WithAtLeastOnceQoS();
                    break;
                case QoSLevel.ExactlyOnce:
                    builder.WithExactlyOnceQoS();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(qos), qos, null);
            }
        }
    }
}