using System;
using System.Collections.Generic;
using UnityEngine;

namespace RedHollow.Game.Art
{
    /// <summary>
    /// Ticket 013 (T-13) — the artKey→asset mapping, as DATA. This is the half of the asset seam
    /// that makes "generated art drops in as a pure asset swap" true: adding a piece of art is
    /// registering one more entry in this table, never a new branch in resolver code. The tests pin
    /// that shape behaviorally — registering a key at runtime flips it from placeholder to real art
    /// through an unchanged <see cref="ArtVisualResolver"/> — and via <see cref="Keys"/>, which
    /// exposes the mapping for inspection.
    ///
    /// Entries carry a factory rather than a loaded asset so the catalog itself never touches the
    /// asset pipeline: a missing file is a factory that was never registered, which the resolver
    /// answers with its fallback instead of an exception. Plain C# on purpose (T10's invariant —
    /// nothing here is a MonoBehaviour and nothing here may hold sim state).
    /// </summary>
    public sealed class ArtCatalog
    {
        private readonly Dictionary<string, Func<GameObject>> _entries =
            new Dictionary<string, Func<GameObject>>();

        /// <summary>Register one art entry. Later registration for the same key wins.</summary>
        public void Register(string artKey, Func<GameObject> instantiate)
        {
            if (string.IsNullOrEmpty(artKey) || instantiate == null)
            {
                // A nameless or bodiless entry can never be resolved; recording it would only
                // let Contains and Resolve disagree. Absence is the honest state.
                return;
            }

            _entries[artKey] = instantiate;
        }

        /// <summary>Whether this key names registered art.</summary>
        public bool Contains(string artKey)
        {
            return !string.IsNullOrEmpty(artKey) && _entries.ContainsKey(artKey);
        }

        /// <summary>Every registered art key — the mapping is inspectable data, not hidden code.</summary>
        public IEnumerable<string> Keys
        {
            get { return _entries.Keys; }
        }

        /// <summary>
        /// Instantiate the art for a key. False (never a throw) for an unregistered, null or empty
        /// key — the resolver's fallback is the answer to absence, not an error.
        /// </summary>
        public bool TryInstantiate(string artKey, out GameObject instance)
        {
            instance = null;

            if (string.IsNullOrEmpty(artKey))
            {
                return false;
            }

            Func<GameObject> factory;
            if (!_entries.TryGetValue(artKey, out factory))
            {
                return false;
            }

            try
            {
                instance = factory();
            }
            catch (Exception)
            {
                // A factory that blows up is absence with extra noise: the seam's contract is
                // that nothing here may fail, so the fallback answers instead.
                instance = null;
            }

            return instance != null;
        }
    }
}
