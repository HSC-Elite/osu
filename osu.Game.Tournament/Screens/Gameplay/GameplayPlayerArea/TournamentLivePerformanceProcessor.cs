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
        private WorkingBeatmap? workingBeatmap;
        private Mod[]? mods;
        private string? modsKey;
        private Task<List<TimedDifficultyAttributes>?>? timedDifficultyTask;
        private int currentFrameIndex = -1;
        private bool needsCalculation;
        private bool listening;

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
            if (incomingUserId != userId)
                return;

            Schedule(() => reset(state));
        }

        private void onNewFrames(int incomingUserId, FrameDataBundle bundle)
        {
            if (incomingUserId != userId || bundle.Frames.Count == 0)
                return;

            Schedule(() =>
            {
                if (!IsAlive)
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

                needsCalculation = true;
            });
        }

        protected override void Update()
        {
            base.Update();

            if (!watchedUserStates.TryGetValue(userId, out var currentState)
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
            else
                spectatorState = currentState;

            if (frames.Count == 0)
                return;

            if (referenceClock == null)
                return;

            IClock clock = referenceClock;
            int frameIndex = frames.BinarySearch(new TimedFrame(clock.CurrentTime, null));

            if (frameIndex < 0)
                frameIndex = ~frameIndex;

            frameIndex = Math.Clamp(frameIndex - 1, 0, frames.Count - 1);

            if (frameIndex != currentFrameIndex)
            {
                currentFrameIndex = frameIndex;
                needsCalculation = true;
            }

            if (!needsCalculation)
                return;

            TimedFrame currentFrame = frames[frameIndex];

            FrameHeader header = currentFrame.Header!;

            if (!prepareDifficulty(currentState, header))
                return;

            if (timedDifficultyTask == null || !timedDifficultyTask.IsCompleted)
                return;

            needsCalculation = false;

            if (!timedDifficultyTask.IsCompletedSuccessfully || workingBeatmap == null || rulesetInfo == null || mods == null)
                return;

            List<TimedDifficultyAttributes>? attributes = timedDifficultyTask.GetResultSafely();

            if (attributes == null || attributes.Count == 0)
                return;

            if (workingBeatmap == null)
            {
                try
                {
                    workingBeatmap = beatmapManager.GetWorkingBeatmap(beatmap!);
                }
                catch (System.IO.FileNotFoundException)
                {
                    return;
                }
            }

            int attributeIndex = attributes.BinarySearch(new TimedDifficultyAttributes(currentFrame.Time, null));

            if (attributeIndex < 0)
                attributeIndex = ~attributeIndex - 1;

            DifficultyAttributes difficultyAttributes = attributes[Math.Clamp(attributeIndex, 0, attributes.Count - 1)].Attributes;
            ScoreInfo scoreInfo = createScoreInfo(header, currentState, workingBeatmap, rulesetInfo, mods);
            PerformanceCalculator? calculator = rulesetInfo.CreateInstance().CreatePerformanceCalculator();

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

            Ruleset ruleset = nextRuleset.CreateInstance();
            APIMod[] apiMods = header.Mods ?? state.Mods.ToArray();
            Mod[] nextMods = apiMods.Select(mod => mod.ToMod(ruleset)).ToArray();
            string nextModsKey = string.Join("|", apiMods.OrderBy(mod => mod.Acronym).Select(mod => mod.ToString()));

            if (beatmap?.OnlineID != nextBeatmap.OnlineID || rulesetInfo?.OnlineID != nextRuleset.OnlineID || modsKey != nextModsKey)
            {
                beatmap = nextBeatmap;
                rulesetInfo = nextRuleset;
                workingBeatmap = null;
                mods = nextMods;
                modsKey = nextModsKey;
                timedDifficultyTask = difficultyCache.GetTimedDifficultyAttributesAsync(nextBeatmap, nextRuleset, nextMods);
                needsCalculation = true;
            }

            return true;
        }

        private static ScoreInfo createScoreInfo(FrameHeader header, SpectatorState state, WorkingBeatmap beatmap, RulesetInfo ruleset, Mod[] mods)
            => new ScoreInfo(beatmap.BeatmapInfo, ruleset)
            {
                Mods = mods,
                TotalScore = header.TotalScore,
                TotalScoreWithoutMods = header.TotalScoreWithoutMods ?? header.TotalScore,
                Accuracy = header.Accuracy,
                Combo = header.Combo,
                MaxCombo = header.MaxCombo,
                Statistics = header.Statistics,
                MaximumStatistics = state.MaximumStatistics,
            };

        private void reset(SpectatorState? state)
        {
            spectatorState = state;
            frames.Clear();
            beatmap = null;
            rulesetInfo = null;
            workingBeatmap = null;
            mods = null;
            modsKey = null;
            timedDifficultyTask = null;
            currentFrameIndex = -1;
            needsCalculation = true;
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
