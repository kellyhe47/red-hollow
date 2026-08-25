using System.Collections.Generic;
using System.Linq;

namespace RedHollow.Sim
{
    /// <summary>
    /// A single replicated field delta. The host emits these to drive client state; the golden
    /// adapter reads the same stream as its `state_changes` observation surface.
    /// </summary>
    public sealed class StateChange
    {
        public readonly string Entity;
        public readonly string Field;
        public readonly object From;
        public readonly object To;

        public StateChange(string entity, string field, object from, object to)
        {
            Entity = entity;
            Field = field;
            From = from;
            To = to;
        }

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "entity", Entity },
            { "field", Field },
            { "from", From },
            { "to", To },
        };
    }

    /// <summary>
    /// A gameplay event broadcast to clients (hit flashes, toasts, stingers all hang off these).
    /// `Type` is the discriminator the manifest canonicalizes on.
    /// </summary>
    public sealed class SimEvent
    {
        public readonly string Type;
        public readonly IDictionary<string, object> Fields;

        public SimEvent(string type, IDictionary<string, object> fields = null)
        {
            Type = type;
            Fields = fields ?? new Dictionary<string, object>();
        }

        public IDictionary<string, object> ToFields()
        {
            var result = new Dictionary<string, object> { { "type", Type } };
            foreach (var kv in Fields)
            {
                result[kv.Key] = kv.Value;
            }

            return result;
        }
    }

    /// <summary>A call the sim made across an injected boundary (today: the profile store).</summary>
    public sealed class ExternalCall
    {
        public readonly string Service;
        public readonly string Op;
        public readonly IDictionary<string, object> Fields;

        public ExternalCall(string service, string op, IDictionary<string, object> fields = null)
        {
            Service = service;
            Op = op;
            Fields = fields ?? new Dictionary<string, object>();
        }

        public IDictionary<string, object> ToFields()
        {
            var result = new Dictionary<string, object> { { "service", Service }, { "op", Op } };
            foreach (var kv in Fields)
            {
                result[kv.Key] = kv.Value;
            }

            return result;
        }
    }

    /// <summary>
    /// Everything one command produced: its return value plus the three side-effect streams.
    /// This is production machinery — the host replicates from it — and it doubles as the
    /// observation record the golden fixtures grade.
    /// </summary>
    public sealed class SimObservation
    {
        public IDictionary<string, object> Result { get; internal set; }

        public List<StateChange> StateChanges { get; } = new List<StateChange>();

        public List<SimEvent> EmittedEvents { get; } = new List<SimEvent>();

        public List<ExternalCall> ExternalCalls { get; } = new List<ExternalCall>();

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "result", Result },
            { "state_changes", StateChanges.Select(c => c.ToFields()).ToList() },
            { "emitted_events", EmittedEvents.Select(e => e.ToFields()).ToList() },
            { "external_calls", ExternalCalls.Select(c => c.ToFields()).ToList() },
        };
    }

    /// <summary>Typed command results expose their replicated shape through this.</summary>
    public interface ISimResult
    {
        IDictionary<string, object> ToFields();
    }
}
