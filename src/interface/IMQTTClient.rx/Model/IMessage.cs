using System;
using System.Collections.Generic;
using System.Text;

namespace IMQTTClientRx.Model
{
    public interface IMessage
    {
        string Topic { get; }

        byte[] Payload { get; }

        QoSLevel QualityOfServiceLevel { get; }

        bool Retain { get; }
    }
}
