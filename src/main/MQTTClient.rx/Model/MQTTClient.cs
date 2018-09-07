using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IMQTTClientRx.Model;
using IMQTTClientRx.Service;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace MQTTClientRx.Model
{
    internal class MQTTClient : IMQTTClient
    {
        private readonly IMqttClient _mqttClient;
        private readonly IMQTTService _mqttService;

        internal MQTTClient(IMqttClient client, IMQTTService mqttService)
        {
            _mqttClient = client;
            _mqttService = mqttService;
        }
        
        public async Task SubscribeAsync(IEnumerable<ITopicFilter> topicFilters)
        {
            await _mqttClient.SubscribeAsync(WrapTopicFilters(topicFilters));
        }

        public async Task UnsubscribeAsync(IEnumerable<ITopicFilter> topicFilters)
        {
            await _mqttClient.UnsubscribeAsync(WrapTopicFiltersToString(topicFilters));
        }

        public async Task UnsubscribeAsync(string[] topics)
        {
            await _mqttClient.UnsubscribeAsync(topics);
        }

        public async Task PublishAsync(IMQTTMessage message)
        {
            while (!_mqttService.IsConnected)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            await _mqttClient.PublishAsync(WrapMessage(message));
        }

        private IList<string> WrapTopicFiltersToString(IEnumerable<ITopicFilter> topicFilters)
        {
            return WrapTopicFilters(topicFilters).Select(x => x.Topic).ToArray();
        }

        private IList<TopicFilter> WrapTopicFilters(IEnumerable<ITopicFilter> topicFilters)
        {
            return topicFilters.Select(tFilter => new TopicFilter(tFilter.Topic, ConvertQosLevel(tFilter.QualityOfServiceLevel))).ToList();
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
    }
}
