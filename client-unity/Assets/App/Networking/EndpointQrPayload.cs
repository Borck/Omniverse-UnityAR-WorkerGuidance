using System;

namespace Guidance.Runtime
{
    /// <summary>Which service a scanned/typed endpoint refers to.</summary>
    public enum EndpointService
    {
        Unknown = 0,
        Grpc = 1,
        Http = 2, // FastAPI / HTTP asset+manifest bridge
    }

    /// <summary>Result of parsing one QR payload (or endpoint string).</summary>
    public readonly struct ScannedEndpoint
    {
        public readonly EndpointService Service;
        public readonly string Host;
        public readonly int Port;
        public readonly bool UseTls; // grpcs:// or https://
        public readonly bool IsValid;

        public ScannedEndpoint(EndpointService service, string host, int port, bool useTls, bool isValid)
        {
            Service = service;
            Host = host;
            Port = port;
            UseTls = useTls;
            IsValid = isValid;
        }

        public static ScannedEndpoint Invalid => new ScannedEndpoint(EndpointService.Unknown, "", 0, false, false);
    }

    /// <summary>
    /// Parses the two connection QR payloads. The scheme self-identifies the
    /// service, so the two codes can be scanned in any order:
    ///
    ///   • gRPC     → <c>grpc://HOST:PORT</c>   (or <c>grpcs://</c> for TLS)
    ///   • FastAPI  → <c>http://HOST:PORT</c>   (or <c>https://</c> for TLS)
    ///
    /// HOST may be an IPv4 address or hostname. PORT is optional; when omitted the
    /// service default is used (50051 for gRPC, 8080 for HTTP). Any path/query on
    /// the HTTP form is ignored — only host:port matters for the bridge base URL.
    ///
    /// Pure, Unity-independent, and unit-testable — no camera or Vuforia here.
    /// </summary>
    public static class EndpointQrPayload
    {
        public const int DefaultGrpcPort = 50051;
        public const int DefaultHttpPort = 8080;

        public static ScannedEndpoint Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return ScannedEndpoint.Invalid;
            var text = raw.Trim();

            EndpointService service;
            bool useTls;
            int defaultPort;
            string remainder;

            if (TryStripScheme(text, "grpcs://", out remainder)) { service = EndpointService.Grpc; useTls = true; defaultPort = DefaultGrpcPort; }
            else if (TryStripScheme(text, "grpc://", out remainder)) { service = EndpointService.Grpc; useTls = false; defaultPort = DefaultGrpcPort; }
            else if (TryStripScheme(text, "https://", out remainder)) { service = EndpointService.Http; useTls = true; defaultPort = DefaultHttpPort; }
            else if (TryStripScheme(text, "http://", out remainder)) { service = EndpointService.Http; useTls = false; defaultPort = DefaultHttpPort; }
            else return ScannedEndpoint.Invalid; // no recognised scheme → not one of our QR codes

            // Drop any path/query/fragment: keep only the authority (host[:port]).
            int cut = remainder.IndexOfAny(new[] { '/', '?', '#' });
            if (cut >= 0) remainder = remainder.Substring(0, cut);
            remainder = remainder.Trim();
            if (remainder.Length == 0) return ScannedEndpoint.Invalid;

            string host;
            int port;
            int colon = remainder.LastIndexOf(':');
            if (colon > 0)
            {
                host = remainder.Substring(0, colon).Trim();
                var portText = remainder.Substring(colon + 1).Trim();
                if (!int.TryParse(portText, out port) || port <= 0 || port > 65535)
                    return ScannedEndpoint.Invalid;
            }
            else
            {
                host = remainder;
                port = defaultPort;
            }

            if (string.IsNullOrWhiteSpace(host)) return ScannedEndpoint.Invalid;

            return new ScannedEndpoint(service, host, port, useTls, isValid: true);
        }

        private static bool TryStripScheme(string text, string scheme, out string remainder)
        {
            if (text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                remainder = text.Substring(scheme.Length);
                return true;
            }
            remainder = text;
            return false;
        }
    }
}
