namespace MQTTClientRx;

/// <summary>
/// A key/value pair carried alongside an MQTT packet. MQTT 5.0 only - user properties are silently
/// unavailable on 3.1.1 and 3.1.0.
/// </summary>
/// <param name="Name">Property name. Not required to be unique.</param>
/// <param name="Value">Property value.</param>
public readonly record struct MqttUserProperty(string Name, string Value)
{
    /// <inheritdoc />
    public override string ToString() => $"{Name}={Value}";
}
