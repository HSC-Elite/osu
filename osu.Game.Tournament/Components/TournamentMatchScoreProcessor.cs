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
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Components
{
    public partial class TournamentMatchScoreProcessor : Component
    {
        [Resolved]
        private MatchIPCInfo ipc { get; set; } = null!;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private TournamentBeatmapDifficultyCache difficultyCache { get; set; } = null!;

        public BindableLong Score1 { get; } = new BindableLong();
        public BindableLong Score2 { get; } = new BindableLong();
        public BindableBool WaitingForAuthoritativeResult { get; } = new BindableBool();
        public BindableBool CurrentlyListening { get; } = new BindableBool();
        public Bindable<string> LastResultRequestStatus { get; } = new Bindable<string>("未请求");

        private IReadOnlyList<TournamentPlayerScoreResult>? authoritativeScores;
        private readonly Dictionary<int, Task<DifficultyAttributes?>> finalDifficultyTasks = new Dictionary<int, Task<DifficultyAttributes?>>();
        private bool scoresBoundToIPC;
        private int requestGeneration;
        private ScheduledDelegate? resultFetchTimeout;
        private ScheduledDelegate? resultRetry;

        [BackgroundDependencyLoader]
        private void load()
        {
            bindScoresToIPC();
            CurrentlyListening.BindTo(ipc.HasActiveMatch);
            ladder.ScoringMode.BindValueChanged(_ => updateScores(), true);

            ipc.State.BindValueChanged(state =>
            {
                if (state.NewValue == TourneyState.Playing)
                    clearAuthoritativeScores();

                if (state.OldValue == TourneyState.Playing && state.NewValue == TourneyState.Ranking)
                    requestAuthoritativeScores();
            });

            if (ipc.State.Value == TourneyState.Ranking)
                requestAuthoritativeScores();
        }

        private void requestAuthoritativeScores()
        {
            if (!ipc.HasActiveMatch.Value || WaitingForAuthoritativeResult.Value)
                return;

            requestGeneration++;
            int generation = requestGeneration;
            WaitingForAuthoritativeResult.Value = true;
            LastResultRequestStatus.Value = "请求中";
            resultFetchTimeout?.Cancel();
            resultRetry?.Cancel();
            resultFetchTimeout = Scheduler.AddDelayed(() =>
            {
                resultFetchTimeout = null;
                resultRetry?.Cancel();
                resultRetry = null;
                requestGeneration++;
                authoritativeScores = null;
                finalDifficultyTasks.Clear();
                bindScoresToIPC();
                WaitingForAuthoritativeResult.Value = false;
                LastResultRequestStatus.Value = "超时";
                Logger.Log("Authoritative match scores were not available before the timeout.");
            }, 10000);

            requestAuthoritativeScores(generation);
        }

        private void requestAuthoritativeScores(int generation)
        {
            ipc.RequestAuthoritativeScores(scores => Schedule(() =>
            {
                if (generation != requestGeneration || !WaitingForAuthoritativeResult.Value || ipc.State.Value != TourneyState.Ranking || !ipc.HasActiveMatch.Value)
                    return;

                if (scores == null || scores.Count == 0)
                {
                    LastResultRequestStatus.Value = scores == null ? "请求失败，重试中" : "暂无结果，重试中";
                    resultRetry = Scheduler.AddDelayed(() =>
                    {
                        resultRetry = null;
                        requestAuthoritativeScores(generation);
                    }, 1000);
                    return;
                }

                authoritativeScores = scores;
                LastResultRequestStatus.Value = "收到结果";
                updateScores();
            }));
        }

        private void clearAuthoritativeScores()
        {
            requestGeneration++;
            resultFetchTimeout?.Cancel();
            resultFetchTimeout = null;
            resultRetry?.Cancel();
            resultRetry = null;
            authoritativeScores = null;
            finalDifficultyTasks.Clear();
            WaitingForAuthoritativeResult.Value = false;
            bindScoresToIPC();
        }

        private bool updateScores()
        {
            if (authoritativeScores != null && tryCalculateAuthoritativeScores(authoritativeScores, out long score1, out long score2))
            {
                unbindScoresFromIPC();
                Score1.Value = score1;
                Score2.Value = score2;
                resultFetchTimeout?.Cancel();
                resultFetchTimeout = null;
                resultRetry?.Cancel();
                resultRetry = null;
                WaitingForAuthoritativeResult.Value = false;
                LastResultRequestStatus.Value = "成功";
                return true;
            }

            bindScoresToIPC();
            if (authoritativeScores != null)
                LastResultRequestStatus.Value = "计算结果中";

            return false;
        }

        private bool tryCalculateAuthoritativeScores(IReadOnlyList<TournamentPlayerScoreResult> scores, out long score1, out long score2)
        {
            score1 = 0;
            score2 = 0;

            int[] redTeamIds = getTeamIds(TeamColour.Red);
            int[] blueTeamIds = getTeamIds(TeamColour.Blue);

            if (ladder.ScoringMode.Value == TournamentScoringMode.Score)
            {
                score1 = scores.Where(s => redTeamIds.Contains(s.UserId)).Sum(s => calculateLegacyScore(s.ScoreInfo));
                score2 = scores.Where(s => blueTeamIds.Contains(s.UserId)).Sum(s => calculateLegacyScore(s.ScoreInfo));
                return true;
            }

            if (!tryGetTeamPerformanceScore(scores, redTeamIds, out double performanceScore1)
                || !tryGetTeamPerformanceScore(scores, blueTeamIds, out double performanceScore2))
                return false;

            score1 = (long)Math.Round(performanceScore1);
            score2 = (long)Math.Round(performanceScore2);
            return true;
        }

        private bool tryGetTeamPerformanceScore(IReadOnlyList<TournamentPlayerScoreResult> scores, int[] teamIds, out double total)
        {
            total = 0;

            foreach (TournamentPlayerScoreResult player in scores.Where(s => teamIds.Contains(s.UserId)))
            {
                double? performanceScore = getPerformanceScore(player);

                if (!performanceScore.HasValue)
                    return false;

                total += performanceScore.Value;
            }

            return true;
        }

        private double? getPerformanceScore(TournamentPlayerScoreResult player)
        {
            ScoreInfo score = player.ScoreInfo;

            if (score.PP.HasValue)
                return score.PP.Value;

            if (!finalDifficultyTasks.TryGetValue(player.UserId, out Task<DifficultyAttributes?>? difficultyTask))
            {
                int beatmapId = (int)(score.BeatmapInfo?.OnlineID ?? -1);
                TournamentBeatmap? beatmap = beatmapId > 0 ? getBeatmap(beatmapId) : null;
                RulesetInfo? rulesetInfo = rulesets.GetRuleset(score.Ruleset.OnlineID);

                if (beatmap == null || rulesetInfo == null)
                    return null;

                Ruleset ruleset = rulesetInfo.CreateInstance();
                LegacyMods mods = ruleset.ConvertToLegacyMods(score.Mods);
                difficultyTask = difficultyCache.GetDifficultyAsync(beatmap, rulesetInfo, mods, downloadIfMissing: false);
                finalDifficultyTasks[player.UserId] = difficultyTask;

                int generation = requestGeneration;
                difficultyTask.ContinueWith(_ => Schedule(() =>
                {
                    if (generation == requestGeneration && IsAlive)
                        updateScores();
                }));
            }

            if (!difficultyTask.IsCompletedSuccessfully)
                return null;

            DifficultyAttributes? attributes = difficultyTask.GetResultSafely();
            PerformanceCalculator? calculator = rulesets.GetRuleset(score.Ruleset.OnlineID)?.CreateInstance().CreatePerformanceCalculator();

            return attributes != null && calculator != null ? calculator.Calculate(score, attributes).Total : null;
        }

        private TournamentBeatmap? getBeatmap(int beatmapId)
        {
            if (ipc.Beatmap.Value?.OnlineID == beatmapId)
                return ipc.Beatmap.Value;

            return ladder.CurrentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(b => b.Beatmap?.OnlineID == beatmapId)?.Beatmap;
        }

        private long calculateLegacyScore(ScoreInfo score)
        {
            Ruleset? ruleset = rulesets.GetRuleset(score.Ruleset.OnlineID)?.CreateInstance();
            LegacyMods mods = ruleset?.ConvertToLegacyMods(score.Mods) ?? LegacyMods.None;
            double multiplier = ladder.ModMultiplierSettings
                                      .Where(m => (TournamentGameBase.ConvertFromAcronym(m.ModAcronym.Value) & mods) > LegacyMods.None)
                                      .Aggregate(1.0, (value, setting) => value * setting.Multiplier.Value);

            return (long)(score.TotalScore * multiplier);
        }

        private int[] getTeamIds(TeamColour colour)
            => ladder.CurrentMatch.Value?.GetTeamByColor(colour)?.Players.Select(player => player.OnlineID).ToArray() ?? Array.Empty<int>();

        private void bindScoresToIPC()
        {
            if (scoresBoundToIPC)
                return;

            Score1.BindTo(ipc.Score1);
            Score2.BindTo(ipc.Score2);
            scoresBoundToIPC = true;
        }

        private void unbindScoresFromIPC()
        {
            if (!scoresBoundToIPC)
                return;

            Score1.UnbindFrom(ipc.Score1);
            Score2.UnbindFrom(ipc.Score2);
            scoresBoundToIPC = false;
        }

        protected override void Dispose(bool isDisposing)
        {
            requestGeneration++;
            resultFetchTimeout?.Cancel();
            resultRetry?.Cancel();
            CurrentlyListening.UnbindFrom(ipc.HasActiveMatch);
            unbindScoresFromIPC();
            base.Dispose(isDisposing);
        }
    }
}
