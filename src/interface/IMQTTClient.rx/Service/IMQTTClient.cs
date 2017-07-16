using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using IMQTTClient.rx.Model;

namespace IMQTTClient.rx.Service
{
    public interface IMQTTClient
    {
        Task<IObservable<IMQTTMessage>> ObservableMQTTClientAsync();
        Task SubscribeAsync(IEnumerable<ITopicFilter> topicFilters);
        Task UnsubscribeAsync(IEnumerable<ITopicFilter> topicFilters);
    }
}
