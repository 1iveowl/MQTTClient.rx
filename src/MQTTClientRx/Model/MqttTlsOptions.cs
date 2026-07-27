using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace MQTTClientRx;

/// <summary>TLS configuration for the broker connection.</summary>
public sealed record MqttTlsOptions
{
    /// <summary>Whether TLS is used. Defaults to <see langword="true"/>, since supplying this record at all implies intent.</summary>
    public bool UseTls { get; init; } = true;

    /// <summary>Accept certificates that do not chain to a trusted root. Test use only.</summary>
    public bool AllowUntrustedCertificates { get; init; }

    /// <summary>Ignore errors validating the certificate chain. Test use only.</summary>
    public bool IgnoreCertificateChainErrors { get; init; }

    /// <summary>Ignore failures to check certificate revocation. Test use only.</summary>
    public bool IgnoreCertificateRevocationErrors { get; init; }

    /// <summary>Expected host name on the server certificate. Defaults to the host in the connection URI.</summary>
    public string? TargetHost { get; init; }

    /// <summary>TLS protocol versions to offer. <see cref="SslProtocols.None"/> lets the OS choose.</summary>
    public SslProtocols SslProtocols { get; init; } = SslProtocols.None;

    /// <summary>
    /// Whether the certificate chain is checked for revocation.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="X509RevocationMode.NoCheck"/>, matching <c>SslStream</c> and
    /// <c>HttpClient</c>. MQTTnet's own default is <see cref="X509RevocationMode.Online"/>, which
    /// rejects otherwise valid certificates whenever the OCSP responder cannot be reached - on macOS
    /// that is common enough to make a publicly trusted broker look untrusted. Set it back to
    /// <see cref="X509RevocationMode.Online"/> when revocation genuinely matters and the responder
    /// is reachable.
    /// </remarks>
    public X509RevocationMode RevocationMode { get; init; } = X509RevocationMode.NoCheck;

    /// <summary>Client certificates presented for mutual TLS.</summary>
    public IReadOnlyList<X509Certificate2> ClientCertificates { get; init; } = [];

    /// <summary>Additional roots to trust when validating the server certificate.</summary>
    public X509Certificate2Collection? TrustChain { get; init; }

    /// <summary>
    /// Custom server certificate validation. Return <see langword="true"/> to accept. When set, this
    /// takes precedence over <see cref="AllowUntrustedCertificates"/> and the ignore-error flags.
    /// </summary>
    public Func<X509Certificate?, X509Chain?, SslPolicyErrors, bool>? CertificateValidationHandler { get; init; }

    /// <summary>
    /// TLS that accepts any server certificate. Convenient against test brokers with self-signed
    /// certificates; never appropriate in production.
    /// </summary>
    public static MqttTlsOptions Insecure { get; } = new()
    {
        UseTls = true,
        AllowUntrustedCertificates = true,
        IgnoreCertificateChainErrors = true,
        IgnoreCertificateRevocationErrors = true
    };
}
