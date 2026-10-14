// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Mods;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    public partial class ModMultiplierProvider : Component, IModMultiplierProvider
    {
        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        public double? GetModMultiplierFromMod(Mod mod)
        {
            return ladder.ModMultiplierSettings.FirstOrDefault(s => s.ModAcronym.Value == mod.Acronym)?.Multiplier.Value;
        }
    }
}
