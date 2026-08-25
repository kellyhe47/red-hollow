namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 017 (T-17) owns this half of <see cref="MatchSim"/>: turning the wave table (R-19)
    /// and the map's entry tunnels (R-14) into live <see cref="Monster"/> entities.
    ///
    /// Nothing in the sim creates a monster today. <see cref="WaveTable"/> says what a wave is
    /// made of, <see cref="SimConfig.Monsters"/> says what each archetype is worth (R-17), and
    /// <see cref="ColonyMap.EntryTunnels"/> says where the breaches are — but no code assembles
    /// the three into <see cref="MatchState.Monsters"/>, so a match can never contain a monster
    /// and no wave can be fought. This file is the seam that closes that gap.
    ///
    /// It grades no fixture: G-010/G-011/G-012 grade what happens when a monster *dies*, and every
    /// fixture that needs a monster hands one to the loader ready-made. The contract therefore
    /// lives entirely in T17_SpawningTests.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>
        /// R-19 / R-14 / R-17 / R-54. Spawn one wave into the world.
        ///
        /// The composition comes from <see cref="WaveTable"/>, the stats from
        /// <see cref="SimConfig.Monsters"/> and the positions from the tunnels the wave marks
        /// active, resolved through <see cref="ColonyMap.EntryTunnels"/>. Every id created joins
        /// <see cref="WaveState.LivingMonsterIds"/>, which is the roster
        /// <see cref="RecordMonsterKill"/> counts down to complete the wave (R-02).
        ///
        /// Shape only — T-17 has not been implemented yet.
        /// </summary>
        public WaveSpawnResult SpawnWave(int waveNumber)
        {
            BeginCommand();
            throw NotYet(
                "T-17",
                "spawning wave " + waveNumber + " from the wave table into MatchState.Monsters "
                + "(R-19 composition, R-17 stats, R-14 tunnel placement)");
        }
    }
}
