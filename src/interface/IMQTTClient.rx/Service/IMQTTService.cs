using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IMQTTClientRx.Model;

namespace IMQTTClientRx.Service
{
    public interface IMQTTService
    {
        bool IsConnected { get; }

        Task<(IObservable<IMQTTMessage> observableMessage, Model.IMQTTClient client)> CreateObservableMQTTServiceAsync(
            IClientOptions options, 
            IEnumerable<ITopicFilter> topicFilters = null,
            IWillMessage willMessage = null);

        Task<IMQTTClient> ConnectAsync(IClientOptions options, IWillMessage willMessage);

        Task DisconnectAsync();
    }
}
