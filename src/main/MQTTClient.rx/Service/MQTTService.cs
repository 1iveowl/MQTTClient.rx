using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using IMQTTClientRx.Model;
using IMQTTClientRx.Service;
using MQTTClientRx.Model;
using MQTTnet;
using MQTTnet.Core.Client;

namespace MQTTClientRx.Service
{
    public class MQTTService : IMQTTService
    {
        public (IObservable<IMQTTMessage> observableMessage, IMQTTClient client) 
            CreateObservableMQTTServiceAsync(IMQTTClientOptions options, IEnumerable<ITopicFilter> topicFilters = null)
        {
            var client = new MqttClientFactory().CreateMqttClient(UnwrapOptions(options));

            var observable = Observable.Create<IMQTTMessage>(
                obs =>
                {
                    var disposableMessage = Observable.FromEventPattern<MqttApplicationMessageReceivedEventArgs>(
                            h => client.ApplicationMessageReceived += h,
                            h => client.ApplicationMessageReceived -= h)
                        .Subscribe(msgEvent =>
                        {
                            var message = new MQTTMessage
                            {
                                Payload = msgEvent.EventArgs.ApplicationMessage.Payload,
                                Retain = msgEvent.EventArgs.ApplicationMessage.Retain,
                                QualityOfServiceLevel = (MQTTQoSLevel)msgEvent.EventArgs.ApplicationMessage.QualityOfServiceLevel,
                                Topic = msgEvent.EventArgs.ApplicationMessage.Topic
                            };
                            
                            obs.OnNext(message);
                        },
                        obs.OnError,
                        obs.OnCompleted);

                    var disposableConnect = Observable.FromEventPattern(
                            h => client.Connected += h,
                            h => client.Connected -= h)
                        .Subscribe(connectEvent =>
                        {
                            Debug.WriteLine("Connected");
                        });

                    var disposableDisconnect = Observable.FromEventPattern(
                            h => client.Disconnected += h,
                            h => client.Disconnected -= h)
                        .Subscribe(connectEvent =>
                        {
                            Debug.WriteLine("Disconnected");
                            obs.OnCompleted();
                        });

                    return new CompositeDisposable(disposableConnect, disposableDisconnect, disposableMessage);

                });


            var wrappedClient = new MQTTClient(client);
            
            return (observable, wrappedClient);
        }
        
        private static MqttClientOptions UnwrapOptions(IMQTTClientOptions wrappedOptions)
        {
            return new MqttClientOptions
            {
                CleanSession = wrappedOptions.CleanSession,
                ClientId = wrappedOptions.ClientId ?? Guid.NewGuid().ToString().Replace("-", string.Empty),
                Port = wrappedOptions.Port,
                // TODO this is a bit wierd, why can TlsOptions be set? Ask Christian
                //TlsOptions = new MqttClientTlsOptions
                //{
                //    Certificates = options.TlsOptions.Certificates.ToList(),
                //    CheckCertificateRevocation = options.TlsOptions.CheckCertificateRevocation,
                //    UseTls = options.TlsOptions.UseTls
                //}
                UserName = wrappedOptions.UserName,
                Password = wrappedOptions.Password,
                KeepAlivePeriod = wrappedOptions.KeepAlivePeriod,
                DefaultCommunicationTimeout = wrappedOptions.DefaultCommunicationTimeout
            };
        }
    }
}
