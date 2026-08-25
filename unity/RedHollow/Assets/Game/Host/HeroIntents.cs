using System.Collections.Generic;
using RedHollow.Game.Input;

namespace RedHollow.Game.Host
{
    /// <summary>
    /// "Hero H asked to do this, this step." The R-30 intent a client resolved
    /// (<see cref="DefaultHeroInputMap"/>) plus the id of the hero it drives — which is the piece
    /// an <see cref="HeroIntent"/> alone cannot carry, because a host drives up to four of them
    /// (R-50) and the sim addresses heroes by id.
    ///
    /// Shaped like <see cref="MonsterAttackIntent"/> and for the same reason: the host loop copies
    /// it straight into a <see cref="RedHollow.Sim.HeroMoveRequest"/> and computes nothing. Speed
    /// is the sim's (R-30), so nothing here carries a distance.
    /// </summary>
    public sealed class HeroIntentCommand
    {
        public string HeroId;

        /// <summary>This step's resolved intent. Null is "this hero sent nothing".</summary>
        public HeroIntent Intent;
    }

    /// <summary>
    /// Where the host loop gets this step's hero intents — the seam where R-30 finally reaches the
    /// sim. A seam rather than a concrete class for the same reason
    /// <see cref="IMonsterAttackSource"/> is one: a real session feeds it from
    /// <see cref="IInputSource"/> through <see cref="IHeroInputMap"/> (locally) and from the
    /// network (remotely), and a test feeds it a scripted frame, without either knowing about the
    /// other.
    /// </summary>
    public interface IHeroIntentSource
    {
        /// <summary>
        /// The intents to apply this step, or null/empty when nobody is holding a key. Candidates
        /// only — the sim decides what each one is actually worth (R-33: a dead hero does not walk).
        /// </summary>
        IReadOnlyList<HeroIntentCommand> IntentsThisStep(ISimHost sim, double deltaSeconds);
    }
}
