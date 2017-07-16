using System;
using System.Collections.Generic;
using System.Text;

namespace IMQTTClient.rx.Model
{
    public interface IMQTTClientOptions
    {
        string Server { get;}

        int? Port { get;}

        IMQTTClientTlsOptions TlsOptions { get; }

        string UserName { get;}

        string Password { get;}

        string ClientId { get;}

        bool CleanSession { get;}

        TimeSpan KeepAlivePeriod { get;}

        TimeSpan DefaultCommunicationTimeout { get;}
    }
}
