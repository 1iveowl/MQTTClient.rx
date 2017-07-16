using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using IMQTTClient.rx.Model;
using IMQTTClient.rx.Service;

namespace MQTTClient.rx.Service
{
    public class MQTTService : IMQTTService
    {
        public Task<(IObservable<IMQTTMessage> observableMessage, IMQTTClient.rx.Model.IMQTTClient client)> CreateObservableMQTTServiceAsync(IMQTTClientOptions options, IEnumerable<ITopicFilter> topicFilters = null)
        {
            throw new NotImplementedException();
        }
    }
}
