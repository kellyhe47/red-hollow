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
        public string PlaceableId
        {
            get { throw new System.NotImplementedException("T26: placeable views"); }
        }

        public VisualHandle Visual
        {
            get { throw new System.NotImplementedException("T26: placeable views"); }
        }

        public Vector3 WorldPosition
        {
            get { throw new System.NotImplementedException("T26: placeable views"); }
        }

        /// <summary>Exactly what the sim says this placeable's HP is. Not a clamp, not a rule.</summary>
        public double DisplayedHp
        {
            get { throw new System.NotImplementedException("T26: placeable views"); }
        }

        /// <summary>The R-23 catalog full-HP denominator this view was bound with.</summary>
        public double FullHp
        {
            get { throw new System.NotImplementedException("T26: placeable views"); }
        }

        /// <summary>
        /// Wireframe S4 — true exactly when the sim says the placeable is below its catalog full
        /// HP. Presence is contract; the shape of the indicator is presentation.
        /// </summary>
        public bool DamageIndicatorVisible
        {
            get { throw new System.NotImplementedException("T26: placeable views"); }
        }

        /// <summary>
        /// The displayed remaining-HP fraction in [0, 1] — monotone increasing in the sim's Hp.
        /// Exact mapping is presentation; monotonicity and range are contract.
        /// </summary>
        public double HpFraction
        {
            get { throw new System.NotImplementedException("T26: placeable views"); }
        }

        public void Bind(string placeableId, VisualHandle visual, double fullHp)
        {
            throw new System.NotImplementedException("T26: placeable views");
        }

        public void RenderFrom(MatchState state)
        {
            throw new System.NotImplementedException("T26: placeable views");
        }
    }
}
