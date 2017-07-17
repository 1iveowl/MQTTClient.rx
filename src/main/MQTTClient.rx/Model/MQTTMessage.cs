using System;
using System.Collections.Generic;
using System.Text;
using IMQTTClientRx.Model;

namespace MQTTClientRx.Model
{
    internal class MQTTMessage : IMQTTMessage
    {
        public string Topic { get; internal set; }
        public byte[] Payload { get; internal set; }
        public QoSLevel QualityOfServiceLevel { get; internal set; }
        public bool Retain { get; internal set; }
    }
}
