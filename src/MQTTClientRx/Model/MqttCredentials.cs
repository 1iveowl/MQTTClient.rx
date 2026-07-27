using System.Text;

namespace MQTTClientRx;

/// <summary>Username and password sent in the CONNECT packet.</summary>
public sealed record MqttCredentials
{
    /// <summary>Username.</summary>
    public required string UserName { get; init; }

    /// <summary>Password bytes. MQTT does not require the password to be text.</summary>
    public ReadOnlyMemory<byte> Password { get; init; }

    /// <summary>Creates credentials from a username and a text password (UTF-8 encoded).</summary>
    public static MqttCredentials Create(string userName, string password) =>
        new() { UserName = userName, Password = Encoding.UTF8.GetBytes(password) };

    /// <summary>Never includes the password.</summary>
    public override string ToString() => $"{UserName}:***";
}
