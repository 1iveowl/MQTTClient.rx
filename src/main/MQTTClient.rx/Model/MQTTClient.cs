using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using IMQTTClientRx.CustomException;
using IMQTTClientRx.Model;
using IMQTTClientRx.Service;
using MQTTClientRx.Service;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet.Serializer;

namespace MQTTClientRx.Model
{
    internal class MQTTClient : IMQTTClient
    {
        private readonly IMqttClient _mqttClient;
        private readonly MQTTService _mqttService;

        private readonly ITopicFilter [] _topicFilters;

        internal IObservable<Unit> ObservableConnect => Observable.FromEventPattern<MqttClientConnectedEventArgs>(
                h => _mqttClient.Connected += h,
                h => _mqttClient.Connected -= h)
            .Where(x => _topicFilters?.Any() ?? false)
            .Select(x => Observable.FromAsync(() => SubscribeAsync(_topicFilters)))
            .Concat();

        internal IObservable<bool> ObservableDisconnect => Observable.FromEventPattern<MqttClientDisconnectedEventArgs>(
                h => _mqttClient.Disconnected += h,
                h => _mqttClient.Disconnected -= h)
            .Select(x => x.EventArgs.ClientWasConnected == false);

        internal IObservable<IMQTTMessage> ObservableMessage => Observable
            .FromEventPattern<MqttApplicationMessageReceivedEventArgs>(
                h => _mqttClient.ApplicationMessageReceived += h,
                h => _mqttClient.ApplicationMessageReceived -= h)
            .Select(msgEvent => new MQTTMessage
                {
                    Payload = msgEvent.EventArgs.ApplicationMessage.Payload,
                    Retain = msgEvent.EventArgs.ApplicationMessage.Retain,
                    QualityOfServiceLevel =
                        ConvertToQoSLevel(msgEvent.EventArgs.ApplicationMessage.QualityOfServiceLevel),
                    Topic = msgEvent.EventArgs.ApplicationMessage.Topic
                });


        public bool IsConnected { get; private set; }

        internal MQTTClient(IMQTTService mqttService, ITopicFilter [] topicFilters)
        {
            _mqttService = mqttService as MQTTService;
            _topicFilters = topicFilters;

            var factory = new MqttFactory();

            _mqttClient = factory.CreateMqttClient();
        }

        public async Task ConnectAsync()
        {
            if (!_mqttService.IsConnected)
            {
                try
                {
                    var opt = UnwrapOptions(_mqttService.ClientOptions, _mqttService.WillMessage);

                    try
                    {
                        await _mqttClient.ConnectAsync(opt);
                    }
                    catch (Exception ex)
                    {
                        throw new MqttClientRxException($"Unable to connect to: {_mqttService.ClientOptions.Uri.AbsoluteUri}", ex);
                    }

                    ;

                    IsConnected = _mqttClient.IsConnected; ;

                    if (!_mqttService.IsConnected)
                    {
                        throw new MqttClientRxException($"Unable to connect to: {_mqttService.ClientOptions.Uri.AbsoluteUri}");
                    }

                }
                catch (Exception)
                {
                    IsConnected = false;
                }
            }
        }

        public async Task DisconnectAsync()
        {
            await _mqttClient.DisconnectAsync();
        }

        public async Task SubscribeAsync(params ITopicFilter [] topicFilters)
        {
            await _mqttClient.SubscribeAsync(WrapTopicFilters(topicFilters));
        }

        public async Task UnsubscribeAsync(params ITopicFilter [] topicFilters)
        {
            await _mqttClient.UnsubscribeAsync(WrapTopicFiltersToString(topicFilters));
        }

        public async Task UnsubscribeAsync(params string[] topics)
        {
            await _mqttClient.UnsubscribeAsync(topics);
        }

        public async Task PublishAsync(IMQTTMessage message)
        {
            await _mqttClient.PublishAsync(WrapMessage(message));
        }

        private IList<string> WrapTopicFiltersToString(IEnumerable<ITopicFilter> topicFilters)
        {
            return WrapTopicFilters(topicFilters).Select(x => x.Topic).ToArray();
        }

        private IEnumerable<TopicFilter> WrapTopicFilters(IEnumerable<ITopicFilter> topicFilters)
        {
            return topicFilters.Select(tFilter =>
            {
                var tf = new TopicFilterBuilder().WithTopic(tFilter.Topic);

                switch (tFilter.QualityOfServiceLevel)
                {
                    case QoSLevel.AtMostOnce:
                        tf.WithAtMostOnceQoS();
                        break;
                    case QoSLevel.AtLeastOnce:
                        tf.WithAtLeastOnceQoS();
                        break;
                    case QoSLevel.ExactlyOnce:
                        tf.WithExactlyOnceQoS();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                return tf.Build();
            });
        }

        private MqttQualityOfServiceLevel ConvertQosLevel(QoSLevel qosLvl)
        {
            switch (qosLvl)
            {
                case QoSLevel.AtMostOnce: return MqttQualityOfServiceLevel.AtMostOnce;
                case QoSLevel.AtLeastOnce: return MqttQualityOfServiceLevel.AtLeastOnce;
                case QoSLevel.ExactlyOnce: return MqttQualityOfServiceLevel.ExactlyOnce;
                default:
                    throw new ArgumentOutOfRangeException(nameof(qosLvl), qosLvl, null);
            }
        }

        private MqttApplicationMessage WrapMessage(IMQTTMessage message)
        {
            return new MqttApplicationMessage
            {
                Payload = message.Payload,
                QualityOfServiceLevel = ConvertQosLevel(message.QualityOfServiceLevel),
                Retain = message.Retain,
                Topic = message.Topic
            };

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
                        UseTls = wrappedOptions.UseTls
                    });
            }

            return optionsBuilder
                .WithWillMessage(WrapWillMessage(willMessage))
                .WithCleanSession(wrappedOptions.CleanSession)
                .WithClientId(wrappedOptions.ClientId ?? Guid.NewGuid().ToString().Replace("-", string.Empty))

                .WithProtocolVersion(UnwrapProtocolVersion(wrappedOptions.ProtocolVersion))
                .WithCommunicationTimeout(wrappedOptions.DefaultCommunicationTimeout == default
                    ? TimeSpan.FromSeconds(10)
                    : wrappedOptions.DefaultCommunicationTimeout)
                .WithKeepAlivePeriod(wrappedOptions.KeepAlivePeriod == default
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

        public void Dispose()
        {
            _mqttClient?.Dispose();
        }
    }
}
