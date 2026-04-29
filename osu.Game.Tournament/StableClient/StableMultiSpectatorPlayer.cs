// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Online.Spectator;
using osu.Game.Screens.OnlinePlay.Multiplayer.Spectate;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Replays.Types;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Select.Leaderboards;

namespace osu.Game.Tournament.StableClient
{
    /// <summary>
    /// 专门用于 Stable 观战桥接的玩家实例。
    /// 它不依赖 SpectatorClient，而是直接接收来自 StableSpectatorHandler 的帧。
    /// </summary>
    public partial class StableMultiSpectatorPlayer : Player
    {
        public event Action? PlayerFinished;

        private readonly Score score;
        private readonly SpectatorPlayerClock spectatorPlayerClock;
        private readonly StableSpectatorHandler handler;

        // 屏蔽局部排行榜，因为锦标赛界面有全局排行榜
        [Cached(typeof(IGameplayLeaderboardProvider))]
        private readonly EmptyGameplayLeaderboardProvider leaderboardProvider = new EmptyGameplayLeaderboardProvider();

        public StableMultiSpectatorPlayer(Score score, SpectatorPlayerClock spectatorPlayerClock, StableSpectatorHandler handler)
            : base(new PlayerConfiguration { AllowUserInteraction = false })
        {
            this.score = score;
            this.spectatorPlayerClock = spectatorPlayerClock;
            this.handler = handler;
        }

        [BackgroundDependencyLoader]
        private void load(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (!LoadedBeatmapSuccessfully)
                return;

            // 即使失败也继续同步判定（锦标赛观战常见需求）
            ScoreProcessor.ApplyNewJudgementsWhenFailed = true;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // 绑定帧接收事件
            handler.OnFramesReceived += onNewFrames;

            // 监听规则集时钟状态，处理 UserPlaybackRate 调整
            DrawableRuleset.FrameStableClock.WaitingOnFrames.BindValueChanged(waiting =>
            {
                if (GameplayClockContainer is MasterGameplayClockContainer master)
                {
                    if (master.UserPlaybackRate.Value > 1 && waiting.NewValue)
                        master.UserPlaybackRate.Value = 1;
                }
            }, true);
        }

        private void onNewFrames(FrameDataBundle bundle)
        {
            if (!LoadedBeatmapSuccessfully)
                return;

            Schedule(() =>
            {
                bool isFirstBundle = score.Replay.Frames.Count == 0;

                foreach (var frame in bundle.Frames)
                {
                    IConvertibleReplayFrame convertibleFrame = GameplayState.Ruleset.CreateConvertibleReplayFrame()!;
                    convertibleFrame.FromLegacy(frame, GameplayState.Beatmap);

                    var convertedFrame = (ReplayFrame)convertibleFrame;
                    convertedFrame.Time = frame.Time;
                    convertedFrame.Header = frame.Header;

                    score.Replay.Frames.Add(convertedFrame);
                }

                if (isFirstBundle && score.Replay.Frames.Count > 0)
                    SetGameplayStartTime(score.Replay.Frames[0].Time);
            });
        }

        protected override void Update()
        {
            // 同步外部时钟的运行状态到本地 Gameplay 容器
            if (GameplayClockContainer.IsRunning)
                GameplayClockContainer.Start();
            else
                GameplayClockContainer.Stop();

            base.Update();
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            // 更新外部时钟的 Waiting 状态，用于同步管理器判断所有玩家是否就绪
            spectatorPlayerClock.WaitingOnFrames = DrawableRuleset.FrameStableClock.WaitingOnFrames.Value || score.Replay.Frames.Count == 0;
        }

        protected override GameplayClockContainer CreateGameplayClockContainer(WorkingBeatmap beatmap, double gameplayStart)
        {
            // 使用外部传入的 SpectatorPlayerClock 作为底层时钟，且不应用 decoupling
            return new GameplayClockContainer(spectatorPlayerClock, applyOffsets: false, requireDecoupling: false);
        }

        protected override Score CreateScore(IBeatmap beatmap) => score;

        protected override void PrepareReplay()
        {
            DrawableRuleset?.SetReplayScore(score);
        }

        protected override ResultsScreen CreateResults(ScoreInfo score)
        {
            Schedule(() => PlayerFinished?.Invoke());
            return new EmptyResultsScreen(score);
        }

        protected override void PerformFail()
        {
            // 锦标赛观战不直接跳出，仅标记失败
            ScoreProcessor.FailScore(score.ScoreInfo);
        }

        protected override void Dispose(bool isDisposing)
        {
            handler.OnFramesReceived -= onNewFrames;
            base.Dispose(isDisposing);
        }

        private partial class EmptyResultsScreen : ResultsScreen
        {
            public EmptyResultsScreen(ScoreInfo score) : base(score) { }
            [BackgroundDependencyLoader] private void load() => this.Hide();
        }

        private partial class EmptyGameplayLeaderboardProvider : Component, IGameplayLeaderboardProvider
        {
            public IBindableList<GameplayLeaderboardScore> Scores => new BindableList<GameplayLeaderboardScore>();
        }
    }
}
