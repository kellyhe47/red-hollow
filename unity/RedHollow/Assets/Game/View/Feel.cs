using System;
using System.Collections.Generic;
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
        /// <summary>How long a hit flash burns. Playtest's number; "temporary" is the contract.</summary>
        private const double FlashSeconds = 0.15;

        /// <summary>How hard a landed hit shoves the view. Presentation-only, world units.</summary>
        private const float NudgeMagnitude = 0.35f;

        /// <summary>Exponential spring rate pulling the nudge back to zero per ticked second.</summary>
        private const double NudgeDecayRate = 8.0;

        /// <summary>
        /// One feel binding: the effect key the visual layer runs, the audio key the sound layer
        /// plays (null = silent binding), which event field names the target entity (null = global),
        /// and whether the event is a landed hit that flashes and nudges that target.
        /// </summary>
        private sealed class Binding
        {
            public string EffectKey;
            public string AudioKey;
            public string TargetField;
            public bool HitReaction;
        }

        /// <summary>
        /// The R-64 list, in the sim's own event-type spellings. This table IS the feel contract:
        /// the sim emits more events than these (xp_awarded, monster_killed, …) and those simply
        /// have no binding — an honest nothing, never an error.
        /// </summary>
        private static readonly Dictionary<string, Binding> Bindings = new Dictionary<string, Binding>
        {
            { "monster_damaged", new Binding { EffectKey = "fx/monster-hit-flash", AudioKey = "sfx/hit-thud", TargetField = "monster_id", HitReaction = true } },
            { "hero_damaged", new Binding { EffectKey = "fx/hero-hit-flash", AudioKey = "sfx/hero-grunt", TargetField = "hero_id", HitReaction = true } },
            { "hero_died", new Binding { EffectKey = "fx/hero-down", AudioKey = "sfx/hero-down-twang", TargetField = "hero_id" } },
            { "hero_respawned", new Binding { EffectKey = "fx/hero-respawn-shimmer", AudioKey = "sfx/respawn-chime", TargetField = "hero_id" } },
            { "civilians_killed", new Binding { EffectKey = "fx/civilian-loss-toll", AudioKey = "sfx/civilian-loss-bell", TargetField = "hotspot_id" } },
            { "hotspot_emptied", new Binding { EffectKey = "fx/hotspot-emptied-dim", AudioKey = "sfx/hotspot-emptied-drone", TargetField = "hotspot_id" } },
            { "placeable_created", new Binding { EffectKey = "fx/placeable-raise-dust", AudioKey = "sfx/hammer-clack", TargetField = "placeable_id" } },
            { "placeable_triggered", new Binding { EffectKey = "fx/placeable-trigger-snap", AudioKey = "sfx/trap-snap", TargetField = "placeable_id" } },
            { "placeable_broken", new Binding { EffectKey = "fx/trap-spent-crumble", AudioKey = "sfx/trap-spent-click", TargetField = "placeable_id" } },
            { "placeable_destroyed", new Binding { EffectKey = "fx/wall-collapse-rubble", AudioKey = "sfx/wall-collapse-crash", TargetField = "placeable_id" } },
            { "turret_fired", new Binding { EffectKey = "fx/turret-muzzle-flash", AudioKey = "sfx/turret-shot", TargetField = "placeable_id" } },
            { "status_applied", new Binding { EffectKey = "fx/status-applied-glow", AudioKey = "sfx/status-on-sizzle", TargetField = "target_id" } },
            { "status_expired", new Binding { EffectKey = "fx/status-expired-fade", AudioKey = "sfx/status-off-hiss", TargetField = "target_id" } },
            { "wave_complete", new Binding { EffectKey = "fx/wave-clear-banner", AudioKey = "stinger/wave-clear" } },
            { "combat_started", new Binding { EffectKey = "fx/wave-start-rumble", AudioKey = "stinger/wave-start" } },
            { "match_victory", new Binding { EffectKey = "fx/victory-sunrise", AudioKey = "stinger/victory-fanfare" } },
            { "match_defeat", new Binding { EffectKey = "fx/defeat-dirge", AudioKey = "stinger/defeat-dirge" } },
        };

        private readonly Dictionary<string, EntityFeelState> _feel =
            new Dictionary<string, EntityFeelState>();

        /// <summary>Whether this event type has a registered feel binding.</summary>
        public bool HasBindingFor(string eventType)
        {
            return eventType != null && Bindings.ContainsKey(eventType);
        }

        /// <summary>
        /// Route one replicated event. A bound event returns its <see cref="FeelCue"/> and updates
        /// the target's <see cref="EntityFeelState"/> (monster_damaged: flash + nudge). An unbound
        /// or null event returns null — never a throw: feel must never be able to take the session
        /// down.
        /// </summary>
        public FeelCue Route(SimEvent evt)
        {
            if (evt == null || evt.Type == null)
            {
                return null;
            }

            Binding binding;
            if (!Bindings.TryGetValue(evt.Type, out binding))
            {
                return null;
            }

            var targetId = TargetOf(evt, binding);

            if (binding.HitReaction && !string.IsNullOrEmpty(targetId))
            {
                var feel = FeelFor(targetId);
                feel.FlashSecondsRemaining = FlashSeconds;
                feel.NudgeOffset = NudgeDirectionFor(targetId) * NudgeMagnitude;
            }

            return new FeelCue
            {
                EventType = evt.Type,
                EffectKey = binding.EffectKey,
                AudioKey = binding.AudioKey,
                TargetId = targetId,
            };
        }

        /// <summary>The accumulated feel for an entity. Total: never null, neutral for the unhit.</summary>
        public EntityFeelState FeelFor(string entityId)
        {
            if (string.IsNullOrEmpty(entityId))
            {
                return new EntityFeelState();
            }

            EntityFeelState state;
            if (!_feel.TryGetValue(entityId, out state))
            {
                state = new EntityFeelState();
                _feel[entityId] = state;
            }

            return state;
        }

        /// <summary>Advance and decay every entity's feel (flash timers, nudge springs back).</summary>
        public void Tick(double deltaSeconds)
        {
            if (deltaSeconds <= 0.0)
            {
                return;
            }

            var spring = (float)Math.Exp(-NudgeDecayRate * deltaSeconds);

            foreach (var state in _feel.Values)
            {
                state.FlashSecondsRemaining = Math.Max(0.0, state.FlashSecondsRemaining - deltaSeconds);
                state.NudgeOffset *= spring;
            }
        }

        /// <summary>The entity the event names, off the binding's field. Missing field → no target.</summary>
        private static string TargetOf(SimEvent evt, Binding binding)
        {
            if (binding.TargetField == null || evt.Fields == null)
            {
                return null;
            }

            object value;
            if (!evt.Fields.TryGetValue(binding.TargetField, out value))
            {
                return null;
            }

            return value as string;
        }

        /// <summary>
        /// A stable horizontal shove direction per entity. Direction is deliberately free of the
        /// contract (playtest's); deriving it from the id keeps the router deterministic without
        /// the event having to carry attacker geometry.
        /// </summary>
        private static Vector3 NudgeDirectionFor(string entityId)
        {
            var angle = (entityId.GetHashCode() & 0xFFFF) * (Mathf.PI * 2f / 0x10000);
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
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
            if (view == null)
            {
                return;
            }

            var offset = feel != null ? feel.NudgeOffset : Vector3.zero;
            view.transform.position = view.WorldPosition + offset;
            var flashing = feel != null && feel.IsFlashing;
            ApplyFlashTint(view, flashing);
            ApplyHitBurst(view, flashing);
        }

        private static void ApplyFlashTint(MonsterView view, bool flashing)
        {
            var tint = flashing ? new Color(1.6f, 0.42f, 0.16f) : Color.white;
            var renderers = view.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].gameObject.name != "fx_hit_burst")
                {
                    ViewLook.TintBlock(renderers[i], tint);
                }
            }
        }

        private static void ApplyHitBurst(MonsterView view, bool flashing)
        {
            var t = view.transform.Find("fx_hit_burst");
            if (t == null)
            {
                if (!flashing)
                {
                    return;
                }

                var burst = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                burst.name = "fx_hit_burst";
                burst.transform.SetParent(view.transform, false);
                burst.transform.localPosition = new Vector3(0f, 1.9f, 0f);
                burst.transform.localScale = Vector3.one * 1.25f;
                ViewLook.StripCollider(burst);
                ViewLook.Paint(burst, ViewLook.Unlit(new Color(1f, 0.30f, 0.08f)));
                return;
            }

            t.gameObject.SetActive(flashing);
            if (flashing)
            {
                t.localScale = Vector3.one * 1.25f;
            }
        }
    }
}
