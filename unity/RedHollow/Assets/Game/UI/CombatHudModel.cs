using System;
using System.Collections.Generic;
using RedHollow.Game.Net;
using RedHollow.Sim;

namespace RedHollow.Game.UI
{
    /// <summary>What a HUD toast is about. Copy is presentation and is never contract.</summary>
    public enum HudToastKind
    {
        /// <summary>R-62 — "you levelled up" (with the badge).</summary>
        LevelUp,

        /// <summary>R-13 — "Civilians lost at the …!" (with the red flash).</summary>
        CiviliansLost,

        /// <summary>R-53 — a player left mid-match; the match continues.</summary>
        PlayerDisconnected,
    }

    /// <summary>One toast the HUD owes the player.</summary>
    public sealed class HudToast
    {
        public HudToastKind Kind;

        /// <summary>Who or what it is about: a hotspot id, a peer id, a hero id.</summary>
        public string SubjectId;

        /// <summary>Copy. The PRD names none, so nothing may assert on this.</summary>
        public string Text;
    }

    /// <summary>One SPACE/Q/E slot's readout: cooldown sweep, padlock, rank.</summary>
    public sealed class AbilitySlotReadout
    {
        /// <summary>An <see cref="AbilitySlot"/> constant.</summary>
        public string Slot;

        /// <summary>R-31 — rank 0 → padlock.</summary>
        public bool Locked;

        public int Rank;

        /// <summary>
        /// R-32 — seconds until ready, 0 when ready. An absent
        /// <see cref="Hero.CooldownReadyAt"/> key means ready, and the deadline is INCLUSIVE:
        /// at now == ready-at the slot is ready.
        /// </summary>
        public double CooldownRemainingSeconds;

        public bool Ready;
    }

    /// <summary>One card in the level-up picker (R-62 / R-42).</summary>
    public sealed class LevelUpChoice
    {
        /// <summary>The sim's own choice spelling: "unlock_Q", "unlock_E", "rank_Q", "rank_E".</summary>
        public string Choice;
    }

    /// <summary>
    /// Ticket 012 (T-12) — S4 Combat: the persistent HUD (R-61), the non-blocking level-up
    /// overlay (R-62), and the wireframe's combat states — dead-hero spectate, civilians-lost
    /// toast + red flash, lost-hotspot marking, monster-spawn entry flare, disconnect toast.
    ///
    /// Read-only over <see cref="MatchState"/> and the profile store; the one command it issues
    /// is <see cref="MatchSim.SpendSkillPoint"/>, through the hosted match. Events arrive via
    /// <see cref="OnSimEvent"/>, session notices via <see cref="OnSessionNotice"/> — the adapters
    /// feed both; this class holds every rule about what they mean on screen.
    /// </summary>
    public sealed class CombatHudModel
    {
        public CombatHudModel(
            HostedMatch match, string accountId, IProfileStore profiles) =>
            throw new NotImplementedException("T-12 / R-61: the combat HUD");

        // ---- top bar (R-61) -------------------------------------------------------------------

        public int WaveNumber =>
            throw new NotImplementedException("T-12 / R-61: wave number");

        public int TotalWaves =>
            throw new NotImplementedException("T-12 / R-61: total waves");

        /// <summary>R-61 — monsters remaining, off the living roster and nothing else.</summary>
        public int MonstersRemaining =>
            throw new NotImplementedException("T-12 / R-61: monsters remaining");

        public int Scrip =>
            throw new NotImplementedException("T-12 / R-61: shared scrip");

        public IReadOnlyList<HotspotReadout> Hotspots =>
            throw new NotImplementedException("T-12 / R-61: per-hotspot civilians");

        // ---- self bar (R-61) ------------------------------------------------------------------

        public double Hp =>
            throw new NotImplementedException("T-12 / R-61: own HP");

        public double MaxHp =>
            throw new NotImplementedException("T-12 / R-61: own max HP");

        public string HeroClass =>
            throw new NotImplementedException("T-12 / R-61: class icon");

        /// <summary>R-41 — the account level, off the profile store.</summary>
        public int Level =>
            throw new NotImplementedException("T-12 / R-61: account level");

