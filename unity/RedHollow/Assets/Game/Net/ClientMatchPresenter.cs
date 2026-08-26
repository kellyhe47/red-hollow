using System;
using RedHollow.Game.Input;
using RedHollow.Sim;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 030 — the CLIENT side of a remote party seat (R-50/R-51/R-52): holds the mirror
    /// <see cref="MatchState"/> the host's snapshots rebuild, and turns this machine's sampled
    /// input into the wire commands <see cref="RemotePartyDriver"/> applies on the host.
    ///
    /// The mirror is a PICTURE (R-51): no <see cref="MatchSim"/> ever holds it, nothing here
    /// issues a sim command, and every write to it comes from <see cref="MatchSnapshot.Apply"/>.
    /// What this class decides is client-side only: which snapshot to show (the latest), what
    /// input state to send (the same held-keys shape the local shell samples), and when a cast
    /// edge fires (press edges are client truth — the host cannot see a keyboard).
    ///
    /// Plain C# — rendering hangs off the mirror through whatever binder the caller syncs, and
    /// the whole decision surface runs in the headless suite.
    /// </summary>
    public sealed class ClientMatchPresenter
    {
        private readonly IClientMatchChannel _channel;
        private readonly IInputSource _input;
        private readonly DefaultHeroInputMap _map = new DefaultHeroInputMap();

        private string _latestSnapshot;
        private bool _qWasDown;
        private bool _eWasDown;

        /// <summary>The mirror world, rebuilt from the latest snapshot each pump.</summary>
        public MatchState Mirror { get; }

        /// <summary>R-63 — the host-computed planning countdown carried by the latest snapshot.</summary>
        public double PlanningRemainingSeconds { get; private set; }

        /// <summary>Whether any snapshot has arrived yet (a joiner shows "connecting…" until one has).</summary>
        public bool Live { get; private set; }

        /// <param name="channel">The wire to the host.</param>
        /// <param name="input">This machine's device, or null for a render-only observer.</param>
        /// <param name="map">
        /// The colony both sides build from (null = the shipped <see cref="ColonyMap.V1"/>); the
        /// mirror starts as its fresh state so hotspot geometry exists before the first snapshot.
        /// </param>
        public ClientMatchPresenter(
            IClientMatchChannel channel, IInputSource input = null, ColonyMap map = null)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            _channel = channel;
            _input = input;

            Mirror = (map ?? ColonyMap.V1()).CreateMatchState(new SimConfig());
            Mirror.Phase = MatchPhase.Combat;

            channel.SnapshotReceived += snapshot => _latestSnapshot = snapshot;
        }

        /// <summary>
        /// One client frame: apply the latest snapshot (older ones are superseded — R-52's mirror
        /// needs the present, not the journey), then sample and send this machine's held input.
        /// </summary>
        public void Pump(double deltaSeconds)
        {
            var snapshot = _latestSnapshot;
            if (snapshot != null)
            {
                _latestSnapshot = null;
                PlanningRemainingSeconds = MatchSnapshot.Apply(snapshot, Mirror);
                Live = true;
            }

            SendHeldInput();
        }

        /// <summary>S3 planning clicks, exposed for the client's UI wiring.</summary>
        public void RequestPurchase(string placeableType, Vec2 pos)
        {
            _channel.SendCommand(RemoteCommands.BuyAt(placeableType, pos.X, pos.Y));
        }

        public void RequestSell(string placeableId)
        {
            _channel.SendCommand(RemoteCommands.SellPlaceable(placeableId));
        }

        public void ReadyUp()
        {
            _channel.SendCommand(RemoteCommands.ReadyUp());
        }

        private void SendHeldInput()
        {
            if (_input == null)
            {
                return;
            }

            var snapshot = _input.Sample();
            if (snapshot == null)
            {
                return;
            }

            var intent = _map.Resolve(snapshot);

            // Press EDGES are client truth (T-25's shape): the host sees a cast token once.
            var qDown = snapshot.Pressed.Contains(PlayerKey.Q);
            var eDown = snapshot.Pressed.Contains(PlayerKey.E);
            string cast = null;
            if (qDown && !_qWasDown)
            {
                cast = AbilitySlot.Q;
            }
            else if (eDown && !_eWasDown)
            {
                cast = AbilitySlot.E;
            }

            _qWasDown = qDown;
            _eWasDown = eDown;

            _channel.SendCommand(RemoteCommands.InputState(
                intent.MoveDirection.x,
                intent.MoveDirection.y,
                snapshot.CursorGroundPoint.x,
                snapshot.CursorGroundPoint.y,
                snapshot.Pressed.Contains(PlayerKey.Space),
                cast));
        }
    }
}
