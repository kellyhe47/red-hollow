namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 025 (T-25) — the shell-side combat action tunables. All three numbers are SHELL
    /// POLICY, not PRD contract: the PRD specs no attack cadence (the 014 harness modeled 0.25 s
    /// and printed it as a parameter) and no aim-line footprint, so they live here as config the
    /// composition root can override (<see cref="ShellBootstrapOptions.CombatActions"/>) rather
    /// than as constants inside the routing. Defaults chosen at T-25 — flagged to the owner.
    /// </summary>
    public sealed class CombatActionConfig
    {
        /// <summary>
        /// R-30 — seconds between basic attacks while SPACE is held. The press itself fires
        /// immediately; this only paces the re-fire. 0.25 s is the 014 harness's number.
        /// </summary>
        public double AttackCadenceSeconds = 0.25;

        /// <summary>How far the basic-attack aim line reaches, in ground units.</summary>
        public double AimLineLength = 48.0;

        /// <summary>Full corridor width of the aim line, in ground units.</summary>
        public double AimLineWidth = 6.0;
    }
}
