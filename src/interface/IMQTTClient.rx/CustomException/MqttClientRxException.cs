using System;

namespace IMQTTClientRx.CustomException
{
    public class MqttClientRxException : Exception
    {
        public MqttClientRxException() { }
        public MqttClientRxException(string message) : base(message) { }
        public MqttClientRxException(string message, Exception inner) : base(message, inner) { }
    }
}
