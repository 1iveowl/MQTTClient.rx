using System;
using System.Collections.Generic;
using System.Text;
using IMQTTClientRx.Model;

namespace Test.Client.Core.Model
{
    internal class TopicFilter : ITopicFilter
    {
        public string Topic { get; internal set; }
        public QoSLevel QualityOfServiceLevel { get; internal set; }
    }
}
