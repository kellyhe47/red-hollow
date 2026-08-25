using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// The host-authoritative simulation (R-51). Every fixture-covered rule lives here, and only the
    /// host ever holds an instance — clients send commands in and receive replicated state out.
    ///
    /// Each public operation is one command. It returns a typed result for the caller and records
    /// its state deltas, gameplay events and external calls into <see cref="LastObservation"/>,
    /// which is what the netcode layer replicates from and what the golden fixtures grade.
    ///
    /// This type must never reference UnityEngine. GameSim.asmdef enforces that in Unity;
    /// sim/GameSim/GameSim.csproj enforces it again by building with no Unity reference at all.
    ///
    /// This file holds the shared core only — fields, constructor and recording plumbing. Each
    /// operation lives in the MatchSim.&lt;Area&gt;.cs partial owned by the ticket that implements it.
    /// </summary>
    public sealed partial class MatchSim
    {
        private readonly SimConfig _config;
        private readonly IProfileStore _profileStore;
        private readonly IClock _clock;
        private readonly IPathOracle _pathOracle;

        public MatchSim(
            MatchState state,
            SimConfig config = null,
            IProfileStore profileStore = null,
            IClock clock = null,
            IPathOracle pathOracle = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            _config = config ?? new SimConfig();
            _profileStore = profileStore ?? new InMemoryProfileStore();
            _clock = clock ?? new SimClock();
            _pathOracle = pathOracle ?? new OpenPathOracle();
        }

        public MatchState State { get; }

        public SimConfig Config => _config;

        public IClock Clock => _clock;

        /// <summary>The observation produced by the most recent command.</summary>
        public SimObservation LastObservation { get; private set; } = new SimObservation();

        // ---- recording plumbing ------------------------------------------------------------------

        private SimObservation BeginCommand()
        {
            LastObservation = new SimObservation();
            return LastObservation;
        }

        private void RecordChange(string entity, string field, object from, object to)
        {
            // Only genuine deltas are replicated — an unchanged field is not a state change.
            if (Equals(from, to))
            {
                return;
            }

            LastObservation.StateChanges.Add(new StateChange(entity, field, from, to));
        }

        private void Emit(string type, IDictionary<string, object> fields = null)
        {
            LastObservation.EmittedEvents.Add(new SimEvent(type, fields));
        }

        private void RecordExternalCall(string service, string op, IDictionary<string, object> fields = null)
        {
            LastObservation.ExternalCalls.Add(new ExternalCall(service, op, fields));
        }

        private TResult Finish<TResult>(TResult result) where TResult : ISimResult
        {
            LastObservation.Result = result.ToFields();
            return result;
        }

        private static NotImplementedException NotYet(string ticket, string behavior) =>
            new NotImplementedException(ticket + " not implemented: " + behavior);
    }
}
