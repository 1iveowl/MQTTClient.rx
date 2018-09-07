using System;
using System.Collections.Generic;
using System.Text;

namespace IMQTTClientRx.Model
{
    public interface IClientOptions : ITlsOptions
    {
        Uri Uri { get; }

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
