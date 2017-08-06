using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IMQTTClientRx.Model;

namespace IMQTTClientRx.Service
{
    public interface IMQTTService
    {
        (IObservable<IMQTTMessage> observableMessage, Model.IMQTTClient client, Task cleanUp) CreateObservableMQTTServiceAsync(
            IClientOptions options, 
            IEnumerable<ITopicFilter> topicFilters = null,
            IWillMessage willMessage = null);
    }
}
