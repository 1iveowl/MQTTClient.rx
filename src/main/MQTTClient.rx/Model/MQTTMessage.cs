using System;
using System.Collections.Generic;
using System.Text;
using IMQTTClient.rx.Model;

namespace MQTTClient.rx.Model
{
    internal class MQTTMessage : IMQTTMessage
    {
        public string Topic { get; internal set; }
        public byte[] Payload { get; internal set; }
        public MQTTQoSLevel QualityOfServiceLevel { get; internal set; }
        public bool Retain { get; internal set; }
    }
}
