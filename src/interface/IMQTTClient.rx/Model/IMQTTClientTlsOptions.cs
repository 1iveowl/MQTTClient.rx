using System;
using System.Collections.Generic;
using System.Text;

namespace IMQTTClientRx.Model
{
    public interface IMQTTClientTlsOptions
    {
        bool UseTls { get;}

        bool CheckCertificateRevocation { get;}

        IEnumerable<byte[]> Certificates { get;}
    }
}