        public double LifetimeXp =>
            throw new NotImplementedException("T-12 / R-61: the XP bar");

        public int UnspentSkillPoints =>
            throw new NotImplementedException("T-12 / R-61: unspent points");

        /// <summary>R-61 — the badge shows exactly when a point is banked.</summary>
        public bool SkillPointBadge =>
            throw new NotImplementedException("T-12 / R-61: the badge");

        /// <summary>The readout for "Q" or "E" (R-31 padlock, R-32 sweep).</summary>
        public AbilitySlotReadout SlotFor(string slot) =>
            throw new NotImplementedException("T-12 / R-31 / R-32: a slot readout");

        // ---- wireframe combat states ----------------------------------------------------------

        /// <summary>Oldest first. Kinds and subjects are contract; copy is not.</summary>
        public IReadOnlyList<HudToast> Toasts =>
            throw new NotImplementedException("T-12: HUD toasts");

        /// <summary>R-13 — raised by a `civilians_killed` event that actually killed somebody.</summary>
        public bool RedFlashActive =>
            throw new NotImplementedException("T-12 / R-13: the red flash");

        /// <summary>Entry-tunnel indices flaring because the wave just spawned out of them.</summary>
        public IReadOnlyList<int> EntryFlares =>
            throw new NotImplementedException("T-12: monster-spawn entry flare");

        /// <summary>
        /// R-05 — the entries the planning preview named, carried across the phase change so a
        /// `wave_spawned` event knows where to flare (the event itself names no tunnels).
        /// </summary>
        public void SetExpectedEntryTunnels(IReadOnlyList<int> tunnels) =>
            throw new NotImplementedException("T-12: where the flare goes");

        /// <summary>R-33 — own hero down → grey overlay "Respawning in Ns".</summary>
        public bool SpectateOverlayVisible =>
            throw new NotImplementedException("T-12 / R-33: dead-hero spectate");

        /// <summary>Seconds until respawn, clamped at 0; the deadline is INCLUSIVE (R-33).</summary>
        public double RespawnInSeconds =>
            throw new NotImplementedException("T-12 / R-33: the respawn countdown");

        /// <summary>The living ally the camera follows, or null when nobody is left standing.</summary>
        public string SpectateTargetHeroId =>
            throw new NotImplementedException("T-12: the spectate camera target");

        // ---- level-up picker (R-62 / R-42) ----------------------------------------------------

        /// <summary>R-62 — a non-blocking overlay. Opening it stops NOTHING.</summary>
        public bool PickerOpen =>
            throw new NotImplementedException("T-12 / R-62: the picker overlay");

        /// <summary>Hotkey L and clicking the badge both land here (R-62).</summary>
        public void OpenPicker() =>
            throw new NotImplementedException("T-12 / R-62: open the picker");

        public void ClosePicker() =>
            throw new NotImplementedException("T-12 / R-62: close the picker");

        /// <summary>
        /// R-42 — the cards: unlock for a locked ability, rank-up for an unlocked one below max
        /// rank. Derived from the profile and the config's max, never hardcoded.
        /// </summary>
        public IReadOnlyList<LevelUpChoice> PickerChoices =>
            throw new NotImplementedException("T-12 / R-42: the choice cards");

        /// <summary>One <see cref="MatchSim.SpendSkillPoint"/> command — a normal command (R-62).</summary>
        public SpendSkillPointResult Spend(string choice) =>
            throw new NotImplementedException("T-12 / R-42: spend a point");

        /// <summary>The reason string off the last `spend_rejected` event, or null.</summary>
        public string LastSpendRejection =>
            throw new NotImplementedException("T-12 / R-42: spend rejection reason");

        // ---- feeds ----------------------------------------------------------------------------

        public void OnSimEvent(SimEvent evt) =>
            throw new NotImplementedException("T-12: the sim event feed");

        public void OnSessionNotice(SessionNotice notice) =>
            throw new NotImplementedException("T-12 / R-53: the disconnect toast");

        /// <summary>Re-read the replicated state and the profile.</summary>
        public void Refresh() =>
            throw new NotImplementedException("T-12: refresh the HUD");
    }
}
