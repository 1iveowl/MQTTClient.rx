using System;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using IMQTTClientRx.Model;
using IMQTTClientRx.Service;
using MQTTClientRx.Extension;
using MQTTClientRx.Model;

// ReSharper disable PossibleMultipleEnumeration

namespace MQTTClientRx.Service
{
    public class MQTTService : IMQTTService
    {
        private MQTTClient _wrappedClient;

        internal IClientOptions ClientOptions { get; private set; }
        internal IWillMessage WillMessage { get; private set; }

        public bool IsConnected => _wrappedClient?.IsConnected ?? false;

        public (IObservable<IMQTTMessage> observableMessage, IMQTTClient client)
            CreateObservableMQTTClient(
                IClientOptions options,
                IWillMessage willMessage = null,
                params ITopicFilter[] topicFilters)
        {
            ClientOptions = options;
            WillMessage = willMessage;

             _wrappedClient = new MQTTClient(this, topicFilters);

            var observable = Observable.Create<IMQTTMessage>(
                    obs =>
                    {
                        var disposableConnect = _wrappedClient.ObservableConnect
                            .Subscribe(_ =>
                            {

                            },
                            obs.OnError,
                            obs.OnCompleted);

                        var disposableMessage = _wrappedClient.ObservableMessage
                            .Subscribe(
                                obs.OnNext,
                                obs.OnError,
                                obs.OnCompleted);

                        var disposableDisconnect = _wrappedClient.ObservableDisconnect
                            .Where(disconnect => disconnect == true)
                            .Select(x => Observable.FromAsync(() => _wrappedClient.DisconnectAsync()).Timeout(TimeSpan.FromSeconds(5)))
                            .Concat()
                            .Subscribe(d =>
                                {
                                    Debug.WriteLine("Disconnected");
                                    obs.OnCompleted();
                                },
                                obs.OnError,
                                obs.OnCompleted);

                        return new CompositeDisposable(
                            disposableMessage,
                            disposableConnect,
                            disposableDisconnect);
                    })
                .FinallyAsync(async () => { await _wrappedClient?.DisconnectAsync(); })
                .Publish().RefCount();

            return (observable, _wrappedClient);
        }
    }
}