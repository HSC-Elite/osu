// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics.Colour;

namespace osu.Game.Screens.Tournament
{
    public interface ITeamColorProvider
    {
        ColourInfo GetTeamColor(int userId);
    }
}
