using System;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Ticket 013 (T-13) — R-64. One routed feel effect: which binding fired, the cue key the audio
    /// layer plays (wave stingers, western-twang UI), the effect key the visual layer runs, and the
    /// entity the effect lands on when the event names one. Pure data — whether a sound actually
    /// comes out of a speaker is playtest's question; the binding/cue-key layer is the testable one.
    /// </summary>
    public sealed class FeelCue
    {
        /// <summary>The sim event type this cue answered.</summary>
        public string EventType;

        /// <summary>
        /// The visual/feel effect this binding runs. Distinct per binding — the sim distinguishes
        /// <c>placeable_broken</c> from <c>placeable_destroyed</c> deliberately, and a router that
        /// funnels two events into one effect erases a distinction the sim went out of its way to
        /// keep.
        /// </summary>
        public string EffectKey;

        /// <summary>Audio cue key (stinger, twang). Null when the binding is silent.</summary>
        public string AudioKey;

        /// <summary>The entity the effect lands on (monster_id / placeable_id / …), when any.</summary>
        public string TargetId;
    }

    /// <summary>
    /// Ticket 013 (T-13) — R-64. Presentation-only feel accumulated on one entity: the hit flash
    /// and the knockback nudge. The nudge is a VIEW OFFSET and nothing more — it never touches the
    /// sim-authoritative position, which stays exactly where replication put it. Pure data.
    /// </summary>
    public sealed class EntityFeelState
    {
        /// <summary>Seconds of hit flash left. Zero or less means not flashing.</summary>
        public double FlashSecondsRemaining;

        /// <summary>World-space presentation offset for the knockback nudge. Decays back to zero.</summary>
        public Vector3 NudgeOffset;

        public bool IsFlashing
        {
            get { return FlashSecondsRemaining > 0.0; }
        }
    }

    /// <summary>
    /// Ticket 013 (T-13) — R-64, the feel router: plain C# mapping from replicated
    /// <see cref="SimEvent"/>s to feel effects. Every R-64 event has a registered binding, so a
    /// renamed or missed event fails loudly in a test instead of silently losing its juice. Plain
    /// C# on purpose: T10's IL invariant scans MonoBehaviours, and feel must never be a place where
    /// a MonoBehaviour learns to hold sim references or write sim state.
    /// </summary>
    public sealed class FeelRouter
    {
        /// <summary>Whether this event type has a registered feel binding.</summary>
        public bool HasBindingFor(string eventType)
        {
            throw new NotImplementedException("ticket 013: FeelRouter.HasBindingFor");
        }

        /// <summary>
        /// Route one replicated event. A bound event returns its <see cref="FeelCue"/> and updates
        /// the target's <see cref="EntityFeelState"/> (monster_damaged: flash + nudge). An unbound
        /// or null event returns null — never a throw: feel must never be able to take the session
        /// down.
        /// </summary>
        public FeelCue Route(SimEvent evt)
        {
            throw new NotImplementedException("ticket 013: FeelRouter.Route");
        }

        /// <summary>The accumulated feel for an entity. Total: never null, neutral for the unhit.</summary>
        public EntityFeelState FeelFor(string entityId)
        {
            throw new NotImplementedException("ticket 013: FeelRouter.FeelFor");
        }

        /// <summary>Advance and decay every entity's feel (flash timers, nudge springs back).</summary>
        public void Tick(double deltaSeconds)
        {
            throw new NotImplementedException("ticket 013: FeelRouter.Tick");
        }
    }

    /// <summary>
    /// Ticket 013 (T-13) — applies accumulated feel to a view, the same shape as
    /// <see cref="ViewRig"/>: plain and static so shared presentation code lives where it cannot
    /// reach sim state. The one rule: feel offsets the TRANSFORM, never the authoritative position
    /// — <see cref="MonsterView.WorldPosition"/> stays the sim's answer, and the nudge rides on top.
    /// </summary>
    public static class FeelRig
    {
        /// <summary>
        /// Place the view at its authoritative position plus the feel's presentation offset. Call
        /// after <see cref="MonsterView.RenderFrom"/>; with neutral feel the view stands exactly at
        /// <see cref="MonsterView.WorldPosition"/> again.
        /// </summary>
        public static void Apply(MonsterView view, EntityFeelState feel)
        {
            throw new NotImplementedException("ticket 013: FeelRig.Apply");
        }
    }
}
