using System.Collections.Generic;
using IMQTTClientRx.Model;

namespace Test.Client.UWP.Model
{
   internal  class TlsOptions : ITlsOptions
    {
        public bool UseTls { get; internal set; }
        public bool CheckCertificateRevocation { get; internal set; }
        public IEnumerable<byte[]> Certificates { get; internal set; }
    }
}
