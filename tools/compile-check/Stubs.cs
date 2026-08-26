// Minimal stand-ins for the Unity-side types the HOST layer touches, so the host-layer sources
// can be compile-checked under dotnet on a VM with no Unity editor. Never shipped, never tested —
// syntax/type verification only.

namespace UnityEngine
{
    public static class Time
    {
        public static float timeScale = 1f;
    }

    public static class Debug
    {
        public static void Log(object message)
        {
            System.Console.WriteLine(message);
        }
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2 zero => new Vector2(0f, 0f);
        public float sqrMagnitude => (x * x) + (y * y);
        public Vector2 normalized
        {
            get
            {
                var m = (float)System.Math.Sqrt(sqrMagnitude);
                return m > 0f ? new Vector2(x / m, y / m) : zero;
            }
        }
    }
}

namespace RedHollow.Game.View
{
    public sealed class MatchViewBinder
    {
        public void Sync(RedHollow.Sim.MatchState state)
        {
        }
    }
}

namespace RedHollow.Game.Net
{
    // The real INetWire lives in NgoWire.cs beside Unity.Netcode types; only its shape matters here.
    public interface INetWire
    {
        bool IsUp { get; }
        void StartHost(RelayEndpoint endpoint);
        void StartClient(RelayEndpoint endpoint);
        void Shutdown();
        event System.Action<string> PeerDisconnected;
    }
}
