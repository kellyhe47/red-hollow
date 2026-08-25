using System;
using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>What kind of thing a visual stands for. Each class has its own placeholder shape.</summary>
    public enum VisualClass
    {
        Ground,
        Hero,
        Monster,
        Placeable,
        Hotspot,
    }

    /// <summary>
    /// A resolved visual: the object that was actually instantiated, plus whether it is the real
    /// art or the primitive stand-in. <see cref="IsPlaceholder"/> is public because "did the art
    /// resolve?" must be an observable answer rather than a silent difference — a missing asset
    /// that renders as nothing is the failure mode this ticket exists to make impossible.
    /// </summary>
    public sealed class VisualHandle
    {
        public GameObject Instance;

        /// <summary>True when this is the primitive stand-in rather than the authored art.</summary>
        public bool IsPlaceholder;

        public VisualClass Class;

        /// <summary>The art that was asked for. Null, empty or unknown all resolve to a placeholder.</summary>
        public string ArtKey;
    }

    /// <summary>
    /// The asset seam. Every visual in the shell comes through here, and the contract is total:
    /// <see cref="Resolve"/> returns a usable <see cref="VisualHandle"/> for any input, including a
    /// null or unknown <c>artKey</c>. It never returns null and never throws.
    ///
    /// That totality is the point of the seam and not a convenience: ticket 013 wires the real
    /// art in <c>art/</c>, and no gameplay ticket may be blocked waiting for it. A resolver that
    /// can fail turns "the art is not ready yet" into "the game does not run".
    /// </summary>
    public interface IVisualResolver
    {
        VisualHandle Resolve(VisualClass visualClass, string artKey);
    }

    /// <summary>
    /// The two things every view does with the visual it was handed, in one place so a hero and a
    /// monster cannot drift apart on either.
    ///
    /// Plain and static rather than a base component: the views are MonoBehaviours, and R-51's IL
    /// invariant is easier to keep honest when shared code is somewhere it cannot reach sim state
    /// at all.
    /// </summary>
    public static class ViewRig
    {
        /// <summary>
        /// Parent a resolved visual under the view that owns it, keeping the resolver's own local
        /// offset (a capsule stands on the floor rather than half inside it). Shared parentage is
        /// also shared lifetime — a despawned view takes its stand-in with it.
        /// </summary>
        public static void Attach(Transform owner, VisualHandle visual)
        {
            if (owner == null || visual == null || visual.Instance == null)
            {
                return;
            }

            visual.Instance.transform.SetParent(owner, false);
        }

        /// <summary>
        /// Show or hide a visual. <paramref name="visible"/> is always a value the sim decided
        /// (R-51) — the view passes the answer through and applies no rule of its own to it.
        /// </summary>
        public static void SetVisible(VisualHandle visual, bool visible)
        {
            if (visual == null || visual.Instance == null)
            {
                return;
            }

            if (visual.Instance.activeSelf != visible)
            {
                visual.Instance.SetActive(visible);
            }
        }
    }

    /// <summary>
    /// The ticket-016 resolver: primitive placeholder art for everything, whatever is asked for.
    /// It is what makes a solo session playable before a single asset is wired.
    ///
    /// Deliberately unconditional. It does not probe for the asset first and fall back, because a
    /// probe is a code path that can answer "absent" — and every such path is somewhere the shell
    /// could learn to block on art. Ticket 013 adds the real lookup *in front of* this fallback;
    /// until then the honest answer for every key is "placeholder", and the handle says so.
    ///
    /// The shapes below are taste, not contract: nothing in the PRD says a hero stand-in is a
    /// capsule, and the tests assert only that something renders.
    /// </summary>
    public sealed class PlaceholderVisualResolver : IVisualResolver
    {
        /// <summary>
        /// Never null, never throws, never a skipped render (R-30's delivery constraint).
        /// <paramref name="artKey"/> is recorded on the handle rather than acted on, so a later
        /// ticket can see which key went unresolved without this one pretending to load it.
        /// </summary>
        public VisualHandle Resolve(VisualClass visualClass, string artKey)
        {
            var instance = CreatePlaceholder(visualClass);

            return new VisualHandle
            {
                Instance = instance,
                IsPlaceholder = true,
                Class = visualClass,
                ArtKey = artKey,
            };
        }

        /// <summary>
        /// A visible primitive for the class. <see cref="GameObject.CreatePrimitive"/> is the happy
        /// path; the catch is not defensive habit but the seam's contract — this method has no
        /// permission to fail, so an engine that refuses a primitive still has to yield something
        /// with a <see cref="Renderer"/> on it.
        /// </summary>
        private static GameObject CreatePlaceholder(VisualClass visualClass)
        {
            var name = "placeholder_" + visualClass.ToString().ToLowerInvariant();

            try
            {
                var primitive = GameObject.CreatePrimitive(PrimitiveFor(visualClass));
                primitive.name = name;
                primitive.transform.localPosition = StandingOffsetFor(visualClass);
                return primitive;
            }
            catch (Exception)
            {
                return BareRenderable(name);
            }
        }

        private static PrimitiveType PrimitiveFor(VisualClass visualClass)
        {
            switch (visualClass)
            {
                case VisualClass.Ground:
                    return PrimitiveType.Plane;

                case VisualClass.Hero:
                case VisualClass.Monster:
                    return PrimitiveType.Capsule;

                case VisualClass.Hotspot:
                    return PrimitiveType.Cylinder;

                default:
                    return PrimitiveType.Cube;
            }
        }

        /// <summary>
        /// How far up the primitive sits so it stands on the floor instead of sinking half into it.
        /// Presentation only — every position assertion in this ticket is horizontal, and the
        /// vertical axis is the one <see cref="SimSpace"/> leaves free for exactly this.
        /// </summary>
        private static Vector3 StandingOffsetFor(VisualClass visualClass)
        {
            switch (visualClass)
            {
                case VisualClass.Ground:
                    return Vector3.zero;

                case VisualClass.Hero:
                case VisualClass.Monster:
                case VisualClass.Hotspot:
                    return new Vector3(0f, 1f, 0f);

                default:
                    return new Vector3(0f, 0.5f, 0f);
            }
        }

        /// <summary>
        /// The last resort: an object that renders nothing recognisable but still renders, so the
        /// caller's "there is a visual here" stays true. Better than a null the whole shell then has
        /// to branch on.
        /// </summary>
        private static GameObject BareRenderable(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            return go;
        }
    }
}
