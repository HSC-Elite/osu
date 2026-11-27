// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Tournament.Audio;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Screens.TeamSpeak;
using osuTK;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneTeamSpeak : TournamentTestScene
    {
        [Cached]
        private readonly TeamSpeakSession session = new TeamSpeakSession();

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(session);
            Add(new Container
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.7f,
                Child = new TeamSpeakConsoleScreen
                {
                    FillMode = FillMode.Stretch,
                },
            });
            Add(new Container
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.3f,
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Gray(0.12f) },
                    new OsuSpriteText { Text = "Stage preview", Position = new Vector2(12) },
                    new TeamSpeakSpeakerOverlay { Position = new Vector2(12, 40) },
                },
            });
        }
    }
}
