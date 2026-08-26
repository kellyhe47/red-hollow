using System.Globalization;
using RedHollow.Sim;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Presentation copy for the HUD. Sim spellings (<c>unlock_Q</c>, <c>hs_saloon</c>,
    /// <c>spike_trap</c>) stay on the models — this is the view-side translation, never a rule.
    /// </summary>
    internal static class HudCopy
    {
        public static string HotspotName(string hotspotId)
        {
            switch (hotspotId)
            {
                case "hs_saloon":
                    return "Saloon";
                case "hs_chapel":
                    return "Chapel";
                case "hs_homestead":
                    return "Homestead";
                default:
                    return TitleFromId(hotspotId, "hs_");
            }
        }

        public static string PlaceableName(string placeableType)
        {
            switch (placeableType)
            {
                case PlaceableType.Barricade:
                    return "Barricade";
                case PlaceableType.SpikeTrap:
                    return "Spike Trap";
                case PlaceableType.DynamiteTrap:
                    return "Dynamite";
                case PlaceableType.Turret:
                    return "Turret";
                case PlaceableType.MedStation:
                    return "Med Station";
                default:
                    return TitleFromId(placeableType, null);
            }
        }

        public static string SkillChoice(string choice)
        {
            switch (choice)
            {
                case "unlock_Q":
                    return "Unlock Q";
                case "unlock_E":
                    return "Unlock E";
                case "rank_Q":
                    return "Rank up Q";
                case "rank_E":
                    return "Rank up E";
                default:
                    return choice ?? string.Empty;
            }
        }

        public static string SlotFace(AbilitySlotReadout slot)
        {
            if (slot == null)
            {
                return string.Empty;
            }

            if (slot.Locked)
            {
                return slot.Slot + "  locked";
            }

            if (!slot.Ready)
            {
                var seconds = slot.CooldownRemainingSeconds;
                var shown = seconds < 1.0
                    ? seconds.ToString("0.0", CultureInfo.InvariantCulture)
                    : ((int)seconds).ToString(CultureInfo.InvariantCulture);
                return slot.Slot + "  " + shown + "s";
            }

            return slot.Slot + "  ready";
        }

        private static string TitleFromId(string id, string prefix)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            var body = id;
            if (!string.IsNullOrEmpty(prefix) && body.StartsWith(prefix))
            {
                body = body.Substring(prefix.Length);
            }

            var parts = body.Split('_');
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                {
                    continue;
                }

                parts[i] = char.ToUpperInvariant(parts[i][0])
                    + (parts[i].Length > 1 ? parts[i].Substring(1) : string.Empty);
            }

            return string.Join(" ", parts);
        }
    }
}
