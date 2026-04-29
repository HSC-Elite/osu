// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Replays;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.OnlinePlay.Multiplayer.Spectate;
using osu.Game.Screens.Play;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.StableClient.IPC;

namespace osu.Game.Tournament.StableClient
{
    public partial class StableTournamentMultiSpectatorScreen : OsuScreen
    {
        private readonly WorkingBeatmap beatmap;

        [Resolved]
        private StableMatchIPCInfo stableIpc { get; set; } = null!;

        [Resolved]
        private LadderInfo ladderInfo { get; set; } = null!;

        private MasterGameplayClockContainer masterClockContainer = null!;
        private SpectatorSyncManager syncManager = null!;

        private readonly List<StablePlayerArea> instances = new List<StablePlayerArea>();
        private StablePlayerArea? currentAudioSource;
        private IAggregateAudioAdjustment? boundAdjustments;

        private double? firstFinishTime;
        private const double max_wait_time = 15000;

        public StableTournamentMultiSpectatorScreen(WorkingBeatmap beatmap)
        {
            this.beatmap = beatmap;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            StableTournamentGrid grid;

            InternalChildren = new Drawable[]
            {
                masterClockContainer = new MasterGameplayClockContainer(beatmap, 0)
                {
                    Child = grid = new StableTournamentGrid(ladderInfo.PlayersPerTeam.Value)
                },
                syncManager = new SpectatorSyncManager(masterClockContainer)
                {
                    ReadyToStart = performInitialSeek
                }
            };

            var handlers = stableIpc.GetActiveSpectatorHandlers().ToList();

            foreach (var handler in handlers.OrderBy(h => stableIpc.GetSlotIndexForUser(h.UserId)))
            {
                int slotIndex = stableIpc.GetSlotIndexForUser(handler.UserId);
                if (slotIndex < 0 || slotIndex >= grid.SlotCount) continue;

                var playerArea = new StablePlayerArea(handler, syncManager.CreateManagedClock());
                playerArea.OnFinished = onPlayerFinished;
                instances.Add(playerArea);

                grid.GetSlot(slotIndex).Add(playerArea.With(p => p.RelativeSizeAxes = Axes.Both));
            }

            if (instances.Any())
                bindAudioAdjustments(instances.First());
        }

        private void onPlayerFinished()
        {
            if (firstFinishTime == null)
                firstFinishTime = Time.Current;

            if (instances.All(i => i.IsFinished))
                stableIpc.State.Value = TourneyState.Ranking;
        }

        private void performInitialSeek()
        {
            double startTime = 0;
            var firstArea = instances.FirstOrDefault();
            if (firstArea?.CurrentScore != null)
                startTime = firstArea.CurrentScore.Replay.Frames.FirstOrDefault()?.Time ?? 0;

            masterClockContainer.Reset(startTime, true);
        }

        protected override void Update()
        {
            base.Update();
            checkAudioSource();

            if (firstFinishTime != null && Time.Current - firstFinishTime > max_wait_time && stableIpc.State.Value == TourneyState.Playing)
            {
                stableIpc.State.Value = TourneyState.Ranking;
            }
        }

        private void checkAudioSource()
        {
            var firstAvailable = instances.FirstOrDefault(i => i.SpectatorPlayerClock.IsRunning && !i.SpectatorPlayerClock.IsCatchingUp && !i.SpectatorPlayerClock.WaitingOnFrames);
            if (firstAvailable == currentAudioSource) return;

            currentAudioSource = firstAvailable;
            if (currentAudioSource != null) bindAudioAdjustments(currentAudioSource);
            foreach (var instance in instances) instance.Mute = instance != currentAudioSource;
        }

        private void bindAudioAdjustments(StablePlayerArea area)
        {
            if (boundAdjustments != null) masterClockContainer.AdjustmentsFromMods.UnbindAdjustments(boundAdjustments);
            boundAdjustments = area.ClockAdjustmentsFromMods;
            masterClockContainer.AdjustmentsFromMods.BindAdjustments(boundAdjustments);
        }

        private partial class StablePlayerArea : CompositeDrawable
        {
            public readonly StableSpectatorHandler Handler;
            public readonly SpectatorPlayerClock SpectatorPlayerClock;
            public IAggregateAudioAdjustment ClockAdjustmentsFromMods => clockAdjustmentsFromMods;
            public Score? CurrentScore { get; private set; }
            public int UserId => Handler.UserId;
            public bool IsFinished { get; private set; }
            public Action? OnFinished;

            private readonly AudioAdjustments clockAdjustmentsFromMods = new AudioAdjustments();
            private readonly BindableDouble volumeAdjustment = new BindableDouble();
            private readonly Container gameplayContent;
            private OsuScreenStack? stack;

            public StablePlayerArea(StableSpectatorHandler handler, SpectatorPlayerClock clock)
            {
                Handler = handler;
                SpectatorPlayerClock = clock;
                RelativeSizeAxes = Axes.Both;

                AudioContainer audioContainer;
                InternalChildren = new Drawable[]
                {
                    audioContainer = new AudioContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = gameplayContent = new Container { RelativeSizeAxes = Axes.Both },
                    }
                };
                audioContainer.AddAdjustment(AdjustableProperty.Volume, volumeAdjustment);
            }

            public bool Mute { set => volumeAdjustment.Value = value ? 0 : 1; }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                Handler.OnFramesReceived += _ => Schedule(checkAndLoadPlayer);
            }

            private void checkAndLoadPlayer()
            {
                if (CurrentScore != null || Handler.Beatmap.Value == null || Handler.Ruleset.Value == null) return;

                CurrentScore = new Score
                {
                    ScoreInfo = new ScoreInfo
                    {
                        User = new APIUser { Id = UserId },
                        BeatmapInfo = Handler.Beatmap.Value.BeatmapInfo,
                        Ruleset = Handler.Ruleset.Value,
                        Mods = Array.Empty<Mod>()
                    },
                    Replay = new Replay()
                };

                gameplayContent.Child = new PlayerIsolationContainer(Handler.Beatmap.Value, Handler.Ruleset.Value, Array.Empty<Mod>())
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = stack = new OsuScreenStack()
                };

                stack.Push(new PlayerLoader(() =>
                {
                    var p = new StableMultiSpectatorPlayer(CurrentScore, SpectatorPlayerClock, Handler);
                    p.PlayerFinished += () => { IsFinished = true; OnFinished?.Invoke(); };
                    return p;
                }));
            }

            private partial class PlayerIsolationContainer : Container
            {
                [Cached] [Cached(typeof(IBindable<RulesetInfo>))]
                private readonly Bindable<RulesetInfo> ruleset = new Bindable<RulesetInfo>();
                [Cached] [Cached(typeof(IBindable<WorkingBeatmap>))]
                private readonly Bindable<WorkingBeatmap> beatmap = new Bindable<WorkingBeatmap>();
                [Cached] [Cached(typeof(IBindable<IReadOnlyList<Mod>>))]
                private readonly Bindable<IReadOnlyList<Mod>> mods = new Bindable<IReadOnlyList<Mod>>();

                public PlayerIsolationContainer(WorkingBeatmap beatmap, RulesetInfo ruleset, IReadOnlyList<Mod> mods)
                {
                    this.beatmap.Value = beatmap; this.ruleset.Value = ruleset; this.mods.Value = mods;
                }

                protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
                {
                    var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
                    dependencies.CacheAs(ruleset.BeginLease(false));
                    dependencies.CacheAs(beatmap.BeginLease(false));
                    dependencies.CacheAs(mods.BeginLease(false));
                    return dependencies;
                }
            }
        }
    }
}
