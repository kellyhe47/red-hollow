using System;

namespace RedHollow.Sim
{
    /// <summary>
    /// R-44 / DEC-015: the production profile store. Accounts are callsign strings with no password
    /// and no auth, so persistence is nothing more than a server-local document keyed by callsign.
    ///
    /// STUB ONLY — ticket 009's implementer owns the behaviour. It exists so the T-09 tests can name
    /// the production type; every member below still throws.
    ///
    /// Deliberately dependency-free: GameSim targets netstandard2.1 and is compiled by Unity too, so
    /// System.Text.Json is not available here without a package reference the Unity build would not
    /// have. Serialize by hand — a profile is four scalars and two ability ranks.
    /// </summary>
    public sealed class JsonProfileStore : IProfileStore
    {
        /// <param name="filePath">
        /// Absolute path of the JSON document holding every callsign's profile. The store owns this
        /// file: it must create it on first save and read it back on construction or on load, which
        /// is what makes "server-local" (R-44) survive a process restart.
        /// </param>
        public JsonProfileStore(string filePath)
        {
            FilePath = filePath;
        }

        public string FilePath { get; }

        public AccountProfile Load(string accountId)
        {
            throw new NotImplementedException(
                "T-09 not implemented: load a profile by callsign, fresh account when unknown (R-44)");
        }

        public void Save(AccountProfile profile)
        {
            throw new NotImplementedException(
                "T-09 not implemented: persist a profile by callsign to " + FilePath + " (R-43/R-44)");
        }
    }
}
