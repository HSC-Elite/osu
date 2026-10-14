// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Overlays;
using osuTK;

namespace osu.Game.Tournament.Screens
{
    public partial class StageContainer : Container
    {
        public Drawable? DisplayedStage { get; private set; }

        public StageContainer()
        {
            RelativeSizeAxes = Axes.Both;
            FillMode = FillMode.Fit;
            FillAspectRatio = 16 / 9f;
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;
        }

        public void AddStage(Drawable stage)
        {
            if (stage.Parent == this)
                return;

            stage.Alpha = 0;
            AddInternal(stage);
        }

        public void ShowStage(Drawable stage)
        {
            if (DisplayedStage == stage)
                return;

            var previousStage = DisplayedStage;

            AddStage(stage);

            DisplayedStage = stage;
            stage.FadeIn(TournamentScreen.FADE_DELAY);
            previousStage?.FadeOut(TournamentScreen.FADE_DELAY);
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

    internal partial class TournamentStageDependencyContainer : Container
    {
        private readonly IReadOnlyDependencyContainer dependencies;

        public TournamentStageDependencyContainer(Container content, IReadOnlyDependencyContainer dependencies)
        {
            this.dependencies = dependencies;
            RelativeSizeAxes = Axes.Both;
            Child = content;
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            return new StageDependencyContainer(base.CreateChildDependencies(parent), dependencies);
        }

        private sealed class StageDependencyContainer : IReadOnlyDependencyContainer
        {
            private readonly IReadOnlyDependencyContainer stageDependencies;
            private readonly IReadOnlyDependencyContainer screenDependencies;
            private readonly DependencyContainer injectionContainer;

            public StageDependencyContainer(IReadOnlyDependencyContainer stageDependencies, IReadOnlyDependencyContainer screenDependencies)
            {
                this.stageDependencies = stageDependencies;
                this.screenDependencies = screenDependencies;
                injectionContainer = new DependencyContainer(this);
            }

            public object Get(Type type)
                => stageDependencies.Get(type) ?? screenDependencies.Get(type);

            public object Get(Type type, CacheInfo info)
                => stageDependencies.Get(type, info) ?? screenDependencies.Get(type, info);

            public void Inject<T>(T instance)
                where T : class, IDependencyInjectionCandidate
                => injectionContainer.Inject(instance);
        }
    }
}
