using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using IMQTTClient.rx.Service;

namespace IMQTTClient.rx.Model
{
    public interface IMQTTClient
    {
        Task SubscribeAsync(IEnumerable<ITopicFilter> topicFilters);
        Task UnsubscribeAsync(IEnumerable<ITopicFilter> topicFilters);
        Task PublishAsync(IMQTTMessage message);
    }
}
