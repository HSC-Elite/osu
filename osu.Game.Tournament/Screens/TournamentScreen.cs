// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Overlays;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Screens
{
    public abstract partial class TournamentScreen : CompositeDrawable
    {
        public const double FADE_DELAY = 200;

        [Resolved]
        protected LadderInfo LadderInfo { get; private set; } = null!;

        [Cached]
        protected readonly OverlayColourProvider ColourProvider = new OverlayColourProvider(OverlayColourScheme.Blue);

        [Resolved]
        protected MatchIPCInfo IPC { get; private set; } = null!;

        [Resolved]
        protected StageContainer? StageContainer { get; private set; }

        private readonly List<TourneyVideo> stageVideos = new List<TourneyVideo>();
        private readonly List<Drawable> pendingStageContent = new List<Drawable>();

        protected Container? StageContent { get; private set; }

        internal bool StageVideoAvailable => stageVideos.Any(video => video.VideoAvailable);

        public bool StageShowing => StageContainer?.DisplayedStage == StageContent;

        protected TournamentScreen()
        {
            RelativeSizeAxes = Axes.Both;

            FillMode = FillMode.Fit;
            FillAspectRatio = 16 / 9f;
        }

        protected void SetStageContent(Container content, IReadOnlyDependencyContainer? dependencies = null)
        {
            var stageContent = new TournamentStageDependencyContainer(content, dependencies ?? Dependencies);

            foreach (var drawable in pendingStageContent)
                stageContent.Add(drawable);

            pendingStageContent.Clear();
            StageContent = stageContent;
            StageContainer?.AddStage(stageContent);
        }

        protected void AddStageContent(Drawable drawable)
        {
            if (StageContent == null)
                pendingStageContent.Add(drawable);
            else
                StageContent.Add(drawable);
        }

        protected void AddStageContentRange(IEnumerable<Drawable> drawables)
        {
            foreach (var drawable in drawables)
                AddStageContent(drawable);
        }

        protected void SetStageVideo(TourneyVideo video) => stageVideos.Add(video);

        protected void ShowStageContent()
        {
            if (StageContainer == null || StageContent == null)
                return;

            StageContainer.ShowStage(StageContent);
        }

        public override void Hide() => this.FadeOut(FADE_DELAY);

        public override void Show()
        {
            this.FadeIn(FADE_DELAY);
        }
    }
}
