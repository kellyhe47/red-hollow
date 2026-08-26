using System;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 020 (T-20) — the one place the shell decides which transport a session rides (R-50).
    ///
    /// The rule is the acceptance criterion itself: <b>no UGS project id means loopback</b>, and
    /// the loopback path must not touch Unity services AT ALL — not sign in, not allocate, not
    /// even ask — because a machine with no cloud project linked is a shipped configuration
    /// (solo is a 1-player lobby), not a degraded one. A config carrying a project id means the
    /// NGO + Lobby + Relay transport, built on the seams but touching nothing until it is started:
    /// choosing a transport is not yet a reason to authenticate.
    /// </summary>
    public static class NetTransportFactory
    {
        /// <summary>
        /// Choose and construct the transport for this config: loopback when
        /// <see cref="NetSessionConfig.UgsProjectId"/> is null or empty (or the config itself is
        /// null), <see cref="NgoNetTransport"/> otherwise. Construction is passive either way —
        /// the services seam is not called until a host start or client join asks for it.
        /// </summary>
        public static INetTransport Create(
            NetSessionConfig config, IUgsServices services, INetWire wire) =>
            throw new NotImplementedException("ticket 020");
    }
}
