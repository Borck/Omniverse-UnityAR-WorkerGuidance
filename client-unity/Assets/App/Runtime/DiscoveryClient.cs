using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Listens for UDP discovery beacons from the guidance server so the Unity
    /// client can auto-find the gRPC + HTTP endpoints without rebuilding the APK
    /// when the LAN IP changes.
    /// </summary>
    public static class DiscoveryClient
    {
        public const string ServiceName = "direkt-guidance";

        [Serializable]
        private class BeaconPayload
        {
            public string service;
            public int version;
            public string tag;
            public string host;
            public int grpc;
            public int http;
        }

        public sealed class DiscoveredServer
        {
            public string Host;
            public int GrpcPort;
            public int HttpPort;
            public string Tag;
            public string GrpcTarget => $"{Host}:{GrpcPort}";
            public string HttpBaseUrl => $"http://{Host}:{HttpPort}";
        }

        /// <summary>
        /// Listens for the first matching beacon for up to <paramref name="timeoutSeconds"/>.
        /// </summary>
        /// <param name="discoveryPort">UDP port the server broadcasts on (default 45454).</param>
        /// <param name="serviceTagFilter">Empty = accept any tag. Otherwise the beacon's tag must match exactly.</param>
        /// <param name="timeoutSeconds">How long to listen before giving up.</param>
        public static Task<DiscoveredServer> DiscoverAsync(
            int discoveryPort, string serviceTagFilter, float timeoutSeconds, CancellationToken cancellationToken)
        {
            return Task.Run(() => Discover(discoveryPort, serviceTagFilter, timeoutSeconds, cancellationToken), cancellationToken);
        }

        private static DiscoveredServer Discover(
            int discoveryPort, string serviceTagFilter, float timeoutSeconds, CancellationToken cancellationToken)
        {
            UdpClient client = null;
            var multicastLock = AcquireAndroidMulticastLock();
            try
            {
                client = new UdpClient();
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
                client.EnableBroadcast = true;
                client.Client.ReceiveTimeout = 500;

                var deadline = DateTime.UtcNow.AddSeconds(Mathf.Max(0.5f, timeoutSeconds));
                var remoteEp = new IPEndPoint(IPAddress.Any, 0);

                while (DateTime.UtcNow < deadline)
                {
                    if (cancellationToken.IsCancellationRequested) return null;

                    byte[] data;
                    try
                    {
                        data = client.Receive(ref remoteEp);
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        continue;
                    }

                    if (data == null || data.Length == 0) continue;

                    BeaconPayload beacon;
                    try
                    {
                        beacon = JsonUtility.FromJson<BeaconPayload>(Encoding.UTF8.GetString(data));
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (beacon == null) continue;
                    if (!string.Equals(beacon.service, ServiceName, StringComparison.Ordinal)) continue;
                    if (!string.IsNullOrEmpty(serviceTagFilter)
                        && !string.Equals(beacon.tag, serviceTagFilter, StringComparison.Ordinal))
                    {
                        Debug.Log($"[Discovery] Ignoring beacon with tag '{beacon.tag}' (filter='{serviceTagFilter}')");
                        continue;
                    }

                    var host = string.IsNullOrEmpty(beacon.host) ? remoteEp.Address.ToString() : beacon.host;
                    return new DiscoveredServer
                    {
                        Host = host,
                        GrpcPort = beacon.grpc > 0 ? beacon.grpc : 50051,
                        HttpPort = beacon.http > 0 ? beacon.http : 8080,
                        Tag = beacon.tag ?? string.Empty,
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Discovery] Listener failed: {ex.Message}");
                return null;
            }
            finally
            {
                client?.Close();
                ReleaseAndroidMulticastLock(multicastLock);
            }
        }

        private static object AcquireAndroidMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity");
                using var wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
                var multicastLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", "DirektGuidanceDiscovery");
                multicastLock.Call("setReferenceCounted", false);
                multicastLock.Call("acquire");
                Debug.Log("[Discovery] Android MulticastLock acquired");
                return multicastLock;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Discovery] Failed to acquire MulticastLock: {ex.Message}");
                return null;
            }
#else
            return null;
#endif
        }

        private static void ReleaseAndroidMulticastLock(object lockObj)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (lockObj is AndroidJavaObject ajo)
            {
                try { ajo.Call("release"); } catch { }
                ajo.Dispose();
            }
#endif
        }
    }
}
