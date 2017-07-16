using System;
using System.Collections.Generic;
using System.Text;
using IMQTTClient.rx.Model;

namespace IMQTTClient.rx.Service
{
    public interface IMQTTMessage
    {
        string Topic { get; }

        byte[] Payload { get; }

        MQTTQoSLevel QualityOfServiceLevel { get; }

        bool Retain { get; }
    }
}
