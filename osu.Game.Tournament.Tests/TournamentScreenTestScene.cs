// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Tournament.Screens;
using osuTK;

namespace osu.Game.Tournament.Tests
{
    public abstract partial class TournamentScreenTestScene : TournamentTestScene
    {
        protected override Container<Drawable> Content { get; } = new TournamentScalingContainer();

        [BackgroundDependencyLoader]
        private void load()
        {
            base.Content.Add(new RefCountedBackbufferProvider
            {
                RelativeSizeAxes = Axes.Both,
                Child = Content,
            });
        }

        protected static Container CreateScreenPreview(TournamentStageScreen screen)
        {
            screen.IsStageActive = true;
            var preview = new Container { RelativeSizeAxes = Axes.Both };
            preview.Add(new Container
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.75f,
                Child = screen,
            });
            screen.OnLoadComplete += _ => preview.Add(new Container
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.25f,
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Child = screen.ControlPanel,
            });
            return preview;
        }

        private partial class TournamentScalingContainer : DrawSizePreservingFillContainer
        {
            public TournamentScalingContainer()
            {
                TargetDrawSize = new Vector2(1024, 768);
                RelativeSizeAxes = Axes.Both;
            }

            protected override void Update()
            {
                base.Update();

                Scale = new Vector2(Math.Min(1, Content.DrawWidth / (1920 + TournamentSceneManager.CONTROL_AREA_WIDTH)));
            }
        }
    }
}
