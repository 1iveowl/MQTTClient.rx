using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IMQTTClient.rx.Model;

namespace MQTTClient.rx.Model
{
    internal class MQTTClient : IMQTTClient.rx.Model.IMQTTClient
    {
        public Task SubscribeAsync(IEnumerable<ITopicFilter> topicFilters)
        {
            throw new NotImplementedException();
        }

        public Task UnsubscribeAsync(IEnumerable<ITopicFilter> topicFilters)
        {
            throw new NotImplementedException();
        }

        public Task PublishAsync(IMQTTMessage message)
        {
            throw new NotImplementedException();
        }
    }
}
