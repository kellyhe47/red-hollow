using System.Collections.Generic;
using RedHollow.Game.View;
using RedHollow.Sim;
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
    ///
    /// A pure function of the snapshot — no field, no clock, no last frame. That is what lets the
    /// whole of R-30 be graded as a table in EditMode, and it is also the property that makes the
    /// two failure modes the PRD calls out structurally impossible rather than merely absent:
    /// <see cref="InputSnapshot.CursorGroundPoint"/> is read into
    /// <see cref="HeroIntent.AimPoint"/> and nowhere else (DEC-017 — no click-to-move), and the
    /// three mouse buttons are never consulted at all (R-30 — the mouse belongs to the UI).
    /// </summary>
    public sealed class DefaultHeroInputMap : IHeroInputMap
    {
        /// <summary>
        /// One frame in, one intent out — always an intent, never null, so no caller downstream has
        /// to branch on "no input this frame".
        ///
        /// Movement is summed across the held keys and then normalised, so a diagonal is a
        /// direction rather than a 1.41x sprint. Only the direction is a contract: the PRD names no
        /// move speed, and the sim owns the distance a step covers.
        /// </summary>
        public HeroIntent Resolve(InputSnapshot snapshot)
        {
            var intent = new HeroIntent();
            if (snapshot == null)
            {
                return intent;
            }

            // R-30 — the cursor aims and does nothing else. Note this is the ONLY read of the
            // cursor in the method: DEC-017's "no click-to-move" is enforced by the shape of the
            // function, not by a check somewhere that could be deleted.
            intent.AimPoint = snapshot.CursorGroundPoint;

            var move = Vector2.zero;
            if (snapshot.Pressed.Contains(PlayerKey.W))
            {
                move.y += 1f;   // R-30 — W is forward, and forward is all it is.
            }

            if (snapshot.Pressed.Contains(PlayerKey.S))
            {
                move.y -= 1f;
            }

            if (snapshot.Pressed.Contains(PlayerKey.D))
            {
                move.x += 1f;
            }

            if (snapshot.Pressed.Contains(PlayerKey.A))
            {
                move.x -= 1f;
            }

            // W+S or A+D cancel to exactly zero, which must stay zero rather than become a
            // normalised NaN.
            intent.MoveDirection = move.sqrMagnitude > 0f ? move.normalized : Vector2.zero;

            // R-30 — SPACE is the basic attack. It takes no AbilitySlot, so nothing downstream can
            // charge it an R-32 cooldown or refuse it as an R-31 locked rank.
            intent.BasicAttack = snapshot.Pressed.Contains(PlayerKey.Space);

            // R-30 / R-31 — exactly one slot per frame, spelled the way the sim spells it. Q wins a
            // same-frame tie arbitrarily; the PRD orders nothing here, and a frame that cast both
            // would need two HeroAbilityRequests, which is a queueing decision no ticket has made.
            if (snapshot.Pressed.Contains(PlayerKey.Q))
            {
                intent.Ability = AbilitySlot.Q;
            }
            else if (snapshot.Pressed.Contains(PlayerKey.E))
            {
                intent.Ability = AbilitySlot.E;
            }

            return intent;
        }
    }

#if ENABLE_LEGACY_INPUT_MANAGER
    /// <summary>
    /// The device end of the seam: real keys and a real cursor, and nothing else. It reads devices
    /// and reports; it decides nothing, because every decision R-30 makes belongs to
    /// <see cref="DefaultHeroInputMap"/> where it can be tested without hardware.
    ///
    /// The mouse *buttons* are deliberately absent from the sample. R-30 keeps them for the UI, so
    /// the cheapest way to guarantee they never reach gameplay is for the gameplay input path never
    /// to look at them — <see cref="PlayerKey.MouseLeft"/> and its siblings exist so the mapping
    /// table can assert they produce nothing, not so this class can report them.
    ///
    /// Plain C# rather than a MonoBehaviour (R-51): it writes no sim state, and a component would
    /// only add a lifetime it does not need.
    /// </summary>
    public sealed class LegacyDeviceInputSource : IInputSource
    {
        private readonly Camera _camera;

        /// <param name="camera">
        /// The top-down camera the cursor is projected through. Null is tolerated — a session whose
        /// camera has not been wired yet still walks, it just aims at the origin, which is a far
        /// better failure than a null reference sixty times a second.
        /// </param>
        public LegacyDeviceInputSource(Camera camera)
        {
            _camera = camera;
        }

        public InputSnapshot Sample()
        {
            var snapshot = new InputSnapshot { CursorGroundPoint = CursorOnGround() };

            AddIfHeld(snapshot, KeyCode.W, PlayerKey.W);
            AddIfHeld(snapshot, KeyCode.A, PlayerKey.A);
            AddIfHeld(snapshot, KeyCode.S, PlayerKey.S);
            AddIfHeld(snapshot, KeyCode.D, PlayerKey.D);
            AddIfHeld(snapshot, KeyCode.Space, PlayerKey.Space);
            AddIfHeld(snapshot, KeyCode.Q, PlayerKey.Q);
            AddIfHeld(snapshot, KeyCode.E, PlayerKey.E);

            return snapshot;
        }

        private static void AddIfHeld(InputSnapshot snapshot, KeyCode key, PlayerKey mapped)
        {
            if (UnityEngine.Input.GetKey(key))
            {
                snapshot.Pressed.Add(mapped);
            }
        }

        /// <summary>
        /// Where the cursor lands on the colony floor, solved against the ground plane rather than
        /// ray-cast against colliders: the aim point must exist even where nothing has been built,
        /// and a skillshot fired at a gap in the geometry is R-30's job to aim, not physics'.
        /// </summary>
        private Vector2 CursorOnGround()
        {
            var camera = _camera != null ? _camera : Camera.main;
            if (camera == null)
            {
                return Vector2.zero;
            }

            var ray = camera.ScreenPointToRay(UnityEngine.Input.mousePosition);

            // Parallel to the floor: the cursor is on the horizon and has no ground point at all.
            if (Mathf.Approximately(ray.direction.y, 0f))
            {
                return Vector2.zero;
            }

            var distance = (SimSpace.GroundHeight - ray.origin.y) / ray.direction.y;
            if (distance < 0f)
            {
                return Vector2.zero;
            }

            return SimSpace.ToGroundVector(ray.GetPoint(distance));
        }
    }
#endif
}
