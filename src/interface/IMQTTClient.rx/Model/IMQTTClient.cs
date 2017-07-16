using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using IMQTTClientRx.Service;

namespace IMQTTClientRx.Model
{
    public interface IMQTTClient
    {
        Task SubscribeAsync(IEnumerable<ITopicFilter> topicFilters);
        Task UnsubscribeAsync(IEnumerable<ITopicFilter> topicFilters);
        Task UnsubscribeAsync(string[] topics);
        Task PublishAsync(IMQTTMessage message);
    }
}
