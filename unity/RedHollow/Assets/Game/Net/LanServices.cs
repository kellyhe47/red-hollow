using System;
using System.Globalization;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 030 — a direct-connection endpoint: an address and a port instead of a Relay
    /// allocation. <see cref="NgoWire"/> hands it to UnityTransport as plain connection data, so
    /// "NGO on loopback" (two builds on one machine, or a LAN) needs no cloud project at all.
    /// </summary>
    public sealed class LocalEndpoint : RelayEndpoint
    {
        public LocalEndpoint(string address, ushort port)
        {
            Address = string.IsNullOrEmpty(address) ? "127.0.0.1" : address;
            Port = port;
        }

        /// <summary>Where the host binds / the client connects. Defaults to loopback.</summary>
        public string Address { get; }

        public ushort Port { get; }
    }

    /// <summary>
    /// Ticket 030 — <see cref="IUgsServices"/> with no cloud behind it (R-50's "no UGS id is
    /// required" carried one layer further): "allocating Relay" answers a <see cref="LocalEndpoint"/>,
    /// "the lobby" is a fixed code naming the address to dial, and heartbeats are no-ops because
    /// there is no service to idle out.
    ///
    /// Why an <see cref="IUgsServices"/> implementation rather than a second transport:
    /// <see cref="NgoNetTransport"/> already owns the bring-up ORDER, the join-refusal reading and
    /// the teardown discipline that T-20 locked — a LAN path that re-implemented them would drift
    /// from the pinned behaviour one edit at a time. This class swaps the SERVICE and keeps every
    /// decision above it exactly as tested.
    ///
    /// The join code is the dial string: <c>LAN</c> joins the default loopback port, and
    /// <c>LAN:192.168.0.12:7777</c> joins a machine across the room. No cloud registry exists to
    /// resolve pretty codes, so the code IS the address — which a party on one couch can read off
    /// the host's S2 screen exactly like a Relay code.
    /// </summary>
    public sealed class LanServices : IUgsServices
    {
        public const string CodePrefix = "LAN";
        public const ushort DefaultPort = 7777;

        private readonly string _hostAddress;
        private readonly ushort _port;

        /// <param name="hostAddress">
        /// The address a HOST advertises in its join code (loopback by default; a LAN host passes
        /// its interface address so the code works from other machines).
        /// </param>
        /// <param name="port">The port the host binds.</param>
        public LanServices(string hostAddress = null, ushort port = DefaultPort)
        {
            _hostAddress = string.IsNullOrEmpty(hostAddress) ? "127.0.0.1" : hostAddress;
            _port = port;
        }

        /// <summary>There is no sign-in; the answer that keeps the transport from retrying is "done".</summary>
        public bool IsSignedIn => true;

        public void SignIn(string projectId)
        {
            // Nothing to authenticate against. Deliberately not a throw: the transport signs in
            // once before hosting or joining, and a LAN session must sail through that step.
        }

        public RelayHostSlot AllocateRelay(int maxConnections)
        {
            return new RelayHostSlot
            {
                RelayJoinCode = Dial(_hostAddress, _port),
                Endpoint = new LocalEndpoint(_hostAddress, _port),
            };
        }

        public RelayJoinSlot JoinRelay(string relayJoinCode)
        {
            return new RelayJoinSlot { Endpoint = Parse(relayJoinCode) };
        }

        public LobbyTicket CreateLobby(int maxPlayers, string relayJoinCode)
        {
            // The lobby IS the dial string: one code on S2, no registry behind it.
            return new LobbyTicket
            {
                LobbyId = "lan_lobby",
                JoinCode = relayJoinCode,
                RelayJoinCode = relayJoinCode,
            };
        }

        public LobbyTicket JoinLobbyByCode(string joinCode)
        {
            // A code this class cannot parse is a bad code — the same JoinLobby refusal shape the
            // cloud path answers with, so S1's inline error works unchanged (T-12/T-20).
            var endpoint = TryParse(joinCode);
            if (endpoint == null)
            {
                throw new UgsUnavailableException(
                    UgsStep.JoinLobby, "not a LAN join code: '" + joinCode + "'");
            }

            return new LobbyTicket
            {
                LobbyId = "lan_lobby",
                JoinCode = joinCode,
                RelayJoinCode = joinCode,
            };
        }

        public void HeartbeatLobby(string lobbyId)
        {
            // No service holds the lobby, so nothing idles it out.
        }

        public void LeaveLobby(string lobbyId)
        {
        }

        /// <summary>"LAN" for the defaults; "LAN:address:port" otherwise.</summary>
        private static string Dial(string address, ushort port)
        {
            if (address == "127.0.0.1" && port == DefaultPort)
            {
                return CodePrefix;
            }

            return CodePrefix + ":" + address + ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        private RelayEndpoint Parse(string code)
        {
            var endpoint = TryParse(code);
            if (endpoint == null)
            {
                throw new UgsUnavailableException(
                    UgsStep.JoinRelay, "not a LAN join code: '" + code + "'");
            }

            return endpoint;
        }

        private LocalEndpoint TryParse(string code)
        {
            if (code == null)
            {
                return null;
            }

            var trimmed = code.Trim();
            if (string.Equals(trimmed, CodePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return new LocalEndpoint("127.0.0.1", DefaultPort);
            }

            if (!trimmed.StartsWith(CodePrefix + ":", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parts = trimmed.Split(':');
            if (parts.Length != 3
                || string.IsNullOrEmpty(parts[1])
                || !ushort.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
            {
                return null;
            }

            return new LocalEndpoint(parts[1], port);
        }
    }
}
