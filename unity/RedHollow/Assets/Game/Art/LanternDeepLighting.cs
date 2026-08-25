using System;
using RedHollow.Game.View;

namespace RedHollow.Game.Art
{
    /// <summary>
    /// Ticket 013 (T-13) — R-15 / DEC-025. "Lantern Deep" is carried by SCENE LIGHTING, not by the
    /// textures (docs/comfy-prompts/00-shared-style.md §"Where the style lives in-engine"):
    ///
    ///  * dark warm ambient — near-black umber, never daylight;
    ///  * fog for the volumetric dust haze;
    ///  * all light artificial and SOURCED — amber point lights (lanterns, string lights, windows);
    ///  * zero natural light — no skybox, no sun, no directional light standing in for one;
    ///  * the cavern dome mesh IS the sky (<see cref="MatchScene.CavernDome"/>).
    ///
    /// Applied over a built <see cref="MatchScene"/> rather than baked into a .unity file so the
    /// look is reviewable in a diff and reproducible headlessly, same as the scene itself. The
    /// tests pin bounds (dark, warm, fog on, no skybox/sun, a dome, a warm point light); the exact
    /// painterly numbers inside those bounds are playtest's to tune, not the tests'.
    /// </summary>
    public static class LanternDeepLighting
    {
        /// <summary>
        /// Impose the Lantern Deep look on a built scene: RenderSettings (ambient, fog, no skybox,
        /// no sun), replace any daylight-style directional light with sourced amber point lights,
        /// and raise the cavern dome (assigned to <see cref="MatchScene.CavernDome"/>).
        /// </summary>
        public static void Apply(MatchScene scene)
        {
            throw new NotImplementedException("ticket 013: LanternDeepLighting.Apply");
        }
    }
}
