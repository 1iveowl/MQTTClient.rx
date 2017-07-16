using System;
using System.Collections.Generic;
using System.Text;

namespace IMQTTClient.rx.Model
{
    public interface IMQTTClientTlsOptions
    {
        bool UseTls { get;}

        bool CheckCertificateRevocation { get;}

        IEnumerable<byte[]> Certificates { get;}
    }
}
