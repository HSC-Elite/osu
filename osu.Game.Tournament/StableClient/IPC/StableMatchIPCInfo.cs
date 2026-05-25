// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Audio;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Database;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Rooms;
using osu.Game.Online.Spectator;
using osu.Game.Rulesets;
using osu.Game.Screens.OnlinePlay;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.StableClient.Protocol;

namespace osu.Game.Tournament.StableClient.IPC
{
    public partial class StableMatchIPCInfo : MatchIPCInfo
    {
        [Resolved]
        private StableBanchoClient client { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private BeatmapLookupCache beatmapLookupCache { get; set; } = null!;

        [Resolved]
        private BeatmapModelDownloader beatmapDownloader { get; set; } = null!;

        [Resolved]
        private TournamentGameBase game { get; set; } = null!;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        [Resolved]
        private Bindable<WorkingBeatmap> workingBeatmap { get; set; } = null!;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private TextureStore textures { get; set; } = null!;

        [Resolved]
        private Bindable<RulesetInfo> ruleset { get; set; } = null!;

        public readonly Bindable<MultiplayerMatch?> CurrentMatch = new Bindable<MultiplayerMatch?>();

        private readonly StableBeatmapAvailabilityTracker beatmapAvailabilityTracker = new StableBeatmapAvailabilityTracker();

        private double waitingForIdle;
        private const int time_to_idle_from_ranking = 15000;

        private string credentialsUsername = string.Empty;
        private string credentialsPasswordHash = string.Empty;

        private readonly StableSpectatorHandler?[] spectatorHandlers = new StableSpectatorHandler?[16];
        private readonly Dictionary<int, long> userScores = new Dictionary<int, long>();
        private readonly List<StableBanchoMessage> receivedMessages = new List<StableBanchoMessage>();
        private static readonly Regex match_history_regex = new Regex(@"https?://\S*/(?<id>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private int lastBeatmapId;
        private long? lastMatchHistoryFetchId;

        public IReadOnlyList<StableBanchoMessage> ReceivedMessages => receivedMessages;
        public long? LastMatchHistoryFetchId => lastMatchHistoryFetchId;

        public IEnumerable<StableSpectatorHandler> GetActiveSpectatorHandlers() => spectatorHandlers.Where(h => h != null).Cast<StableSpectatorHandler>();

        public int GetSlotIndexForUser(int userId)
        {
            var match = CurrentMatch.Value;
            if (match == null) return -1;

            for (int i = 0; i < 16; i++)
            {
                if (match.SlotUserIds[i] == userId)
                    return i;
            }

            return -1;
        }

        public int GetTeamForUser(int userId)
        {
            var match = CurrentMatch.Value;
            if (match == null) return 0;

            for (int i = 0; i < 16; i++)
            {
                if (match.SlotUserIds[i] == userId)
                    return match.SlotTeams[i];
            }

            return 0;
        }

        public StableMatchIPCInfo()
        {
        }

        public void SetCredentials(string username, string passwordHash)
        {
            credentialsUsername = username;
            credentialsPasswordHash = passwordHash;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            game.Add(beatmapAvailabilityTracker);

            client.OnMatchCreated += onMatchUpdated;
            client.OnMatchUpdated += onMatchUpdated;
            client.OnMatchDisbanded += onMatchDisbanded;
            client.OnMessageReceived += onMessageReceived;
            ladder.PlayersPerTeam.BindValueChanged(_ => Schedule(refreshTrackedPlayers), true);

            CurrentMatch.BindValueChanged(e =>
            {
                if (e.NewValue != null)
                {
                    if (e.OldValue?.Id != e.NewValue.Id)
                        resetRoomState();

                    updateMatchState(e.NewValue, true);
                    updateSpectatorHandlers(e.NewValue);
                    updatePlaybackState(e.NewValue, e.OldValue);
                }
                else
                {
                    resetRoomState();
                }
            });

            beatmapAvailabilityTracker.Availability.BindValueChanged(avail =>
            {
                if (avail.NewValue.State == DownloadState.LocallyAvailable && lastBeatmapId > 0)
                {
                    var local = beatmaps.QueryBeatmap(b => b.OnlineID == lastBeatmapId);

                    if (local != null)
                    {
                        Logger.Log($"StableMatchIPCInfo: beatmap {lastBeatmapId} became locally available, refreshing spectator handlers.");
                        Schedule(() => workingBeatmap.Value = beatmaps.GetWorkingBeatmap(local));

                        foreach (var handler in GetActiveSpectatorHandlers())
                            handler.RefreshBeatmapAvailability();

                        if (CurrentMatch.Value?.InProgress == true && CurrentMatch.Value.BeatmapId == lastBeatmapId)
                        {
                            Logger.Log($"StableMatchIPCInfo: beatmap {lastBeatmapId} is now playable locally, entering Playing.");
                            Schedule(() => State.Value = TourneyState.Playing);
                        }
                    }
                }
            });
        }

        private void onMatchUpdated(MultiplayerMatch match) => Schedule(() =>
        {
            if (CurrentMatch.Value?.Id != match.Id)
                return;

            var previousMatch = CurrentMatch.Value;
            CurrentMatch.Value = match;

            updateMatchState(match, false);
            updateSpectatorHandlers(match);
            updatePlaybackState(match, previousMatch);
        });

        private void onMatchDisbanded(int matchId) => Schedule(() =>
        {
            if (CurrentMatch.Value?.Id != matchId)
                return;

            CurrentMatch.Value = null;
            State.Value = TourneyState.Idle;
            clearSpectatorHandlers();
        });

        private void updateMatchState(MultiplayerMatch match, bool forceUpdate)
        {
            if (forceUpdate || match.BeatmapId != lastBeatmapId)
            {
                lastBeatmapId = match.BeatmapId;

                beatmapAvailabilityTracker.PlaylistItem.Value = new PlaylistItem(new APIBeatmap { OnlineID = match.BeatmapId });

                var existing = ladder.CurrentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(b => b.ID == match.BeatmapId);

                if (existing != null)
                {
                    Beatmap.Value = existing.Beatmap;
                }
                else
                {
                    beatmapLookupCache.GetBeatmapAsync(match.BeatmapId).ContinueWith(t =>
                    {
                        var apiBeatmap = t.GetResultSafely();

                        if (apiBeatmap != null && lastBeatmapId == match.BeatmapId)
                        {
                            Schedule(() => Beatmap.Value = new TournamentBeatmap(apiBeatmap));
                        }
                    });
                }

                var localBeatmap = beatmaps.QueryBeatmap(b => b.OnlineID == match.BeatmapId);

                if (localBeatmap != null)
                {
                    Schedule(() => workingBeatmap.Value = beatmaps.GetWorkingBeatmap(localBeatmap));
                }
                else
                {
                    Schedule(() => workingBeatmap.Value = new DummyWorkingBeatmap(audio, textures));

                    beatmapLookupCache.GetBeatmapAsync(match.BeatmapId).ContinueWith(t =>
                    {
                        var apiBeatmap = t.GetResultSafely();

                        if (apiBeatmap?.BeatmapSet != null)
                        {
                            if (!beatmaps.IsAvailableLocally(new BeatmapSetInfo { OnlineID = apiBeatmap.BeatmapSet.OnlineID }))
                                beatmapDownloader.Download(apiBeatmap.BeatmapSet);
                        }
                    });
                }
            }

            var rulesetInfo = rulesets.GetRuleset(match.PlayMode);
            if (rulesetInfo != null)
                Schedule(() => ruleset.Value = rulesetInfo);

            Mods.Value = (LegacyMods)match.Mods;
        }

        private void updatePlaybackState(MultiplayerMatch match, MultiplayerMatch? previousMatch)
        {
            bool beatmapAvailableLocally = beatmaps.QueryBeatmap(b => b.OnlineID == match.BeatmapId) != null;

            if (match.InProgress)
            {
                if (previousMatch == null || !previousMatch.InProgress)
                    userScores.Clear();

                State.Value = beatmapAvailableLocally ? TourneyState.Playing : TourneyState.Idle;

                if (!beatmapAvailableLocally)
                {
                    Logger.Log($"StableMatchIPCInfo: match {match.Id} is in progress but beatmap {match.BeatmapId} is unavailable locally; staying idle until download completes.");
                }
            }
            else if (previousMatch?.InProgress == true)
            {
                State.Value = TourneyState.Ranking;
                waitingForIdle = 0;
            }
            else
            {
                State.Value = TourneyState.Idle;
            }
        }

        private void resetRoomState()
        {
            userScores.Clear();
            receivedMessages.Clear();
            lastMatchHistoryFetchId = null;
            waitingForIdle = 0;
            State.Value = TourneyState.Idle;
            ChatChannel.Value = 0;
            Score1.Value = 0;
            Score2.Value = 0;
        }

        private void onMessageReceived(bMessage message) => Schedule(() =>
        {
            var stableMessage = new StableBanchoMessage(
                message.SendingClient,
                message.Message,
                message.Target,
                message.SenderId,
                Time.Current);

            receivedMessages.Add(stableMessage);

            if (!message.Message.StartsWith("Match history available", StringComparison.OrdinalIgnoreCase))
                return;

            var match = match_history_regex.Match(message.Message);

            if (match.Success && int.TryParse(match.Groups["id"].Value, out int fetchId))
            {
                lastMatchHistoryFetchId = fetchId;
                Logger.Log($"StableMatchIPCInfo: captured match history fetch id {fetchId} from bancho message.");
                fetchChannelId(fetchId);
            }
        });

        private void fetchChannelId(int matchId)
        {
            if (string.IsNullOrEmpty(credentialsUsername) || string.IsNullOrEmpty(credentialsPasswordHash))
                return;

            Task.Run(async () =>
            {
                try
                {
                    var req = new OsuWebRequest(
                        $"https://osu.ppy.sh/web/osu-getchannelid.php?u={Uri.EscapeDataString(credentialsUsername)}&h={Uri.EscapeDataString(credentialsPasswordHash)}&mp={matchId}");
                    await req.PerformAsync().ConfigureAwait(false);

                    if (int.TryParse(req.GetResponseString(), out int channelId) && channelId > 0)
                    {
                        Schedule(() => ChatChannel.Value = channelId);
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to retrieve multiplayer channel ID.");
                }
            });
        }

        private void updateSpectatorHandlers(MultiplayerMatch match)
        {
            int trackedSlots = Math.Min(16, ladder.PlayersPerTeam.Value * 2);

            for (int i = 0; i < 16; i++)
            {
                if (i >= trackedSlots)
                {
                    expireHandler(i);
                    continue;
                }

                int userId = match.SlotUserIds[i];

                if (spectatorHandlers[i]?.UserId != userId)
                {
                    expireHandler(i);

                    if (userId > 0)
                    {
                        var handler = new StableSpectatorHandler(userId, credentialsUsername, credentialsPasswordHash);
                        handler.OnFramesReceived += bundle => onFramesReceived(userId, bundle);
                        spectatorHandlers[i] = handler;
                        Scheduler.Add(() =>
                        {
                            game.Add(handler);
                        });
                    }
                }
            }
        }

        private void refreshTrackedPlayers()
        {
            if (CurrentMatch.Value != null)
                updateSpectatorHandlers(CurrentMatch.Value);
            else
                clearSpectatorHandlers();
        }

        private void expireHandler(int index)
        {
            if (spectatorHandlers[index] == null)
                return;

            spectatorHandlers[index]!.Expire();
            spectatorHandlers[index] = null;
        }

        private void clearSpectatorHandlers()
        {
            for (int i = 0; i < 16; i++)
                expireHandler(i);
        }

        private void onFramesReceived(int userId, FrameDataBundle bundle) => Schedule(() =>
        {
            userScores[userId] = bundle.Header.TotalScore;
        });

        protected override void Update()
        {
            base.Update();

            if (State.Value == TourneyState.Ranking)
            {
                waitingForIdle += Time.Elapsed;
                if (waitingForIdle >= time_to_idle_from_ranking)
                    State.Value = TourneyState.Idle;
            }
            else
            {
                waitingForIdle = 0;
            }

            updateTeamScores();
        }

        private void updateTeamScores()
        {
            var match = CurrentMatch.Value;
            if (match == null) return;

            long blueScore = 0;
            long redScore = 0;

            for (int i = 0; i < 16; i++)
            {
                int userId = match.SlotUserIds[i];

                if (userId > 0 && userScores.TryGetValue(userId, out long score))
                {
                    if (match.SlotTeams[i] == 1) // Blue
                        blueScore += score;
                    else if (match.SlotTeams[i] == 2) // Red
                        redScore += score;
                }
            }

            Score1.Value = blueScore;
            Score2.Value = redScore;
        }

        protected override void Dispose(bool isDisposing)
        {
            if (client != null)
            {
                client.OnMatchCreated -= onMatchUpdated;
                client.OnMatchUpdated -= onMatchUpdated;
                client.OnMatchDisbanded -= onMatchDisbanded;
                client.OnMessageReceived -= onMessageReceived;
            }

            clearSpectatorHandlers();
            base.Dispose(isDisposing);
        }

        private partial class StableBeatmapAvailabilityTracker : OnlinePlayBeatmapAvailabilityTracker
        {
            public new Bindable<PlaylistItem?> PlaylistItem => base.PlaylistItem;
        }
    }

    public readonly record struct StableBanchoMessage(
        string Sender,
        string Message,
        string Target,
        int SenderId,
        double ReceivedAt);
}
