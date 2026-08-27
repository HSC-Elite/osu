// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Platform;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Skinning;
using osu.Game.Tournament.Configuration;
using osu.Game.Tournament.MultiWindow;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tournament
{
    public partial class TournamentControlGame : OsuGameBase
    {
        [Cached]
        private readonly TournamentStageState stageState = new TournamentStageState();

        public string? LocalSyncPipeName { get; init; }

        public string? RemoteSyncPipeName { get; init; }

        public override void SetHost(GameHost host)
        {
            base.SetHost(host);

            if (host.Window != null)
                host.Window.Title = "osu! tournament control";
        }

        private DependencyContainer dependencies = null!;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            return dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        }

        [BackgroundDependencyLoader]
        private void load(FrameworkConfigManager frameworkConfig, Storage baseStorage)
        {
            var windowMode = frameworkConfig.GetBindable<WindowMode>(FrameworkSetting.WindowMode);

            dependencies.Cache(new TournamentConfigManager(baseStorage));

            if (!string.IsNullOrEmpty(LocalSyncPipeName) && !string.IsNullOrEmpty(RemoteSyncPipeName))
            {
                TournamentCompanionManager companionManager;
                Add(companionManager = new TournamentCompanionManager(TournamentWindowRole.Control, LocalSyncPipeName, RemoteSyncPipeName));
                dependencies.Cache(companionManager);
            }

            AddRange(new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(12, 12, 12, 255),
                },
                new TournamentControlLayoutHost(),
            });

            windowMode.BindValueChanged(_ => Schedule(() => windowMode.Value = WindowMode.Windowed), true);
        }

        protected override IAPIProvider CreateAPIProvider(EndpointConfiguration endpoints)
        {
            return new DummyAPIAccess();
        }

        private partial class TournamentControlLayoutHost : CompositeDrawable
        {
            private FillFlowContainer content = null!;
            private readonly Dictionary<string, ITournamentSynchronisedStatefulDrawable> controlsByKey = new Dictionary<string, ITournamentSynchronisedStatefulDrawable>();

            [Resolved]
            private TournamentStageState stageState { get; set; } = null!;

            [Resolved]
            private TournamentCompanionManager? companionManager { get; set; }

            public TournamentControlLayoutHost()
            {
                RelativeSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = new BasicScrollContainer(Direction.Vertical)
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = content = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 8),
                        Padding = new MarginPadding(8),
                    }
                };

                stageState.ControlPanelLayoutJson.BindValueChanged(layout => rebuild(layout.NewValue), true);

                if (companionManager != null)
                    companionManager.SyncMessageReceived += handleSyncMessage;
            }

            private void rebuild(string layoutJson)
            {
                content.Clear();
                controlsByKey.Clear();

                if (string.IsNullOrWhiteSpace(layoutJson))
                    return;

                SerialisedDrawableInfo[]? drawables = JsonConvert.DeserializeObject<SerialisedDrawableInfo[]>(layoutJson);

                if (drawables == null)
                    return;

                foreach (var drawableInfo in drawables)
                {
                    var drawable = drawableInfo.CreateInstance();
                    content.Add(drawable);

                    if (drawable is ITournamentSynchronisedStatefulDrawable self)
                        controlsByKey[self.SynchronisationKey] = self;
                }
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);

                if (companionManager != null)
                    companionManager.SyncMessageReceived -= handleSyncMessage;
            }

            private void handleSyncMessage(TournamentWindowSyncMessage message)
            {
                if (message.MessageType != TournamentWindowSyncMessageType.ControlStateChanged)
                    return;

                if (message.ControlKey == null || message.ControlProperty == null)
                    return;

                if (controlsByKey.TryGetValue(message.ControlKey, out var control))
                    control.ApplyState(message.ControlProperty, message.JsonValue);
            }
        }
    }
}
