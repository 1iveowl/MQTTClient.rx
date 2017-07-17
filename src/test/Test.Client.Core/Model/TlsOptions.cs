using System;
using System.Collections.Generic;
using System.Text;
using IMQTTClientRx.Model;

namespace Test.Client.Core.Model
{
   internal  class TlsOptions : ITlsOptions
    {
        public bool UseTls { get; internal set; }
        public bool CheckCertificateRevocation { get; internal set; }
        public IEnumerable<byte[]> Certificates { get; internal set; }
    }
}
