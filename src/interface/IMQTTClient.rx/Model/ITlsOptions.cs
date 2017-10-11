using System;
using System.Collections.Generic;
using System.Text;

namespace IMQTTClientRx.Model
{
    public interface ITlsOptions
    {
        bool UseTls { get;}

        IEnumerable<byte[]> Certificates { get;}

        bool IgnoreCertificateChainErrors { get; }

        bool IgnoreCertificateRevocationErrors { get; }

        bool AllowUntrustedCertificates { get; }
    }
}
