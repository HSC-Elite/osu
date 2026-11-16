// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Tournament.Components;

namespace osu.Game.Tournament.Screens
{
    /// <summary>
    /// A stream scene whose control panel is hosted separately in the console window.
    /// </summary>
    public abstract partial class TournamentStageScreen : TournamentScreen
    {
        // Ownership of the drawable tree belongs to the scene manager, not this screen.
        public ControlPanel ControlPanel { get; protected set; } = new ControlPanel(true);

        public bool IsStageActive { get; internal set; }
    }
}
