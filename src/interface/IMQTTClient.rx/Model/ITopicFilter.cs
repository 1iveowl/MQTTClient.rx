using System;
using System.Collections.Generic;
using System.Text;

namespace IMQTTClientRx.Model
{
    public interface ITopicFilter
    {
        string Topic { get; }
        QoSLevel QualityOfServiceLevel { get; }
    }
}
