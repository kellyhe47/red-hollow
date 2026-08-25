using System;
using RedHollow.Game.View;

namespace RedHollow.Game.Art
{
    /// <summary>
    /// Ticket 013 (T-13) — the real-art resolver, chained IN FRONT of the total fallback from
    /// ticket 016. A key the <see cref="ArtCatalog"/> knows resolves to the authored art with
    /// <see cref="VisualHandle.IsPlaceholder"/> false; everything else — unknown, null, empty,
    /// garbage — delegates to the fallback and comes back as its placeholder handle.
    ///
    /// The contract is the seam's totality, restated one layer up: <see cref="Resolve"/> NEVER
    /// returns null and NEVER throws, for any input. <see cref="PlaceholderVisualResolver"/>
    /// deliberately does not probe for assets; this type is the one place that looks, and its
    /// answer to "absent" is always "ask the fallback", never an error. That is what keeps the
    /// placeholder build shippable and every gameplay ticket unblocked by art.
    /// </summary>
    public sealed class ArtVisualResolver : IVisualResolver
    {
        /// <param name="catalog">The artKey→asset table. The mapping is data, not code.</param>
        /// <param name="fallback">
        /// The total resolver that answers for every key the catalog does not. Required: without a
        /// fallback this resolver could fail, and a resolver that can fail turns "the art is not
        /// ready yet" into "the game does not run".
        /// </param>
        public ArtVisualResolver(ArtCatalog catalog, IVisualResolver fallback)
        {
            throw new NotImplementedException("ticket 013: ArtVisualResolver constructor");
        }

        /// <summary>Known key → real art, IsPlaceholder false. Anything else → the fallback's answer.</summary>
        public VisualHandle Resolve(VisualClass visualClass, string artKey)
        {
            throw new NotImplementedException("ticket 013: ArtVisualResolver.Resolve");
        }
    }
}
