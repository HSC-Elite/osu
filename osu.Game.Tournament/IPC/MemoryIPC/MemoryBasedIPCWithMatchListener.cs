// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Online.API;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Online.Requests;
using osu.Game.Tournament.Online.Requests.Responses;

namespace osu.Game.Tournament.IPC.MemoryIPC
{
    [SupportedOSPlatform("windows")]
    public partial class MemoryBasedIPCWithMatchListener : MemoryBasedIPC
    {
        private int currentMatch = -1;
        private long abortedEventId = 0;
        private long currentGameID = -1;

        private double waitTime;
        private BeatmapChoice? currentChoice;

        public event Action<bool>? MatchFinished;
        public event Action? FetchFailed;
        public event Action? MatchAborted;

        public bool LastFetchSuccess { get; private set; }

        // we just need fetch when game finished.
        private const int refresh_interval = 3000;

        internal double ResultFetchTimeout { get; set; } = 10_000;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        public IBindableList<APIMatchEvent> Events => events;

        private readonly BindableList<APIMatchEvent> events = new BindableList<APIMatchEvent>();

        public IBindable<bool> CurrentlyListening => currentlyListening;

        private readonly BindableBool currentlyListening = new BindableBool();

        public IBindable<bool> CurrentlyPlaying => currentlyPlaying;

        private readonly BindableBool currentlyPlaying = new BindableBool();

        public IBindable<bool> CanSubmitManualResult => canSubmitManualResult;

        private readonly BindableBool canSubmitManualResult = new BindableBool();

        public bool Aborted => abortedEventId == currentGameID;
        private APIMatchEvent? currentMatchEvent => Events.LastOrDefault(e => e.Id == currentMatch);

        public APIMatchEvent? LatestMatchEvent => Events.MaxBy(e => e.Id);
        public long LatestMatchEventID => LatestMatchEvent?.Id ?? 0;

        public MemoryBasedIPCWithMatchListener()
        {
            State.BindValueChanged(s =>
            {
                if (s.NewValue != TourneyState.Ranking || s.OldValue != TourneyState.Playing)
                    return;

                RequestCurrentRoundResultFromApi();
            });
        }

        public void StartListening()
        {
            if (currentMatch == -1) return;

            currentlyListening.Value = true;
            Logger.Log($"MatchListener:Listening to match {currentMatch}");
            FetchMatch();
        }

        public void StartListening(int? matchID)
        {
            if (!matchID.HasValue)
                return;

            StopListening();
            currentMatch = matchID.Value;
            StartListening();
        }

        public void StopListening()
        {
            events.Clear();
            currentMatch = -1;
            abortedEventId = 0;
            currentGameID = -1;
            currentChoice = null;
            currentMatchFinished = false;
            currentlyPlaying.Value = false;
            currentlyListening.Value = false;
            canSubmitManualResult.Value = false;

            fetchTimeOutScheduleDelegate?.Cancel();
            fetchTimeOutScheduleDelegate = null;
        }

        public void CurrentRoundAborted()
        {
            if (!currentlyPlaying.Value
                || State.Value != TourneyState.Idle
                || abortedEventId == currentGameID)
                return;

            Logger.Log($"MatchListener:Match {currentMatch} aborted");

            currentlyPlaying.Value = false;
            abortedEventId = currentGameID;
            canSubmitManualResult.Value = false;
            fetchTimeOutScheduleDelegate?.Cancel();
            fetchTimeOutScheduleDelegate = null;
            MatchAborted?.Invoke();
        }

        private ScheduledDelegate? fetchTimeOutScheduleDelegate;

        /// <summary>
        /// set timeout to fetch result
        /// </summary>
        public void RequestCurrentRoundResultFromApi()
        {
            if (!currentlyListening.Value)
            {
                currentlyPlaying.Value = false;
                MatchFinished?.Invoke(false);
                return;
            }

            if (!currentlyPlaying.Value || currentMatchFinished)
                return;

            canSubmitManualResult.Value = true;
            long requestedGameID = currentGameID;

            if (fetchTimeOutScheduleDelegate?.Completed == false)
                return;

            fetchTimeOutScheduleDelegate?.Cancel();

            fetchTimeOutScheduleDelegate = Scheduler.AddDelayed(() =>
            {
                if (currentlyListening.Value == false || currentGameID != requestedGameID)
                    return;

                Logger.Log($"MatchListener:Match {currentMatch} finished, timeout from api");

                updateScoreFromApi();
                applyLatestResultScores();

                currentlyPlaying.Value = false;
                fetchTimeOutScheduleDelegate = null;
                MatchFinished?.Invoke(false);
            }, ResultFetchTimeout);

            FetchMatch();
        }

        public void BindChoiceToNextOrCurrentMatch(BeatmapChoice? choice)
        {
            currentChoice = choice;
        }

