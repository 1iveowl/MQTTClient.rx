namespace IMQTTClient.rx.Model
{
    public interface IMQTTMessage
    {
        string Topic { get; }

        byte[] Payload { get; }

        MQTTQoSLevel QualityOfServiceLevel { get; }

        bool Retain { get; }
    }
}
