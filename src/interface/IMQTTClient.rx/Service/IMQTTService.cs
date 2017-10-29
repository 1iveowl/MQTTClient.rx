using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IMQTTClientRx.Model;

namespace IMQTTClientRx.Service
{
    public interface IMQTTService
    {
        bool IsConnected { get; }

        (IObservable<IMQTTMessage> observableMessage, Model.IMQTTClient client) CreateObservableMQTTService(
            IClientOptions options, 
            IEnumerable<ITopicFilter> topicFilters = null,
            IWillMessage willMessage = null);

    }
}