        /// <summary>
        /// 根据API历史检查当前比赛的分数是否正确
        /// </summary>
        /// <returns>返回true则说明需要重新算分，BeatmapChoice会在该方法中获得正确的分数</returns>
        public bool RecheckMatchScoreWithHistory()
        {
            var currntmatch = Ladder.CurrentMatch.Value;

            if (currntmatch == null)
                return false;

            bool diffFromAPi = false;

            foreach (var pb in currntmatch.PicksBans.Where(p => p.Type == ChoiceType.Pick))
            {
                var game = Events.FirstOrDefault(e => e.Game?.BeatmapId == pb.BeatmapID)?.Game;
                if (game == null)
                    continue;

                long redScore = getTeamScore(TeamColour.Red, game).Sum(CalculateModMultiplier);
                long blueScore = getTeamScore(TeamColour.Blue, game).Sum(CalculateModMultiplier);

                if (redScore != pb.Scores[TeamColour.Red] || blueScore != pb.Scores[TeamColour.Blue])
                {
                    pb.Scores[TeamColour.Red] = redScore;
                    pb.Scores[TeamColour.Blue] = blueScore;

                    diffFromAPi = true;
                }
            }

            return diffFromAPi;
        }

        protected override void Update()
        {
            base.Update();

            if (!api.IsLoggedIn)
                return;

            if (!currentlyListening.Value)
                return;

            updateStatue();

            waitTime += Time.Elapsed;

            if (waitTime >= refresh_interval)
            {
                FetchMatch();
            }
        }

        private void updateStatue()
        {
            if (!CurrentlyListening.Value || !CurrentlyPlaying.Value || Aborted || State.Value == TourneyState.Playing || currentGameID == -1)
                return;

            if (currentMatchFinished)
            {
                updateScoreFromApi();
                applyLatestResultScores();

                currentlyPlaying.Value = false;

                fetchTimeOutScheduleDelegate?.Cancel();
                fetchTimeOutScheduleDelegate = null;
                canSubmitManualResult.Value = false;
                MatchFinished?.Invoke(true);
            }
        }

        private IEnumerable<PlayerScore> getTeamScore(TeamColour colour, bool forceApi = false)
        {
            if (!forceApi && (!currentMatchFinished || Aborted || currentlyPlaying.Value || State.Value == TourneyState.Playing))
                return base.GetTeamScore(colour);

            var gameResult = Events.LastOrDefault(e => e.Game?.Id == currentGameID)?.Game;

            if (gameResult == null)
                return base.GetTeamScore(colour);

            return getTeamScore(colour, gameResult);
        }

        private IEnumerable<PlayerScore> getTeamScore(TeamColour colour, APIMatchGame gameResult)
        {
            LegacyMods mods = LegacyMods.None;

            if (gameResult.Mods != null)
            {
                foreach (string mod in gameResult.Mods)
                {
                    mods |= GetLegacyModFromString(mod);
                }
            }

            int[] teamIds = Ladder.CurrentMatch.Value?.GetTeamByColor(colour)?.Players.Select(p => p.OnlineID).ToArray() ??
                            Array.Empty<int>();

            return gameResult.Scores.Where(s => teamIds.Any(t => t == s.UserID)).Select(s =>
            {
                LegacyMods playerMods = mods;

                foreach (APIMod mod in s.Mods)
                {
                    playerMods |= GetLegacyModFromString(mod.Acronym);
                }

                return new PlayerScore
                {
                    OnlineId = s.UserID,
                    Score = s.TotalScore,
                    Mods = playerMods
                };
            });
        }

        public bool SubmitManualResult(long redScore, long blueScore)
        {
            BeatmapChoice? choice = getCurrentChoice();

            if (!CanSubmitManualResult.Value || currentGameID == -1 || Aborted || choice == null)
                return false;

            addFakeEvent(redScore, blueScore, choice.BeatmapID);
            updateScoreFromApi();
            applyLatestResultScores();
            currentlyPlaying.Value = false;
            canSubmitManualResult.Value = false;
            fetchTimeOutScheduleDelegate?.Cancel();
            fetchTimeOutScheduleDelegate = null;
            MatchFinished?.Invoke(false);
            return true;
        }

        public void AddFakeEvent(long redScore, long blueScore)
        {
            if (!CurrentlyListening.Value)
                return;

            if ((!currentlyPlaying.Value && !CanSubmitManualResult.Value) || (currentMatchFinished && !CanSubmitManualResult.Value) || Aborted)
                return;

            if (currentGameID == -1)
                return;

            addFakeEvent(redScore, blueScore, getCurrentChoice()?.BeatmapID ?? -1);
        }

        private void addFakeEvent(long redScore, long blueScore, int beatmapId)
        {
            int redOnlineId = Ladder.CurrentMatch.Value?.GetTeamByColor(TeamColour.Red)?.Players.FirstOrDefault()?.OnlineID ?? -1;
            int blueOnlineId = Ladder.CurrentMatch.Value?.GetTeamByColor(TeamColour.Blue)?.Players.FirstOrDefault()?.OnlineID ?? -1;

            events.Add(new APIMatchEvent
            {
                Id = currentGameID,
                Timestamp = DateTime.Now,
                Game = new APIMatchGame
                {
                    BeatmapId = beatmapId,
                    Id = (int)currentGameID,
                    Scores = new List<MatchScore>
                    {
                        new MatchScore
                        {
                            TotalScore = redScore,
                            UserID = redOnlineId,
                        },
                        new MatchScore
                        {
                            TotalScore = blueScore,
                            UserID = blueOnlineId,
                        }
                    }
                },
                Detail = new MatchEventDetail
                {
                    Type = MatchEventType.Other,
                }
            });

            currentMatchFinished = true;
        }

