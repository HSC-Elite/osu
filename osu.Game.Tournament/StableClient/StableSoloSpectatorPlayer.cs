// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Online.Spectator;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Replays.Types;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Play.Leaderboards;

namespace osu.Game.Tournament.StableClient
{
    public partial class StableSoloSpectatorPlayer : Player
    {
        public event Action? PlayerFinished;

        private readonly Score score;
        private readonly StableSpectatorHandler handler;
        private readonly Action exitToSpectator;
        private readonly Protocol.ReplayAction? initialReplayAction;
        private bool exitScheduled;

        [Cached(typeof(IGameplayLeaderboardProvider))]
        private readonly EmptyGameplayLeaderboardProvider leaderboardProvider = new EmptyGameplayLeaderboardProvider();

        public StableSoloSpectatorPlayer(Score score, StableSpectatorHandler handler, Action exitToSpectator, Protocol.ReplayAction? initialReplayAction)
            : base(new PlayerConfiguration { AllowUserInteraction = false })
        {
            this.score = score;
            this.handler = handler;
            this.exitToSpectator = exitToSpectator;
            this.initialReplayAction = initialReplayAction;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (LoadedBeatmapSuccessfully)
                ScoreProcessor.ApplyNewJudgementsWhenFailed = true;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            handler.OnFramesReceived += onNewFrames;

            if (initialReplayAction is Protocol.ReplayAction action)
                HandleReplayAction(action);

            Logger.Log($"StableSoloSpectatorPlayer: load complete, initial replay frame count={score.Replay.Frames.Count}");

            if (score.Replay.Frames.Count > 0)
            {
                SetGameplayStartTime(score.Replay.Frames[0].Time);
                Logger.Log($"StableSoloSpectatorPlayer: gameplay start time set to {score.Replay.Frames[0].Time} from buffered replay");
            }
        }

        private void onNewFrames(FrameDataBundle bundle)
        {
            if (!LoadedBeatmapSuccessfully)
                return;

            Schedule(() =>
            {
                bool isFirstBundle = score.Replay.Frames.Count == 0;
                ReplayFrame? lastFrame = score.Replay.Frames.LastOrDefault();
                int frameCountBefore = score.Replay.Frames.Count;
                double lastFrameTime = lastFrame?.Time ?? double.NegativeInfinity;
                int droppedFrameCount = 0;
                double firstDroppedFrameTime = double.NegativeInfinity;
                double lastDroppedFrameTime = double.NegativeInfinity;
                double replayTailAtFirstDrop = double.NegativeInfinity;

                foreach (var frame in bundle.Frames)
                {
                    if (frame.Time < lastFrameTime)
                    {
                        if (droppedFrameCount == 0)
                        {
                            firstDroppedFrameTime = frame.Time;
                            replayTailAtFirstDrop = lastFrameTime;
                        }

                        lastDroppedFrameTime = frame.Time;
                        droppedFrameCount++;
                        continue;
                    }

                    IConvertibleReplayFrame convertibleFrame = GameplayState.Ruleset.CreateConvertibleReplayFrame()!;
                    convertibleFrame.FromLegacy(frame, GameplayState.Beatmap, lastFrame);

                    var convertedFrame = (ReplayFrame)convertibleFrame;
                    convertedFrame.Time = frame.Time;
                    convertedFrame.Header = frame.Header;

                    score.Replay.Frames.Add(convertedFrame);
                    lastFrame = convertedFrame;
                    lastFrameTime = convertedFrame.Time;
                }

                int appendedFrameCount = score.Replay.Frames.Count - frameCountBefore;

                if (droppedFrameCount > 0)
                {
                    Logger.Log(
                        $"StableSoloSpectatorPlayer: dropped {droppedFrameCount} out-of-order frames from bundle " +
                        $"(firstTime={firstDroppedFrameTime}, lastTime={lastDroppedFrameTime}, replayTail={replayTailAtFirstDrop}).");
                }

                if (appendedFrameCount > 0)
                {
                    Logger.Log(
                        $"StableSoloSpectatorPlayer: appended {appendedFrameCount} replay frames " +
                        $"from bundle (total={score.Replay.Frames.Count}, score={bundle.Header.TotalScore}, acc={bundle.Header.Accuracy:P2})");
                }

                if (isFirstBundle && score.Replay.Frames.Count > 0)
                {
                    SetGameplayStartTime(score.Replay.Frames[0].Time);
                    Logger.Log($"StableSoloSpectatorPlayer: gameplay start time set to {score.Replay.Frames[0].Time} from live bundle");
                }
            });
        }

        protected override Score CreateScore(IBeatmap beatmap) => score;

        protected override void PrepareReplay()
        {
            DrawableRuleset?.SetReplayScore(score);
        }

        internal void HandleReplayAction(Protocol.ReplayAction action)
        {
            if (exitScheduled)
                return;

            switch (action)
            {
                case Protocol.ReplayAction.Completion:
                case Protocol.ReplayAction.Fail:
                    exitScheduled = true;
                    Schedule(() => Schedule(exitToSpectator));
                    break;

                case Protocol.ReplayAction.NewSong:
                case Protocol.ReplayAction.SongSelect:
                case Protocol.ReplayAction.WatchingOther:
                    exitScheduled = true;
                    Schedule(exitToSpectator);
                    break;
            }
        }

        protected override ResultsScreen CreateResults(ScoreInfo score)
        {
            Schedule(() => PlayerFinished?.Invoke());
            return new EmptyResultsScreen(score);
        }

        protected override void PerformFail()
        {
            ScoreProcessor.FailScore(score.ScoreInfo);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            handler.OnFramesReceived -= onNewFrames;
            return base.OnExiting(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            handler.OnFramesReceived -= onNewFrames;
            base.Dispose(isDisposing);
        }

        private partial class EmptyResultsScreen : ResultsScreen
        {
            public EmptyResultsScreen(ScoreInfo score)
                : base(score)
            {
            }

            [BackgroundDependencyLoader]
            private void load() => this.Hide();
        }

        private partial class EmptyGameplayLeaderboardProvider : Component, IGameplayLeaderboardProvider
        {
            public IBindableList<GameplayLeaderboardScore> Scores => new BindableList<GameplayLeaderboardScore>();
        }
    }
}
