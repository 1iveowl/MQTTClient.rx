using System;
using System.Collections.Generic;
using System.Text;

namespace IMQTTClientRx.Model
{
    public interface IClientOptions : ITlsOptions
    {
        string Server { get;}

        int? Port { get;}

        string Url { get; }

        string UserName { get;}

        string Password { get;}

        string ClientId { get;}

        bool CleanSession { get;}

        TimeSpan KeepAlivePeriod { get;}

        TimeSpan DefaultCommunicationTimeout { get;}
        ProtocolVersion ProtocolVersion { get; }

        ConnectionType ConnectionType { get; }
    }
}
