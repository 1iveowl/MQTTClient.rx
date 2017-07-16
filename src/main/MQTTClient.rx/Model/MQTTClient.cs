using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IMQTTClientRx.Model;
using MQTTnet.Core;
using MQTTnet.Core.Client;
using MQTTnet.Core.Packets;
using MQTTnet.Core.Protocol;

namespace MQTTClientRx.Model
{
    internal class MQTTClient : IMQTTClient
    {
        private readonly MqttClient _mqttClient;

        internal MQTTClient(MqttClient client)
        {
            _mqttClient = client;
        }
        
        public async Task SubscribeAsync(IEnumerable<ITopicFilter> topicFilters)
        {
            await _mqttClient.SubscribeAsync(WrapTopicFilters(topicFilters));
        }

        public async Task UnsubscribeAsync(IEnumerable<ITopicFilter> topicFilters)
        {
            await _mqttClient.Unsubscribe(WrapTopicFiltersToString(topicFilters));
        }

        public async Task UnsubscribeAsync(string[] topics)
        {
            await _mqttClient.Unsubscribe(topics);
        }

        public async Task PublishAsync(IMQTTMessage message)
        {
            await _mqttClient.PublishAsync(WrapMessage(message));
        }

        private IList<string> WrapTopicFiltersToString(IEnumerable<ITopicFilter> topicFilters)
        {
            return WrapTopicFilters(topicFilters).Select(x => x.Topic).ToArray();
        }

        private IList<TopicFilter> WrapTopicFilters(IEnumerable<ITopicFilter> topicFilters)
        {
            return topicFilters.Select(tFilter => new TopicFilter(tFilter.Topic, MapQosLevel(tFilter.QualityOfServiceLevel))).ToList();
        }

        private MqttQualityOfServiceLevel MapQosLevel(MQTTQoSLevel qosLvl)
        {
            return (MqttQualityOfServiceLevel)qosLvl;
        }

        private MqttApplicationMessage WrapMessage(IMQTTMessage message)
        {
            
            return new MqttApplicationMessage(
                    message.Topic, 
                    message.Payload, 
                    MapQosLevel(message.QualityOfServiceLevel), 
                    retain:message.Retain);
        }
    }
}
