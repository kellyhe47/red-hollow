using System;
using System.Collections.Generic;
using UnityEngine;

namespace RedHollow.Game.Input
{
    /// <summary>
    /// Every physical control R-30 names, as an enum rather than a device code.
    ///
    /// Why an enum and not <c>KeyCode</c> / an Input System <c>InputAction</c>: R-30 is a *mapping*
    /// requirement ("W is movement only", "mouse buttons stay free"), and a mapping can only be
    /// tested if the thing being mapped can be spelled without a keyboard attached. Anything
    /// device-shaped lives behind <see cref="IInputSource"/>; everything downstream of that seam is
    /// plain data.
    /// </summary>
    public enum PlayerKey
    {
        W,
        A,
        S,
        D,
        Space,
        Q,
        E,

        /// <summary>R-30 — reserved for UI. Must never produce a gameplay intent.</summary>
        MouseLeft,

        /// <summary>R-30 — reserved for UI.</summary>
        MouseRight,

        /// <summary>R-30 — reserved for UI.</summary>
        MouseMiddle,
    }

    /// <summary>
    /// One frame of raw player input, device-agnostic. Pure data on purpose — it has no behaviour to
    /// stub and no engine call to fake, which is what lets the R-30 mapping be table-tested in
    /// EditMode with no device, no scene and no Input System backend.
    /// </summary>
    public sealed class InputSnapshot
    {
        /// <summary>Which controls are held this frame.</summary>
        public readonly HashSet<PlayerKey> Pressed = new HashSet<PlayerKey>();

        /// <summary>
        /// Where the cursor is, projected onto the ground plane, in the sim's own ground space
        /// (see <see cref="RedHollow.Game.View.SimSpace"/>): x = right, y = forward.
        ///
        /// R-30 / DEC-017: this is an *aim* input and nothing else. A cursor position alone must
        /// never become movement — that is exactly the click-to-move the PRD rejects.
        /// </summary>
        public Vector2 CursorGroundPoint;
    }

    /// <summary>
    /// What one frame of input asks the hero to do (R-30). The four channels are deliberately
    /// separate, because R-30's whole content is that they do not collapse into each other: moving
    /// is not aiming, the basic attack is not an ability, and Q is not E.
    /// </summary>
    public sealed class HeroIntent
    {
        /// <summary>
        /// Ground-space direction to walk, x = right / y = forward, <see cref="Vector2.zero"/> for
        /// "not moving". The PRD states no move speed, so magnitude is not a contract — direction is.
        /// </summary>
        public Vector2 MoveDirection;

        /// <summary>
        /// R-30 — the ground point the hero aims at, which is the cursor and never the movement
        /// direction. Basic attacks and skillshots are fired along it ("SPACE = basic attack toward
        /// cursor").
        /// </summary>
        public Vector2 AimPoint;

        /// <summary>R-30 — SPACE. The basic attack is not an ability and holds no slot.</summary>
        public bool BasicAttack;

        /// <summary>
        /// R-30 / R-31 — the ability slot this frame casts: one of the
        /// <see cref="RedHollow.Sim.AbilitySlot"/> constants ("Q" / "E"), or null for none. Reuses
        /// the sim's spelling so this feeds <see cref="RedHollow.Sim.HeroAbilityRequest.Slot"/>
        /// without a translation table in between.
        /// </summary>
        public string Ability;
    }

    /// <summary>
    /// Where a frame of raw input comes from. The one place in the shell allowed to know about a
    /// keyboard, a mouse or the Input System — everything else consumes an
    /// <see cref="InputSnapshot"/>, which is why no test in this ticket needs a device.
    /// </summary>
    public interface IInputSource
    {
        InputSnapshot Sample();
    }

    /// <summary>
    /// Turns raw input into a <see cref="HeroIntent"/>. A seam rather than a static call so a
    /// remapping ticket can swap the binding without touching the views that consume the intent.
    /// </summary>
    public interface IHeroInputMap
    {
        HeroIntent Resolve(InputSnapshot snapshot);
    }

    /// <summary>
    /// The shipped R-30 binding: WASD move (W is movement only), cursor aims, SPACE is the basic
    /// attack, Q and E are the two abilities, and the mouse buttons produce nothing at all because
    /// they belong to the UI.
    /// </summary>
    public sealed class DefaultHeroInputMap : IHeroInputMap
    {
        public HeroIntent Resolve(InputSnapshot snapshot)
        {
            throw new NotImplementedException("ticket 016 — R-30 input mapping");
        }
    }
}
