// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
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
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.IPC.MemoryIPC;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Online.Requests;
using osu.Game.Tournament.Online.Requests.Responses;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Provides the live or authoritative score for the current match without coupling scoring rules to a particular IPC implementation.
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

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        public BindableLong Score1 { get; } = new BindableLong();
        public BindableLong Score2 { get; } = new BindableLong();
        public BindableBool WaitingForAuthoritativeResult { get; } = new BindableBool();
        public BindableBool CurrentlyListening { get; } = new BindableBool();
        public Bindable<string> LastAPIRequestStatus { get; } = new Bindable<string>("未监听");

        public int CurrentMatchID => currentMatchID;
        public long CurrentApiGameID => currentGameID;
        public long LatestMatchEventID => matchEvents.LastOrDefault()?.Id ?? 0;

        private readonly Dictionary<DifficultyLookup, Task<DifficultyAttributes?>> difficultyTasks = new Dictionary<DifficultyLookup, Task<DifficultyAttributes?>>();
        private readonly HashSet<Task<DifficultyAttributes?>> loggedDifficultyFailures = new HashSet<Task<DifficultyAttributes?>>();

        private IProvideAdditionalData? additionalData;
        private readonly BindableList<APIMatchEvent> matchEvents = new BindableList<APIMatchEvent>();
        private int currentMatchID = -1;
        private long currentGameID = -1;
        private APIMatchGame? authoritativeGame;
        private bool apiRequestPending;
        private double timeSinceApiUpdate;
        private ScheduledDelegate? resultFetchTimeout;
        private bool scoresBoundToIPC;
        private WorkingBeatmap? workingBeatmap;
        private BeatmapLookup? workingBeatmapLookup;
        private double timeSinceUpdate;

        [BackgroundDependencyLoader]
        private void load()
        {
            additionalData = ipc as IProvideAdditionalData;

            if (additionalData == null)
                bindScoresToIPC();

            ipc.Beatmap.BindValueChanged(_ => resetDifficultyState());
            ladder.Ruleset.BindValueChanged(_ => resetDifficultyState());
            ladder.ScoringMode.BindValueChanged(_ => updateScores(), true);
            ipc.State.BindValueChanged(s =>
            {
                if (s.NewValue == TourneyState.Playing)
                    clearAuthoritativeGame();

                if (s.OldValue == TourneyState.Playing && s.NewValue == TourneyState.Ranking)
                    requestCurrentRoundResultFromApi();
            });
        }

        protected override void Update()
        {
            base.Update();

            timeSinceUpdate += Time.Elapsed;

            if (timeSinceUpdate >= update_interval)
            {
                timeSinceUpdate = 0;
                updateScores();
            }

            if (!api.IsLoggedIn || currentMatchID <= 0)
                return;

            timeSinceApiUpdate += Time.Elapsed;

            if (timeSinceApiUpdate >= 3000)
                fetchMatch();
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
            if (authoritativeGame != null && tryCalculateAuthoritativeScores(authoritativeGame, out long authoritativeScore1, out long authoritativeScore2))
            {
                unbindScoresFromIPC();
                Score1.Value = authoritativeScore1;
                Score2.Value = authoritativeScore2;
                WaitingForAuthoritativeResult.Value = false;
                return;
            }

            if (additionalData == null)
                return;

            long legacyScore1 = getTeamPlayers(TeamColour.Red).Sum(calculateLegacyScore);
            long legacyScore2 = getTeamPlayers(TeamColour.Blue).Sum(calculateLegacyScore);

            if (ladder.ScoringMode.Value == TournamentScoringMode.PerformancePoint
                && tryCalculatePerformanceScore(TeamColour.Red, out double performanceScore1)
                && tryCalculatePerformanceScore(TeamColour.Blue, out double performanceScore2))
            {
                Score1.Value = (long)Math.Round(performanceScore1);
                Score2.Value = (long)Math.Round(performanceScore2);
                return;
            }

            Score1.Value = legacyScore1;
            Score2.Value = legacyScore2;
        }

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

        public void StartListening(int? matchID)
        {
            if (!matchID.HasValue || matchID.Value <= 0)
                return;

            StopListening();
            currentMatchID = matchID.Value;
            CurrentlyListening.Value = true;
            LastAPIRequestStatus.Value = api.IsLoggedIn ? "等待首次请求" : "等待 API 登录";

            if (api.IsLoggedIn)
                fetchMatch();
        }

        public void StopListening()
        {
            currentMatchID = -1;
            CurrentlyListening.Value = false;
            WaitingForAuthoritativeResult.Value = false;
            resetApiState();
        }

        private void resetApiState()
        {
            matchEvents.Clear();
            currentGameID = -1;
            clearAuthoritativeGame();
            WaitingForAuthoritativeResult.Value = false;
            apiRequestPending = false;
            timeSinceApiUpdate = 0;
            resultFetchTimeout?.Cancel();
            resultFetchTimeout = null;

            if (currentMatchID <= 0)
                LastAPIRequestStatus.Value = "未监听";

            if (additionalData == null)
                bindScoresToIPC();

            updateScores();
        }

        private void clearAuthoritativeGame()
        {
            authoritativeGame = null;
            WaitingForAuthoritativeResult.Value = false;

            if (additionalData == null)
                bindScoresToIPC();
        }

        private void requestCurrentRoundResultFromApi()
        {
            if (!api.IsLoggedIn || currentMatchID <= 0 || authoritativeGame != null)
                return;

            WaitingForAuthoritativeResult.Value = true;
            resultFetchTimeout?.Cancel();
            resultFetchTimeout = Scheduler.AddDelayed(() =>
            {
                resultFetchTimeout = null;
                WaitingForAuthoritativeResult.Value = false;

                if (authoritativeGame == null)
                    Logger.Log("Match score API result was not available before the timeout.");
            }, 10000);

            fetchMatch();
        }

        private void fetchMatch()
        {
            if (!api.IsLoggedIn || currentMatchID <= 0 || apiRequestPending)
                return;

            timeSinceApiUpdate = 0;
            apiRequestPending = true;
            LastAPIRequestStatus.Value = "请求中";

            var request = new GetAPIMatchInfo(currentMatchID)
            {
                AfterEvent = matchEvents.LastOrDefault()?.Id,
            };

            request.Success += content =>
            {
                apiRequestPending = false;

                if (content.APIMatch.ID != currentMatchID
                    || (request.AfterEvent.HasValue && request.AfterEvent != matchEvents.LastOrDefault()?.Id))
                {
                    LastAPIRequestStatus.Value = "忽略过期响应";
                    return;
                }

                LastAPIRequestStatus.Value = "成功";
                processMatchUpdate(content);
            };

            request.Failure += _ =>
            {
                apiRequestPending = false;
                LastAPIRequestStatus.Value = "失败";
                Logger.Log("Match score API request failed.");
            };

            api.Queue(request);
        }

        private void processMatchUpdate(APIMatchInfo content)
        {
            long previousGameID = currentGameID;
            APIMatchEvent[] newEvents = content.Events
                                                 .Where(e => e.Game == null || e.Game.Scores.Count != 0)
                                                 .ExceptBy(matchEvents.Select(e => e.Id), e => e.Id)
                                                 .ToArray();

            matchEvents.AddRange(newEvents);

            if (previousGameID > 0 && content.CurrentGameID != previousGameID)
                applyAuthoritativeGame(findGame(previousGameID));

            if (content.CurrentGameID is long newGameID && newGameID != previousGameID)
            {
                currentGameID = newGameID;
                clearAuthoritativeGame();
                resultFetchTimeout?.Cancel();
                resultFetchTimeout = null;
                updateScores();
            }
            else if (content.CurrentGameID == null)
            {
                applyAuthoritativeGame(findGame(currentGameID));
            }

            if (currentGameID <= 0)
            {
                APIMatchGame? latestGame = findLatestGameForCurrentBeatmap();

                if (latestGame != null)
                {
                    currentGameID = latestGame.Id;
                    applyAuthoritativeGame(latestGame);
                }
            }

            if (matchEvents.Any(e => e.Detail.Type == MatchEventType.MatchDisbanded))
            {
                currentMatchID = -1;
                CurrentlyListening.Value = false;
                resetApiState();
            }
        }

        private APIMatchGame? findGame(long gameID) => matchEvents.LastOrDefault(e => e.Game?.Id == gameID && e.Game.Scores.Count != 0)?.Game;

        private APIMatchGame? findLatestGameForCurrentBeatmap()
        {
            int? beatmapID = ipc.Beatmap.Value?.OnlineID;

            return matchEvents.LastOrDefault(e => e.Game?.BeatmapId == beatmapID && e.Game.Scores.Count != 0)?.Game;
        }

        private void applyAuthoritativeGame(APIMatchGame? game)
        {
            if (game == null)
                return;

            authoritativeGame = game;
            resultFetchTimeout?.Cancel();
            resultFetchTimeout = null;
            updateScores();
        }

        private bool tryCalculateAuthoritativeScores(APIMatchGame game, out long score1, out long score2)
        {
            score1 = 0;
            score2 = 0;

            if (ladder.ScoringMode.Value == TournamentScoringMode.Score)
            {
                score1 = getApiTeamScore(TeamColour.Red, game);
                score2 = getApiTeamScore(TeamColour.Blue, game);
                return true;
            }

            if (!tryGetApiPerformanceScore(TeamColour.Red, game, out double performanceScore1)
                || !tryGetApiPerformanceScore(TeamColour.Blue, game, out double performanceScore2))
                return false;

            score1 = (long)Math.Round(performanceScore1);
            score2 = (long)Math.Round(performanceScore2);
            return true;
        }

        private long getApiTeamScore(TeamColour colour, APIMatchGame game)
        {
            int[] teamIDs = getTeamIDs(colour);
            Ruleset? ruleset = ladder.Ruleset.Value?.CreateInstance();

            return game.Scores.Where(s => teamIDs.Contains(s.UserID))
                              .Sum(s => calculateLegacyScore(s.TotalScore, getApiMods(game, s, ruleset)));
        }

        private bool tryGetApiPerformanceScore(TeamColour colour, APIMatchGame game, out double score)
        {
            score = 0;
            int[] teamIDs = getTeamIDs(colour);

            foreach (MatchScore apiScore in game.Scores.Where(s => teamIDs.Contains(s.UserID)))
            {
                double? playerScore = calculateApiPerformanceScore(game, apiScore);

                if (!playerScore.HasValue)
                    return false;

                score += playerScore.Value;
            }

            return true;
        }

        private double? calculateApiPerformanceScore(APIMatchGame game, MatchScore apiScore)
        {
            TournamentBeatmap? beatmap = getBeatmap(game.BeatmapId);
            RulesetInfo? rulesetInfo = rulesets.GetRuleset(apiScore.RulesetID);

            if (beatmap == null || rulesetInfo == null)
                return apiScore.PP;

            WorkingBeatmap? working = getWorkingBeatmap(beatmap);

            if (working == null)
                return apiScore.PP;

            ScoreInfo score = apiScore.ToScoreInfo(rulesets, working.BeatmapInfo);
            Ruleset ruleset = rulesetInfo.CreateInstance();
            LegacyMods mods = ruleset.ConvertToLegacyMods(score.Mods);
            Task<DifficultyAttributes?> difficultyTask = getDifficultyTask(beatmap, rulesetInfo, normaliseMods(mods));

            if (!difficultyTask.IsCompletedSuccessfully)
                return apiScore.PP;

            DifficultyAttributes? attributes = difficultyTask.GetResultSafely();
            PerformanceCalculator? performanceCalculator = ruleset.CreatePerformanceCalculator();

            if (attributes == null || performanceCalculator == null)
                return apiScore.PP;

            return performanceCalculator.Calculate(score, attributes).Total;
        }

        private TournamentBeatmap? getBeatmap(int beatmapID)
        {
            if (ipc.Beatmap.Value?.OnlineID == beatmapID)
                return ipc.Beatmap.Value;

            return ladder.CurrentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(b => b.ID == beatmapID)?.Beatmap;
        }

        private LegacyMods getApiMods(APIMatchGame game, MatchScore score, Ruleset? ruleset)
        {
            if (ruleset == null)
                return LegacyMods.None;

            LegacyMods mods = LegacyMods.None;

            foreach (string acronym in game.Mods ?? Enumerable.Empty<string>())
            {
                Mod? mod = ruleset.CreateModFromAcronym(acronym);

                if (mod != null)
                    mods |= ruleset.ConvertToLegacyMods(new[] { mod });
            }

            foreach (APIMod apiMod in score.Mods)
                mods |= ruleset.ConvertToLegacyMods(new[] { apiMod.ToMod(ruleset) });

            return mods;
        }

        private int[] getTeamIDs(TeamColour colour) =>
            ladder.CurrentMatch.Value?.GetTeamByColor(colour)?.Players.Select(p => p.OnlineID).ToArray() ?? Array.Empty<int>();

        private long calculateLegacyScore(SlotPlayerStatus player)
            => calculateLegacyScore(player.Score.Value, player.Mods.Value);

        private long calculateLegacyScore(long score, LegacyMods mods)
        {
            double multiplier = ladder.ModMultiplierSettings
                                      .Where(m => (m.Mods.Value & mods) > LegacyMods.None)
                                      .Aggregate(1.0, (value, setting) => value * setting.Multiplier.Value);

            return (long)(score * multiplier);
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
