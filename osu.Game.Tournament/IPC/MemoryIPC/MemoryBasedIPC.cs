// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.IPC.MemoryIPC
{
    [SupportedOSPlatform("windows")]
    public partial class MemoryBasedIPC : MatchIPCInfo
    {
        private const int chat_diagnostic_log_interval_ms = 30000;

        private int lastBeatmapId;
        private GetBeatmapRequest? beatmapLookupRequest;
        private ChannelManager chatManager = null!;
        private readonly Dictionary<DifficultyLookup, Task<DifficultyAttributes?>> difficultyTasks = new Dictionary<DifficultyLookup, Task<DifficultyAttributes?>>();
        private readonly HashSet<Task<DifficultyAttributes?>> loggedDifficultyFailures = new HashSet<Task<DifficultyAttributes?>>();
        private WorkingBeatmap? workingBeatmap;
        private BeatmapLookup? workingBeatmapLookup;

        public IBindable<bool> Available => available;

        private readonly BindableBool available = new BindableBool();

        public SlotPlayerStatus[] SlotPlayers { get; } = Enumerable.Range(0, 8).Select(i => new SlotPlayerStatus()).ToArray();
        private readonly BindableInt memoryChatChannel = new BindableInt();
        private int currentMemoryMessageCount = 0;
        private long nextChatDiagnosticLogAt;

        [Resolved]
        protected LadderInfo Ladder { get; private set; } = null!;

        [Resolved]
        protected IAPIProvider API { get; private set; } = null!;

        [Resolved]
        private TournamentBeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private TournamentBeatmapDifficultyCache difficultyCache { get; set; } = null!;

        public bool FetchDataFromMemory { get; set; }

        private readonly BindableInt playersPerTeam = new BindableInt
        {
            MinValue = 1,
            MaxValue = 4,
        };

        private StableMemoryReader[] readers;
        private TourneyManagerMemoryReader tourneyManagerMemoryReader;

        public int PlayTime => SlotPlayers.Max(s => s.PlayTime.Value);

        public MemoryBasedIPC()
        {
            readers = Enumerable.Range(0, 8).Select(i => new StableMemoryReader()).ToArray();
            tourneyManagerMemoryReader = new TourneyManagerMemoryReader();

            memoryChatChannel.BindValueChanged(c =>
            {
                resetTourneyChatChannel(c.NewValue);
            }, true);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            AddInternal(chatManager = new ChannelManager(API));
            playersPerTeam.BindTo(Ladder.PlayersPerTeam);
            Ladder.CurrentMatch.BindValueChanged(_ => Schedule(updateMatchPlayers), true);
            Beatmap.BindValueChanged(_ => resetDifficultyState());
            Ladder.Ruleset.BindValueChanged(_ => resetDifficultyState());
        }

        public override bool PostChatMessage(string message)
        {
            var channel = ChatChannel.Value;

            if (channel == null || string.IsNullOrWhiteSpace(message))
                return false;

            chatManager.PostMessage(message, target: channel);
            return true;
        }

        public override void RefreshChatChannel()
        {
            if (tourneyManagerMemoryReader.Status != AttachStatus.Attached)
                return;

            try
            {
                updateTourneyChat(tourneyManagerMemoryReader);
            }
            catch (InvalidOperationException)
            {
                if (tourneyManagerMemoryReader.Status != AttachStatus.UnAttached)
                    throw;
            }
        }

        private void updateMatchPlayers()
        {
            var match = Ladder.CurrentMatch.Value;
            HasActiveMatch.Value = match != null;

            var players = new List<MatchRoomPlayerInfo>();

            if (match != null)
            {
                foreach (TeamColour team in new[] { TeamColour.Red, TeamColour.Blue })
                {
                    var teamPlayers = match.GetTeamByColor(team)?.Players;

                    if (teamPlayers == null)
                        continue;

                    for (int i = 0; i < teamPlayers.Count; i++)
                    {
                        var player = teamPlayers[i];
                        players.Add(new MatchRoomPlayerInfo(player.OnlineID, player.Username, team, i));
                    }
                }
            }

            if (RoomPlayersInternal.SequenceEqual(players))
                return;

            RoomPlayersInternal.Clear();
            RoomPlayersInternal.AddRange(players);
        }

        private const int update_hz = 5;
        private double lastUpdateTime;

        public void Reset()
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }

            readers = Enumerable.Range(0, 8).Select(i => new StableMemoryReader()).ToArray();

            tourneyManagerMemoryReader.Dispose();
            tourneyManagerMemoryReader = new TourneyManagerMemoryReader();
        }

        private void updateTourneyManagerData()
        {
            var reader = tourneyManagerMemoryReader;

            try
            {
                State.Value = reader.GetTourneyState();
                LegacyMods mods = Mods.Value = reader.GetMods();

                int beatmapId = reader.GetBeatmapId();

                if (beatmapId > 0 && lastBeatmapId != beatmapId)
                {
                    beatmapLookupRequest?.Cancel();

                    lastBeatmapId = beatmapId;

                    var existing = Ladder.CurrentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(b => b.ID == beatmapId);

                    if (existing != null)
                    {
                        Beatmap.Value = existing.Beatmap;
                        var ruleset = Ladder.Ruleset.Value?.CreateInstance();
                        string modStr = existing.Mods;

                        var mod = ruleset!.CreateModFromAcronym(modStr);

                        Mods.Value = mod != null ? ruleset.ConvertToLegacyMods(new[] { mod }) : mods;
                    }
                    else
                    {
                        beatmapLookupRequest = new GetBeatmapRequest(new APIBeatmap { OnlineID = beatmapId });
                        beatmapLookupRequest.Success += b =>
                        {
                            if (lastBeatmapId == beatmapId)
                                Beatmap.Value = new TournamentBeatmap(b);
                        };
                        beatmapLookupRequest.Failure += _ =>
                        {
                            if (lastBeatmapId == beatmapId)
                                Beatmap.Value = null;
                        };
                        API.Queue(beatmapLookupRequest);
                        Mods.Value = mods;
                    }
                }

                memoryChatChannel.Value = (int)reader.GetChannelId();
                updateTourneyChat(reader);
            }
            catch (InvalidOperationException)
            {
                if (reader.Status == AttachStatus.UnAttached)
                {
                    Logger.Log("Attempt fetch data when Unattached. Tourney Manager");
                    return;
                }

                throw;
            }
        }

        private void resetTourneyChatChannel(int channelId)
        {
            ChatChannel.Value = channelId > 0 ? new Channel
            {
                Name = "mp",
                Id = channelId,
                Type = ChannelType.Private
            } : null;

            currentMemoryMessageCount = 0;
        }

        private void updateTourneyChat(TourneyManagerMemoryReader reader)
        {
            List<Message>? updatedMessages = reader.GetTourneyChat(out int memoryMessageCount, currentMemoryMessageCount);

            applyUpdatedTourneyChat(updatedMessages);
            currentMemoryMessageCount = memoryMessageCount;
        }

        private void applyUpdatedTourneyChat(List<Message>? tourneyChatItems)
        {
            if (tourneyChatItems == null)
                return;

            Message[] takenChat = tourneyChatItems.TakeLast(Channel.MAX_HISTORY).ToArray();

            var channel = ChatChannel.Value;

            if (channel == null)
                return;
            int previousChannelCount = channel.Messages.Count;

            Message[] toAdd = takenChat.Except(channel.Messages).ToArray();
            channel.AddNewMessages(toAdd);

            if (toAdd.Length > 0)
            {
                Logger.Log($"memory: add {toAdd.Length} message items");
            }

            bool suspiciousLargeAdd = previousChannelCount > 0 && toAdd.Length >= 20;
            bool suspiciousFullWindowAdd = previousChannelCount > 0 && takenChat.Length > 0 && toAdd.Length == takenChat.Length;

            if ((suspiciousLargeAdd || suspiciousFullWindowAdd) && shouldEmitChatDiagnostic(ref nextChatDiagnosticLogAt))
            {
                Logger.Log(
                    $"memory chat diagnostic: channel_count={previousChannelCount}, incoming_count={tourneyChatItems.Count}, taken_count={takenChat.Length}, to_add={toAdd.Length}, current_memory_count={currentMemoryMessageCount}, first_add={describeMessage(toAdd.FirstOrDefault())}, last_add={describeMessage(toAdd.LastOrDefault())}",
                    LoggingTarget.Runtime,
                    LogLevel.Important);
            }
        }

        private static bool shouldEmitChatDiagnostic(ref long nextLogAt)
        {
            long now = Environment.TickCount64;

            if (now < nextLogAt)
                return false;

            nextLogAt = now + chat_diagnostic_log_interval_ms;
            return true;
        }

        private static string describeMessage(Message? message)
        {
            if (message == null)
                return "<none>";

            string content = message.Content ?? string.Empty;

            return $"[{message.Timestamp:O}] {message.Sender.Username} len={content.Length} hash={getContentHash(content):X8}";
        }

        private static int getContentHash(string content)
        {
            unchecked
            {
                int hash = 17;

                foreach (char c in content)
                    hash = hash * 31 + c;

                return hash;
            }
        }

        protected override void Update()
        {
            base.Update();

            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return;

            lastUpdateTime += Time.Elapsed;

            if (lastUpdateTime < 1000.0 / update_hz)
                return;

            lastUpdateTime = 0;

            for (int i = 0; i < playersPerTeam.Value * 2; i++)
            {
                var reader = readers[i];
                var player = SlotPlayers[i];

                switch (reader.Status)
                {
                    case AttachStatus.UnAttached:
                        if (OperatingSystem.IsWindows())
                        {
                            reader.AttachToProcessByTitleNameAsync($"{TournamentGame.TOURNAMENT_CLIENT_NAME}{i}");
                        }
                        else
                        {
                            int index = i;
                            reader.AttachToProcessByProcessCommandLineAsync(s => s.Contains($"-spectateclient {index}"));
                        }

                        continue;

                    case AttachStatus.Initializing:
                        continue;

                    case AttachStatus.Attached:
                    {
                        if (!FetchDataFromMemory)
                            continue;

                        try
                        {
                            var user = reader.GetTournamentUser();
                            if (user == null)
                                continue;

                            player.OnlineID.Value = user.OnlineID;

                            var gameplayData = reader.GetGameplayData();
                            if (gameplayData == null)
                                continue;

                            player.Accuracy.Value = gameplayData.Accuracy / 100;
                            player.Combo.Value = gameplayData.Combo;
                            player.MaxCombo.Value = gameplayData.MaxCombo;
                            player.Hit50.Value = gameplayData.Hit50;
                            player.Hit100.Value = gameplayData.Hit100;
                            player.Hit300.Value = gameplayData.Hit300;
                            player.HitGeki.Value = gameplayData.HitGeki;
                            player.HitKatu.Value = gameplayData.HitKatu;
                            player.HitMiss.Value = gameplayData.HitMiss;
                            player.Mods.Value = gameplayData.Mods;
                            player.Score.Value = gameplayData.Score;
                            player.PlayTime.Value = reader.PlayTime;
                            continue;
                        }
                        catch (InvalidOperationException)
                        {
                            if (reader.Status == AttachStatus.UnAttached)
                            {
                                Logger.Log($"Attempt fetch data when Unattached. {TournamentGame.TOURNAMENT_CLIENT_NAME}{i}");
                                continue;
                            }

                            throw;
                        }
                    }
                }
            }

            switch (tourneyManagerMemoryReader.Status)
            {
                case AttachStatus.UnAttached:

                    if (OperatingSystem.IsWindows())
                        tourneyManagerMemoryReader.AttachToProcessByTitleNameAsync(" Tournament Manager");
                    else
                        tourneyManagerMemoryReader.AttachToProcessByProcessCommandLineAsync(s => !s.Contains($"-spectateclient"));

                    available.Value = false;
                    break;

                case AttachStatus.Initializing:
                    available.Value = false;
                    break;

                case AttachStatus.Attached:
                    updateTourneyManagerData();
                    available.Value = true;
                    break;
            }

            UpdateScore();
        }

        protected void UpdateScore()
        {
            Team1Combo.Value = getCombo(TeamColour.Red);
            Team2Combo.Value = getCombo(TeamColour.Blue);

            if (Ladder.ScoringMode.Value == TournamentScoringMode.PerformancePoint
                && tryCalculatePerformanceScore(TeamColour.Red, out double performanceScore1)
                && tryCalculatePerformanceScore(TeamColour.Blue, out double performanceScore2))
            {
                Score1.Value = (long)Math.Round(performanceScore1);
                Score2.Value = (long)Math.Round(performanceScore2);
                return;
            }

            Score1.Value = GetTeamScore(TeamColour.Red).Sum(CalculateModMultiplier);
            Score2.Value = GetTeamScore(TeamColour.Blue).Sum(CalculateModMultiplier);
        }

        private void resetDifficultyState()
        {
            difficultyTasks.Clear();
            loggedDifficultyFailures.Clear();
            workingBeatmap = null;
            workingBeatmapLookup = null;
        }

        private bool tryCalculatePerformanceScore(TeamColour colour, out double score)
        {
            score = 0;

            TournamentBeatmap? beatmap = Beatmap.Value;
            RulesetInfo? rulesetInfo = Ladder.Ruleset.Value;

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
                if (difficultyTask.IsFaulted && loggedDifficultyFailures.Add(difficultyTask))
                    Logger.Error(difficultyTask.Exception, "Difficulty task failed");

                return null;
            }

            DifficultyAttributes? difficultyAttributes = difficultyTask.GetAwaiter().GetResult();

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
            int[] teamIds = GetTeamIds(colour);
            return SlotPlayers.Where(player => teamIds.Contains(player.OnlineID.Value));
        }

        private static LegacyMods normaliseMods(LegacyMods mods)
        {
            mods &= ~LegacyMods.FreeMod;
            mods &= ~LegacyMods.NoMod;
            return mods;
        }

        protected long CalculateModMultiplier(PlayerScore s)
        {
            return (long)(s.Score * Ladder.ModMultiplierSettings
                                               .Where(m => (TournamentGameBase.ConvertFromAcronym(m.ModAcronym.Value) & s.Mods) > LegacyMods.None)
                                               .Aggregate(1.0, (total, setting) => total * setting.Multiplier.Value));
        }

        protected virtual IEnumerable<PlayerScore> GetTeamScore(TeamColour colour)
        {
            int[] teamIds = GetTeamIds(colour);

            return SlotPlayers.Where(s => teamIds.Any(t => t == s.OnlineID.Value)).Select(s => new PlayerScore
            {
                OnlineId = s.OnlineID.Value,
                Score = s.Score.Value,
                Mods = s.Mods.Value
            });
        }

        protected int[] GetTeamIds(TeamColour colour)
        {
            return Ladder.CurrentMatch.Value?.GetTeamByColor(colour)?.Players.Select(p => p.OnlineID).ToArray() ??
                   Array.Empty<int>();
        }

        private int getCombo(TeamColour colour)
        {
            int[] teamIds = GetTeamIds(colour);

            return SlotPlayers.Where(s => teamIds.Any(t => t == s.OnlineID.Value)).Select(s => s.Combo.Value).Sum();
        }

        private readonly record struct DifficultyLookup(int OnlineID, string MD5Hash, string RulesetShortName, LegacyMods Mods);

        private readonly record struct BeatmapLookup(int OnlineID, string MD5Hash);
    }

    public struct PlayerScore
    {
        public int OnlineId;
        public long Score;
        public LegacyMods Mods;
    }
}
