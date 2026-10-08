// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Overlays;
using osuTK;

namespace osu.Game.Tournament.Screens
{
    public partial class StageContainer : Container
    {
        private Drawable? displayedStage;

        public StageContainer()
        {
            Size = new Vector2(1366, 768);
        }

        public void ShowStage(Drawable stage)
        {
            if (displayedStage == stage)
                return;

            var previousStage = displayedStage;

            if (stage.Parent != this)
            {
                stage.Alpha = 0;
                AddInternal(stage);
            }

            displayedStage = stage;
            stage.FadeIn(TournamentScreen.FADE_DELAY);

            if (previousStage != null)
            {
                previousStage.FadeOut(TournamentScreen.FADE_DELAY);
                Scheduler.AddDelayed(() =>
                {
                    if (displayedStage != previousStage && previousStage.Parent == this)
                        RemoveInternal(previousStage, false);
                }, TournamentScreen.FADE_DELAY);
            }
        }
    }

    internal partial class TournamentStageContentContainer : Container
    {
        [Cached]
        private readonly OverlayColourProvider colourProvider;

        public TournamentStageContentContainer(OverlayColourProvider colourProvider)
        {
            this.colourProvider = colourProvider;
            RelativeSizeAxes = Axes.Both;
        }
    }
}
