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
        private static readonly string[] Slots = { AbilitySlot.Q, AbilitySlot.E };

        private readonly HostedMatch _match;

        private readonly string _accountId;

        private readonly IProfileStore _profiles;

        private readonly List<HotspotReadout> _hotspots = new List<HotspotReadout>();

        private readonly List<HudToast> _toasts = new List<HudToast>();

        private readonly List<int> _expectedEntryTunnels = new List<int>();

        private readonly List<int> _entryFlares = new List<int>();

        /// <summary>R-12 — hotspots the sim reported emptied.</summary>
        private readonly HashSet<string> _lostHotspots = new HashSet<string>();

        private AccountProfile _profile;

        private bool _redFlashActive;

        private bool _pickerOpen;

        private string _lastSpendRejection;

        public CombatHudModel(HostedMatch match, string accountId, IProfileStore profiles)
        {
            _match = match;
            _accountId = accountId;
            _profiles = profiles;
        }

        // ---- top bar (R-61) -------------------------------------------------------------------

        public int WaveNumber => _match.State.Wave.Number;

        public int TotalWaves => _match.State.Wave.TotalWaves;

        /// <summary>R-61 — monsters remaining, off the living roster and nothing else.</summary>
        public int MonstersRemaining => _match.State.Wave.LivingMonsterIds.Count;

        public int Scrip => _match.State.Team.Scrip;

        public IReadOnlyList<HotspotReadout> Hotspots => _hotspots;

        // ---- self bar (R-61) ------------------------------------------------------------------

        public double Hp
        {
            get
            {
                var hero = OwnHero();
                return hero == null ? 0.0 : hero.Hp;
            }
        }

        public double MaxHp
        {
            get
            {
                var hero = OwnHero();
                return hero == null ? 0.0 : hero.MaxHp;
            }
        }

        public string HeroClass
        {
            get
            {
                var hero = OwnHero();
                return hero == null ? null : hero.HeroClass;
            }
        }

        /// <summary>R-41 — the account level, off the profile store.</summary>
        public int Level => Profile().Level;

        public double LifetimeXp => Profile().LifetimeXp;

        public int UnspentSkillPoints => Profile().SkillPoints;

        /// <summary>R-61 — the badge shows exactly when a point is banked.</summary>
        public bool SkillPointBadge => Profile().SkillPoints > 0;

        /// <summary>The readout for "Q" or "E" (R-31 padlock, R-32 sweep).</summary>
        public AbilitySlotReadout SlotFor(string slot)
        {
            var profile = Profile();
            profile.Abilities.TryGetValue(slot, out var rank);

            var remaining = 0.0;
            var hero = OwnHero();
            if (hero != null && hero.CooldownReadyAt.TryGetValue(slot, out var readyAt))
            {
                // Inclusive deadline: at now == ready-at the slot is ready with nothing left.
                remaining = readyAt - _match.Clock.ElapsedSeconds;
                if (remaining < 0.0)
                {
                    remaining = 0.0;
                }
            }

            return new AbilitySlotReadout
            {
                Slot = slot,
                Locked = rank <= 0,
                Rank = rank,
                CooldownRemainingSeconds = remaining,
                Ready = remaining <= 0.0,
            };
        }

        // ---- wireframe combat states ----------------------------------------------------------

        /// <summary>Oldest first. Kinds and subjects are contract; copy is not.</summary>
        public IReadOnlyList<HudToast> Toasts => _toasts;

        /// <summary>R-13 — raised by a `civilians_killed` event that actually killed somebody.</summary>
        public bool RedFlashActive => _redFlashActive;

        /// <summary>Entry-tunnel indices flaring because the wave just spawned out of them.</summary>
        public IReadOnlyList<int> EntryFlares => _entryFlares;

        /// <summary>
        /// R-05 — the entries the planning preview named, carried across the phase change so a
        /// `wave_spawned` event knows where to flare (the event itself names no tunnels).
        /// </summary>
        public void SetExpectedEntryTunnels(IReadOnlyList<int> tunnels)
        {
            _expectedEntryTunnels.Clear();
            if (tunnels != null)
            {
                _expectedEntryTunnels.AddRange(tunnels);
            }
        }

        /// <summary>R-33 — own hero down → grey overlay "Respawning in Ns".</summary>
        public bool SpectateOverlayVisible
        {
            get
            {
                var hero = OwnHero();
                return hero != null && !hero.Alive;
            }
        }

        /// <summary>Seconds until respawn, clamped at 0; the deadline is INCLUSIVE (R-33).</summary>
        public double RespawnInSeconds
        {
            get
            {
                var hero = OwnHero();
                if (hero == null || hero.Alive || !hero.RespawnAt.HasValue)
                {
                    return 0.0;
                }

                var remaining = hero.RespawnAt.Value - _match.Clock.ElapsedSeconds;
                return remaining > 0.0 ? remaining : 0.0;
            }
        }

        /// <summary>The living ally the camera follows, or null when nobody is left standing.</summary>
        public string SpectateTargetHeroId
        {
            get
            {
                var own = OwnHero();
                foreach (var hero in _match.State.Heroes.Values)
                {
                    if (hero.Alive && (own == null || !ReferenceEquals(hero, own)))
                    {
                        return hero.Id;
                    }
                }

                return null;
            }
        }

        // ---- level-up picker (R-62 / R-42) ----------------------------------------------------

        /// <summary>R-62 — a non-blocking overlay. Opening it stops NOTHING.</summary>
        public bool PickerOpen => _pickerOpen;

        /// <summary>Hotkey L and clicking the badge both land here (R-62).</summary>
        public void OpenPicker()
        {
            // One assignment: no clock, no session, no Time.timeScale (R-62).
            _pickerOpen = true;
        }

        public void ClosePicker()
        {
            _pickerOpen = false;
        }

        /// <summary>
        /// R-42 — the cards: unlock for a locked ability, rank-up for an unlocked one below max
        /// rank. Derived from the profile and the config's max, never hardcoded.
        /// </summary>
        public IReadOnlyList<LevelUpChoice> PickerChoices
        {
            get
            {
                var profile = Profile();
                var maxRank = _match.Sim.Config.MaxAbilityRank;
                var choices = new List<LevelUpChoice>();

                foreach (var slot in Slots)
                {
                    profile.Abilities.TryGetValue(slot, out var rank);
                    if (rank <= 0)
                    {
                        choices.Add(new LevelUpChoice { Choice = "unlock_" + slot });
                    }
                    else if (rank < maxRank)
                    {
                        choices.Add(new LevelUpChoice { Choice = "rank_" + slot });
                    }

                    // An ability at max rank offers nothing further (R-42).
                }

                return choices;
            }
        }

        /// <summary>One <see cref="MatchSim.SpendSkillPoint"/> command — a normal command (R-62).</summary>
        public SpendSkillPointResult Spend(string choice)
        {
            var hero = OwnHero();
            var result = _match.Sim.SpendSkillPoint(new SpendSkillPointRequest
            {
                AccountId = _accountId,
                HeroId = hero == null ? null : hero.Id,
                Choice = choice,
            });

            _lastSpendRejection = result.Accepted ? null : result.RejectionReason;

            return result;
        }

        /// <summary>The reason string off the last `spend_rejected` event, or null.</summary>
        public string LastSpendRejection => _lastSpendRejection;

        // ---- feeds ----------------------------------------------------------------------------

        public void OnSimEvent(SimEvent evt)
        {
            if (evt == null || evt.Fields == null)
            {
                return;
            }

            switch (evt.Type)
            {
                case "level_up":
                    _toasts.Add(new HudToast
                    {
                        Kind = HudToastKind.LevelUp,
                        SubjectId = FieldString(evt, "hero_id"),
                        Text = "Level up!",
                    });
                    break;

                case "civilians_killed":
                    // R-13 — flashing red for nobody dying is crying wolf.
                    if (FieldInt(evt, "count") > 0)
                    {
                        _redFlashActive = true;
                        _toasts.Add(new HudToast
                        {
                            Kind = HudToastKind.CiviliansLost,
                            SubjectId = FieldString(evt, "hotspot_id"),
                            Text = "Civilians lost!",
                        });
                    }

                    break;

                case "hotspot_emptied":
                    var emptied = FieldString(evt, "hotspot_id");
                    if (!string.IsNullOrEmpty(emptied))
                    {
                        _lostHotspots.Add(emptied);
                    }

                    break;

                case "wave_spawned":
                    // DEC-018 — the event names no tunnels; the flare targets are the entries the
                    // planning preview named, carried across the phase change.
                    _entryFlares.Clear();
                    _entryFlares.AddRange(_expectedEntryTunnels);
                    break;
            }
        }

        public void OnSessionNotice(SessionNotice notice)
        {
            if (notice == null || notice.Kind != SessionNoticeKind.PlayerDisconnected)
            {
                return;
            }

            _toasts.Add(new HudToast
            {
                Kind = HudToastKind.PlayerDisconnected,
                SubjectId = notice.PeerId,
                Text = notice.Text,
            });
        }

        /// <summary>Re-read the replicated state and the profile.</summary>
        public void Refresh()
        {
            _profile = _profiles.Load(_accountId);

            _hotspots.Clear();
            foreach (var hotspot in _match.State.Hotspots.Values)
            {
                _hotspots.Add(new HotspotReadout
                {
                    HotspotId = hotspot.Id,
                    Civilians = hotspot.Civilians,
                    Lost = _lostHotspots.Contains(hotspot.Id) || hotspot.Civilians <= 0,
                });
            }
        }

        // ---- helpers --------------------------------------------------------------------------

        private AccountProfile Profile() => _profile ?? (_profile = _profiles.Load(_accountId));

        private Hero OwnHero()
        {
            foreach (var hero in _match.State.Heroes.Values)
            {
                if (string.Equals(hero.AccountId, _accountId, StringComparison.Ordinal))
                {
                    return hero;
                }
            }

            return null;
        }

        private static string FieldString(SimEvent evt, string key) =>
            evt.Fields.TryGetValue(key, out var value) ? value as string : null;

        private static int FieldInt(SimEvent evt, string key) =>
            evt.Fields.TryGetValue(key, out var value) && value != null
                ? Convert.ToInt32(value)
                : 0;
    }
}
