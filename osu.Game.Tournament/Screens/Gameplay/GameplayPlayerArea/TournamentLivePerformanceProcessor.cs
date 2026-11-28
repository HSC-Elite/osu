// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Online.Spectator;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    internal partial class TournamentLivePerformanceProcessor : Component
    {
        public BindableLong PerformancePoints { get; } = new BindableLong();

        [Resolved]
        private ISpectatorDataSource spectatorDataSource { get; set; } = null!;

        [Resolved]
        private TournamentBeatmapDifficultyCache difficultyCache { get; set; } = null!;

        [Resolved]
        private TournamentBeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private MatchIPCInfo ipc { get; set; } = null!;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        private readonly int userId;
        private readonly IBindableDictionary<int, SpectatorState> watchedUserStates = new BindableDictionary<int, SpectatorState>();
        private readonly List<TimedFrame> frames = new List<TimedFrame>();

        private SpectatorState? spectatorState;
        private TournamentBeatmap? beatmap;
        private RulesetInfo? rulesetInfo;
        private APIMod[]? apiMods;
        private Mod[]? mods;
        private ScoreInfo? scoreInfo;
        private Ruleset? ruleset;
        private Task<List<TimedDifficultyAttributes>?>? timedDifficultyTask;
        private TimedFrame? lastCalculatedFrame;
        private bool listening;

        private bool isPpScoring => ipc.ScoringMode.Value == TournamentScoringMode.PerformancePoint;

        public TournamentLivePerformanceProcessor(int userId)
        {
            this.userId = userId;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            watchedUserStates.BindTo(spectatorDataSource.WatchedUserStates);
            spectatorDataSource.OnUserBeganPlaying += onUserBeganPlaying;
            spectatorDataSource.OnNewFrames += onNewFrames;
            listening = true;
        }

        public void SetReferenceClock(IClock clock)
        {
            referenceClock = clock;
        }

        private IClock? referenceClock;

        private void onUserBeganPlaying(int incomingUserId, SpectatorState state)
        {
            if (incomingUserId != userId || !isPpScoring)
                return;

            Schedule(() =>
            {
                if (isPpScoring)
                    reset(state);
            });
        }

        private void onNewFrames(int incomingUserId, FrameDataBundle bundle)
        {
            if (incomingUserId != userId || !isPpScoring || bundle.Frames.Count == 0)
                return;

            Schedule(() =>
            {
                if (!IsAlive || !isPpScoring)
                    return;

                if (spectatorState == null && watchedUserStates.TryGetValue(userId, out var state))
                    reset(state);

                if (spectatorState == null)
                    return;

                var frame = new TimedFrame(bundle.Frames[0].Time, bundle.Header);
                int insertionIndex = frames.BinarySearch(frame);

                if (insertionIndex >= 0)
                    frames[insertionIndex] = frame;
                else
                    frames.Insert(~insertionIndex, frame);
            });
        }

        protected override void Update()
        {
            base.Update();

            if (!isPpScoring
                || !watchedUserStates.TryGetValue(userId, out var currentState)
                || currentState.BeatmapID == null
                || currentState.RulesetID == null
                || currentState.State != SpectatedUserState.Playing)
            {
                if (spectatorState != null)
                    reset(null);

                return;
            }

            if (spectatorState == null || spectatorState.BeatmapID != currentState.BeatmapID || spectatorState.RulesetID != currentState.RulesetID)
                reset(currentState);
            else if (!ReferenceEquals(spectatorState, currentState))
            {
                spectatorState = currentState;
                lastCalculatedFrame = null;
            }

            if (frames.Count == 0)
                return;

            if (referenceClock == null)
                return;

            IClock clock = referenceClock;
            int frameIndex = frames.BinarySearch(new TimedFrame(clock.CurrentTime, null));

            if (frameIndex < 0)
                frameIndex = ~frameIndex;

            frameIndex = Math.Clamp(frameIndex - 1, 0, frames.Count - 1);

            TimedFrame currentFrame = frames[frameIndex];

            if (currentFrame == lastCalculatedFrame)
                return;

            FrameHeader header = currentFrame.Header!;

            if (!prepareDifficulty(currentState, header) || timedDifficultyTask?.IsCompleted != true)
                return;

            lastCalculatedFrame = currentFrame;

            if (!timedDifficultyTask.IsCompletedSuccessfully || ruleset == null)
                return;

            List<TimedDifficultyAttributes>? attributes = timedDifficultyTask.GetResultSafely();

            if (attributes == null || attributes.Count == 0)
                return;

            int attributeIndex = attributes.BinarySearch(new TimedDifficultyAttributes(currentFrame.Time, null));

            if (attributeIndex < 0)
                attributeIndex = ~attributeIndex - 1;

            DifficultyAttributes difficultyAttributes = attributes[Math.Clamp(attributeIndex, 0, attributes.Count - 1)].Attributes;
            if (scoreInfo == null)
            {
                try
                {
                    var workingBeatmap = beatmapManager.GetWorkingBeatmap(beatmap!);
                    scoreInfo = new ScoreInfo(workingBeatmap.BeatmapInfo, rulesetInfo!) { Mods = mods! };
                }
                catch (System.IO.FileNotFoundException)
                {
                    return;
                }
            }

            scoreInfo.TotalScore = header.TotalScore;
            scoreInfo.TotalScoreWithoutMods = header.TotalScoreWithoutMods ?? header.TotalScore;
            scoreInfo.Accuracy = header.Accuracy;
            scoreInfo.Combo = header.Combo;
            scoreInfo.MaxCombo = header.MaxCombo;
            scoreInfo.Statistics = header.Statistics;
            scoreInfo.MaximumStatistics = currentState.MaximumStatistics;

            // Some calculators retain intermediate results between calls.
            var calculator = ruleset.CreatePerformanceCalculator();
            if (calculator != null)
                PerformancePoints.Value = (long)Math.Round(calculator.Calculate(scoreInfo, difficultyAttributes).Total, MidpointRounding.AwayFromZero);
        }

        private bool prepareDifficulty(SpectatorState state, FrameHeader header)
        {
            int beatmapId = state.BeatmapID!.Value;
            int rulesetId = state.RulesetID!.Value;
            TournamentBeatmap? nextBeatmap = ipc.Beatmap.Value?.OnlineID == beatmapId
                ? ipc.Beatmap.Value
                : ladder.CurrentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(b => b.Beatmap?.OnlineID == beatmapId)?.Beatmap;
            RulesetInfo? nextRuleset = rulesets.GetRuleset(rulesetId);

            if (nextBeatmap == null || nextRuleset == null)
                return false;

            IEnumerable<APIMod> nextApiMods = header.Mods ?? state.Mods;

            if (beatmap?.OnlineID != nextBeatmap.OnlineID || rulesetInfo?.OnlineID != nextRuleset.OnlineID
                                                           || apiMods == null || !apiMods.SequenceEqual(nextApiMods))
            {
                ruleset = nextRuleset.CreateInstance();
                beatmap = nextBeatmap;
                rulesetInfo = nextRuleset;
                apiMods = nextApiMods.ToArray();
                mods = apiMods.Select(mod => mod.ToMod(ruleset)).ToArray();
                scoreInfo = null;
                timedDifficultyTask = difficultyCache.GetTimedDifficultyAttributesAsync(nextBeatmap, nextRuleset, mods);
            }

            return true;
        }

        private void reset(SpectatorState? state)
        {
            spectatorState = state;
            frames.Clear();
            beatmap = null;
            rulesetInfo = null;
            scoreInfo = null;
            ruleset = null;
            mods = null;
            apiMods = null;
            timedDifficultyTask = null;
            lastCalculatedFrame = null;
            PerformancePoints.Value = 0;
        }

        protected override void Dispose(bool isDisposing)
        {
            if (listening)
            {
                spectatorDataSource.OnUserBeganPlaying -= onUserBeganPlaying;
                spectatorDataSource.OnNewFrames -= onNewFrames;
                watchedUserStates.UnbindFrom(spectatorDataSource.WatchedUserStates);
            }

            base.Dispose(isDisposing);
        }

        private readonly record struct TimedFrame(double Time, FrameHeader? Header) : IComparable<TimedFrame>
        {
            public int CompareTo(TimedFrame other) => Time.CompareTo(other.Time);
        }
    }
}
