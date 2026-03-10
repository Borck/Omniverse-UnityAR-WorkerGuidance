using UnityEngine;

namespace Guidance.Runtime
{
    public sealed class SessionClient
    {
        public void Initialize()
        {
            Debug.Log("[SessionClient] Initialized (gRPC stream not wired yet).");
        }
    }
}
