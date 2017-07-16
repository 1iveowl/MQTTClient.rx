using System;
using System.Collections.Generic;
using System.Text;

namespace IMQTTClient.rx.Model
{
    public enum MQTTQoSLevel
    {
        AtMostOnce = 0x00,
        AtLeastOnce = 0x01,
        ExactlyOnce = 0x02
    }
}
