using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IMQTTClient.rx.Model;

namespace IMQTTClient.rx.Service
{
    public interface IMQTTService
    {
        Task<(IObservable<IMQTTMessage> observableMessage, Model.IMQTTClient client)> CreateObservableMQTTServiceAsync(IMQTTClientOptions options, IEnumerable<ITopicFilter> topicFilters = null);
    }
}
