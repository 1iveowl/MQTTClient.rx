using System;
using System.Collections.Generic;
using System.Text;
using IMQTTClientRx.Model;

namespace Test.Client.Core.Model
{
    internal class WillMessage : IWillMessage
    {
        public string Topic { get; internal set; }
        public byte[] Payload { get; internal set; }
        public QoSLevel QualityOfServiceLevel { get; internal set; }
        public bool Retain { get; internal set; }
    }
}
