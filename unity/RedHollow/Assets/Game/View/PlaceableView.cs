using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Ticket 026 (T-26) — one placeable's presentation, driven entirely from replicated sim state
    /// (R-51), on exactly the shape <see cref="MonsterView"/> locked: <c>Bind</c> ties the
    /// component to one replicated id and its visual, <c>RenderFrom</c> mirrors and never decides.
    ///
    /// The one addition over the monster shape is the damage readout (wireframe S4: "Barricades
    /// show HP bars when damaged"): <see cref="FullHp"/> is the R-23 catalog's MaxHp for the row,
    /// handed in at bind time because <see cref="Placeable"/> carries no full-HP column and a view
    /// must not reach into config; <see cref="DamageIndicatorVisible"/> and
    /// <see cref="HpFraction"/> are the observable state the indicator presentation hangs off.
    /// </summary>
    public sealed class PlaceableView : MonoBehaviour
    {
        public string PlaceableId { get; private set; }

        public VisualHandle Visual { get; private set; }

        public Vector3 WorldPosition { get; private set; }

        /// <summary>Exactly what the sim says this placeable's HP is. Not a clamp, not a rule.</summary>
        public double DisplayedHp { get; private set; }

        /// <summary>The R-23 catalog full-HP denominator this view was bound with.</summary>
        public double FullHp { get; private set; }

        /// <summary>
        /// Wireframe S4 — true exactly when the sim says the placeable is below its catalog full
        /// HP. Presence is contract; the shape of the indicator is presentation.
        /// </summary>
        public bool DamageIndicatorVisible { get; private set; }

        /// <summary>
        /// The displayed remaining-HP fraction in [0, 1] — monotone increasing in the sim's Hp.
        /// Exact mapping is presentation; monotonicity and range are contract. The clamp lives
        /// here and not on <see cref="DisplayedHp"/>: the raw HP stays the sim's verbatim answer,
        /// only the FRACTION (a presentation quantity) is bounded to its own definition.
        /// </summary>
        public double HpFraction { get; private set; }

        /// <summary>
        /// Ties this component to one replicated placeable id, the visual it wears and the R-23
        /// catalog denominator its damage readout divides by. The visual is parented here so the
        /// two share a lifetime — a view destroyed on removal must not leave its stand-in standing.
        /// </summary>
        public void Bind(string placeableId, VisualHandle visual, double fullHp)
        {
            PlaceableId = placeableId;
            Visual = visual;
            FullHp = fullHp;
            ViewRig.Attach(transform, visual);
        }

        /// <summary>
        /// R-51 — copy this frame's replicated values out of the world. Read-only by construction:
        /// every assignment below writes a property of this component, never a field of the sim.
        /// An unknown id is a no-op rather than an error (T16's rule): a view that outlives its
        /// placeable by a frame keeps showing its last replicated values instead of throwing.
        /// </summary>
        public void RenderFrom(MatchState state)
        {
            if (state == null || string.IsNullOrEmpty(PlaceableId))
            {
                return;
            }

            Placeable placeable;
            if (!state.Placeables.TryGetValue(PlaceableId, out placeable) || placeable == null)
            {
                return;
            }

            DisplayedHp = placeable.Hp;
            WorldPosition = SimSpace.ToWorld(placeable.Pos);
            transform.position = WorldPosition;

            if (FullHp > 0.0)
            {
                var fraction = placeable.Hp / FullHp;
                HpFraction = fraction < 0.0 ? 0.0 : (fraction > 1.0 ? 1.0 : fraction);
                DamageIndicatorVisible = placeable.Hp < FullHp;
            }
            else
            {
                // No known denominator (no catalog wired) — there is no fraction to show, and an
                // indicator over an unknown full would be a made-up number.
                HpFraction = 0.0;
                DamageIndicatorVisible = false;
            }
        }
    }
}
