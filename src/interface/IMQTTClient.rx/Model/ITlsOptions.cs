using System;
using System.Collections.Generic;
using System.Text;

namespace IMQTTClientRx.Model
{
    public interface ITlsOptions
    {
        bool UseTls { get;}

        bool CheckCertificateRevocation { get;}

        IEnumerable<byte[]> Certificates { get;}
    }
}
