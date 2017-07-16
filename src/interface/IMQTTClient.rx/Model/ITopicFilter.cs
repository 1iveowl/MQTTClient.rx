using System;
using System.Collections.Generic;
using System.Text;

namespace IMQTTClient.rx.Model
{
    public interface ITopicFilter
    {
        string Topic { get; }
        MQTTQoSLevel QualityOfServiceLevel { get; }
    }
}
