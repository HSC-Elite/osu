// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Localisation;
using osu.Game.Configuration;

namespace osu.Game.Overlays.Settings.Sections.Custom
{
    public partial class TestPlayerSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Test player";

        [BackgroundDependencyLoader]
        private void load(CustomConfigManager config)
        {
            Child = new SettingsTextBox
            {
                LabelText = "Test player replay path (.osr)",
                TooltipText = "Development server, osu! only. Uses replay cursor and buttons during solo or multiplayer gameplay. Clear to disable. Beatmap and mods must match (including Classic for stable replays).",
                Current = config.GetBindable<string>(CustomSetting.TestPlayerReplayPath)
            };
        }
    }
}
