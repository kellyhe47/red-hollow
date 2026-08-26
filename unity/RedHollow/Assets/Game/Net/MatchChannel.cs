using System;
using System.Collections.Generic;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 030 — the message seam replication rides (R-50/R-51/R-52), split by side exactly as
    /// <see cref="INetTransport"/> splits bring-up: the HOST broadcasts snapshots and receives
    /// per-peer commands; a CLIENT sends commands and receives snapshots. Payloads are opaque
    /// strings at this seam (<see cref="MatchSnapshot"/> and <see cref="RemoteCommands"/> own the
    /// formats), so an implementation can be NGO custom messages, a socket, or the in-memory pair
    /// the tests drive — and none of the replication DECISIONS live anywhere a test cannot reach.
    /// </summary>
    public interface IHostMatchChannel
    {
        /// <summary>Send one snapshot to every connected client.</summary>
        void Broadcast(string snapshot);

        /// <summary>A client's command arrived: (session peer id, payload).</summary>
        event Action<string, string> CommandReceived;
    }

    /// <summary>The client half. See <see cref="IHostMatchChannel"/>.</summary>
    public interface IClientMatchChannel
    {
        /// <summary>Send one command to the host.</summary>
        void SendCommand(string payload);

        /// <summary>A host snapshot arrived.</summary>
        event Action<string> SnapshotReceived;
    }

    /// <summary>
    /// The in-memory channel pair: one host end, any number of client ends, delivery synchronous
    /// and in order. It is to the message seam what <see cref="LoopbackNetTransport"/> is to
    /// bring-up — not a test double but the honest degenerate case, and the thing every
    /// replication decision is exercised over headlessly.
    /// </summary>
    public sealed class InMemoryMatchChannel : IHostMatchChannel
    {
        private readonly List<ClientEnd> _clients = new List<ClientEnd>();

        public event Action<string, string> CommandReceived;

        /// <summary>Attach one client end speaking as <paramref name="peerId"/>.</summary>
        public IClientMatchChannel Connect(string peerId)
        {
            var client = new ClientEnd(this, peerId);
            _clients.Add(client);
            return client;
        }

        public void Broadcast(string snapshot)
        {
            foreach (var client in _clients)
            {
                client.Deliver(snapshot);
            }
        }

        private void Receive(string peerId, string payload)
        {
            var handler = CommandReceived;
            if (handler != null)
            {
                handler(peerId, payload);
            }
        }

        private sealed class ClientEnd : IClientMatchChannel
        {
            private readonly InMemoryMatchChannel _host;
            private readonly string _peerId;

            public ClientEnd(InMemoryMatchChannel host, string peerId)
            {
                _host = host;
                _peerId = peerId;
            }

            public event Action<string> SnapshotReceived;

            public void SendCommand(string payload)
            {
                _host.Receive(_peerId, payload);
            }

            public void Deliver(string snapshot)
            {
                var handler = SnapshotReceived;
                if (handler != null)
                {
                    handler(snapshot);
                }
            }
        }
    }
}