        private void updateScoreFromApi()
        {
            Score1.Value = getTeamScore(TeamColour.Red, true).Sum(CalculateModMultiplier);
            Score2.Value = getTeamScore(TeamColour.Blue, true).Sum(CalculateModMultiplier);
        }

        private BeatmapChoice? getCurrentChoice()
        {
            if (currentChoice != null)
                return currentChoice;

            int beatmapId = Beatmap.Value?.OnlineID ?? -1;

            return Ladder.CurrentMatch.Value?.PicksBans.LastOrDefault(choice => choice.Type == ChoiceType.Pick && choice.BeatmapID == beatmapId);
        }

        private void applyLatestResultScores()
        {
            APIMatchGame? gameResult = Events.LastOrDefault(e => e.Game?.Id == currentGameID)?.Game;

            if (gameResult == null)
                return;

            BeatmapChoice? choice = Ladder.CurrentMatch.Value?.PicksBans.LastOrDefault(choice => choice.Type == ChoiceType.Pick && choice.BeatmapID == gameResult.BeatmapId) ?? getCurrentChoice();

            if (choice == null)
                return;

            choice.Scores[TeamColour.Red] = Score1.Value;
            choice.Scores[TeamColour.Blue] = Score2.Value;
        }

        protected override IEnumerable<PlayerScore> GetTeamScore(TeamColour colour) => getTeamScore(colour);

        // ture meanwhile current match id is null from api
        private bool currentMatchFinished;

        public void FetchMatch()
        {
            waitTime = 0;

            var req = new GetAPIMatchInfo(currentMatch)
            {
                AfterEvent = LatestMatchEventID
            };

            req.Success += content =>
            {
                LastFetchSuccess = true;

                if (content.APIMatch.ID != currentMatch || (req.AfterEvent.HasValue && req.AfterEvent != LatestMatchEventID))
                    return;

                var newEvents = content.Events.Where(e => e.Game == null || e.Game?.Scores.Count != 0).ExceptBy(Events.Select(e => e.Id), e => e.Id).ToArray();

                if (content.CurrentGameID == null)
                {
                    // 理论上永远为true
                    currentMatchFinished = Events.Concat(newEvents).Any(e => e.Game?.Id == currentGameID && e.Game?.Scores.Count != 0);
                    Logger.Log($"MatchListener: Match Finished event currentGameId {currentGameID}, currentMatchFinished {currentMatchFinished}");

                    if (currentMatchFinished && !currentlyPlaying.Value && !CanSubmitManualResult.Value)
                    {
                        canSubmitManualResult.Value = false;
                    }
                }
                else if (content.CurrentGameID != currentGameID)
                {
                    Logger.Log($"MatchListener: New Match started, GameID {content.CurrentGameID}, currentMatchFinished {currentMatchFinished}, currentlyPlaying {currentlyPlaying}");
                    currentMatchFinished = false;
                    currentChoice = null;
                    canSubmitManualResult.Value = false;
                    fetchTimeOutScheduleDelegate?.Cancel();
                    fetchTimeOutScheduleDelegate = null;
                    currentlyPlaying.Value = true;
                    currentGameID = content.CurrentGameID.Value;
                }

                events.AddRange(newEvents);

                if (!currentlyPlaying.Value && !Aborted && newEvents.Any(e => e.Game?.Id == currentGameID && e.Game.Scores.Count != 0))
                {
                    updateScoreFromApi();
                    applyLatestResultScores();
                    MatchFinished?.Invoke(true);
                }

                if (Events.Any(e => e.Detail.Type == MatchEventType.MatchDisbanded))
                    StopListening();
            };

            req.Failure += _ =>
            {
                LastFetchSuccess = false;

                Logger.Log("MatchListener: Occur network problem, match event fetch failed");
                FetchFailed?.Invoke();
            };

            api.Queue(req);
        }

        public static LegacyMods GetLegacyModFromString(string modString)
        {
            switch (modString)
            {
                case "EZ":
                    return LegacyMods.Easy;

                case "NF":
                    return LegacyMods.NoFail;

                case "HT":
                    return LegacyMods.HalfTime;

                case "HR":
                    return LegacyMods.HardRock;

                case "SD":
                    return LegacyMods.SuddenDeath;

                case "PF":
                    return LegacyMods.Perfect;

                case "DT":
                    return LegacyMods.DoubleTime;

                case "NC":
                    return LegacyMods.Nightcore;

                case "FI":
                    return LegacyMods.FadeIn;

                case "HD":
                    return LegacyMods.Hidden;

                case "FL":
                    return LegacyMods.Flashlight;

                case "RX":
                    return LegacyMods.Relax;

                default:
                    Logger.Log($"Cannot prase {nameof(modString)}: {modString} to {nameof(LegacyMods)}.", level: LogLevel.Error);
                    return LegacyMods.None;
            }
        }
    }
}
