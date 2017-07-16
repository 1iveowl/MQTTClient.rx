using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IMQTTClientRx.Model;

namespace IMQTTClientRx.Service
{
    public interface IMQTTService
    {
        (IObservable<IMQTTMessage> observableMessage, Model.IMQTTClient client) CreateObservableMQTTServiceAsync(IMQTTClientOptions options, IEnumerable<ITopicFilter> topicFilters = null);
    }
}
