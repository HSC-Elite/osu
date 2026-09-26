// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.IPC.MemoryIPC;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Calculates the live score for the current match without coupling the scoring rules to a particular IPC implementation.
    /// </summary>
    public partial class TournamentMatchScoreProcessor : Component
    {
        private const double update_interval = 1000.0 / 5;

        [Resolved]
        private MatchIPCInfo ipc { get; set; } = null!;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        [Resolved]
        private TournamentBeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private TournamentBeatmapDifficultyCache difficultyCache { get; set; } = null!;

        private readonly Dictionary<DifficultyLookup, Task<DifficultyAttributes?>> difficultyTasks = new Dictionary<DifficultyLookup, Task<DifficultyAttributes?>>();
        private readonly HashSet<Task<DifficultyAttributes?>> loggedDifficultyFailures = new HashSet<Task<DifficultyAttributes?>>();

        private IProvideAdditionalData? additionalData;
        private WorkingBeatmap? workingBeatmap;
        private BeatmapLookup? workingBeatmapLookup;
        private double timeSinceUpdate;

        [BackgroundDependencyLoader]
        private void load()
        {
            ipc.Beatmap.BindValueChanged(_ => resetDifficultyState());
            ladder.Ruleset.BindValueChanged(_ => resetDifficultyState());
            ladder.ScoringMode.BindValueChanged(_ => updateScores(), true);
        }

        protected override void Update()
        {
            base.Update();

            additionalData ??= ipc as IProvideAdditionalData;

            if (additionalData == null)
                return;

            timeSinceUpdate += Time.Elapsed;

            if (timeSinceUpdate < update_interval)
                return;

            timeSinceUpdate = 0;
            updateScores();
        }

        private void resetDifficultyState()
        {
            difficultyTasks.Clear();
            loggedDifficultyFailures.Clear();
            workingBeatmap = null;
            workingBeatmapLookup = null;

            updateScores();
        }

        private void updateScores()
        {
            if (additionalData == null)
                return;

            long legacyScore1 = getTeamPlayers(TeamColour.Red).Sum(calculateLegacyScore);
            long legacyScore2 = getTeamPlayers(TeamColour.Blue).Sum(calculateLegacyScore);

            if (ladder.ScoringMode.Value == TournamentScoringMode.PerformancePoint
                && tryCalculatePerformanceScore(TeamColour.Red, out double performanceScore1)
                && tryCalculatePerformanceScore(TeamColour.Blue, out double performanceScore2))
            {
                ipc.Score1.Value = (long)Math.Round(performanceScore1);
                ipc.Score2.Value = (long)Math.Round(performanceScore2);
                return;
            }

            ipc.Score1.Value = legacyScore1;
            ipc.Score2.Value = legacyScore2;
        }

        private long calculateLegacyScore(SlotPlayerStatus player)
        {
            double multiplier = ladder.ModMultiplierSettings
                                      .Where(m => (m.Mods.Value & player.Mods.Value) > LegacyMods.None)
                                      .Aggregate(1.0, (value, setting) => value * setting.Multiplier.Value);

            return (long)(player.Score.Value * multiplier);
        }

        private bool tryCalculatePerformanceScore(TeamColour colour, out double score)
        {
            score = 0;

            TournamentBeatmap? beatmap = ipc.Beatmap.Value;
            RulesetInfo? rulesetInfo = ladder.Ruleset.Value;

            if (beatmap == null || rulesetInfo == null)
                return false;

            foreach (SlotPlayerStatus player in getTeamPlayers(colour))
            {
                double? playerScore = calculatePerformanceScore(beatmap, rulesetInfo, player);

                if (!playerScore.HasValue)
                    return false;

                score += playerScore.Value;
            }

            return true;
        }

        private double? calculatePerformanceScore(TournamentBeatmap beatmap, RulesetInfo rulesetInfo, SlotPlayerStatus player)
        {
            if (!beatmapManager.HasBeatmap(beatmap))
                return null;

            Ruleset ruleset = rulesetInfo.CreateInstance();
            PerformanceCalculator? performanceCalculator = ruleset.CreatePerformanceCalculator();

            if (performanceCalculator == null)
                return null;

            LegacyMods mods = normaliseMods(player.Mods.Value);
            Task<DifficultyAttributes?> difficultyTask = getDifficultyTask(beatmap, rulesetInfo, mods);

            if (!difficultyTask.IsCompletedSuccessfully)
            {
                if (difficultyTask.IsFaulted)
                {
                    if (loggedDifficultyFailures.Add(difficultyTask))
                        Logger.Error(difficultyTask.Exception, "Difficulty task failed");
                }

                return null;
            }

            DifficultyAttributes? difficultyAttributes = difficultyTask.GetResultSafely();

            if (difficultyAttributes == null)
                return null;

            WorkingBeatmap? working = getWorkingBeatmap(beatmap);

            if (working == null)
                return null;

            var score = new ScoreInfo(working.BeatmapInfo, rulesetInfo)
            {
                Accuracy = Math.Clamp(player.Accuracy.Value, 0, 1),
                Combo = Math.Max(0, player.Combo.Value),
                MaxCombo = Math.Max(0, player.MaxCombo.Value),
                TotalScore = Math.Max(0, player.Score.Value),
                IsLegacyScore = true,
                LegacyTotalScore = Math.Max(0, player.Score.Value),
                Mods = ruleset.ConvertFromLegacyMods(mods).ToArray(),
            };

            score.SetCount300(Math.Max(0, player.Hit300.Value));
            score.SetCount100(Math.Max(0, player.Hit100.Value));
            score.SetCount50(Math.Max(0, player.Hit50.Value));
            score.SetCountGeki(Math.Max(0, player.HitGeki.Value));
            score.SetCountKatu(Math.Max(0, player.HitKatu.Value));
            score.SetCountMiss(Math.Max(0, player.HitMiss.Value));

            return performanceCalculator.Calculate(score, difficultyAttributes).Total;
        }

        private Task<DifficultyAttributes?> getDifficultyTask(TournamentBeatmap beatmap, RulesetInfo rulesetInfo, LegacyMods mods)
        {
            var lookup = new DifficultyLookup(beatmap.OnlineID, beatmap.MD5Hash, rulesetInfo.ShortName, mods);

            if (difficultyTasks.TryGetValue(lookup, out Task<DifficultyAttributes?>? task))
                return task;

            task = difficultyCache.GetDifficultyAsync(beatmap, rulesetInfo, mods, downloadIfMissing: false);
            difficultyTasks.Add(lookup, task);
            return task;
        }

        private WorkingBeatmap? getWorkingBeatmap(TournamentBeatmap beatmap)
        {
            var lookup = new BeatmapLookup(beatmap.OnlineID, beatmap.MD5Hash);

            if (workingBeatmap != null && workingBeatmapLookup == lookup)
                return workingBeatmap;

            workingBeatmapLookup = lookup;

            try
            {
                return workingBeatmap = beatmapManager.GetWorkingBeatmap(beatmap);
            }
            catch (FileNotFoundException)
            {
                workingBeatmap = null;
                return null;
            }
        }

        private IEnumerable<SlotPlayerStatus> getTeamPlayers(TeamColour colour)
        {
            int[] teamIds = ladder.CurrentMatch.Value?.GetTeamByColor(colour)?.Players.Select(p => p.OnlineID).ToArray() ?? Array.Empty<int>();

            return additionalData!.SlotPlayers.Where(s => teamIds.Contains(s.OnlineID.Value));
        }

        private static LegacyMods normaliseMods(LegacyMods mods)
        {
            mods &= ~LegacyMods.FreeMod;
            mods &= ~LegacyMods.NoMod;
            return mods;
        }

        private readonly record struct DifficultyLookup(int OnlineID, string MD5Hash, string RulesetShortName, LegacyMods Mods);

        private readonly record struct BeatmapLookup(int OnlineID, string MD5Hash);
    }
}
