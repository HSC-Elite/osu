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
using osu.Game.Online.Chat;
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
        public StableSpectatorDataSource SpectatorDataSource { get; } = new StableSpectatorDataSource();
        private ChannelManager chatManager = null!;
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
        private void load(IAPIProvider api)
        {
            AddInternal(chatManager = new ChannelManager(api));
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

                    updateMatchState(e.NewValue, e.OldValue?.Id != e.NewValue.Id);
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
                            Logger.Log($"StableMatchIPCInfo: beatmap {lastBeatmapId} is now playable locally, waiting for spectator players.");
                            Schedule(() => State.Value = TourneyState.WaitingForClients);
                        }
                    }
                }
            });
        }

        private void onMatchUpdated(MultiplayerMatch match) => Schedule(() =>
        {
            if (CurrentMatch.Value?.Id != match.Id)
                return;

            CurrentMatch.Value = match;
        });

        private void onMatchDisbanded(int matchId) => Schedule(() =>
        {
            if (CurrentMatch.Value?.Id != matchId)
                return;

            CurrentMatch.Value = null;
            State.Value = TourneyState.Idle;
            clearSpectatorHandlers();
        });

        private void updateRoomPlayers(MultiplayerMatch match)
            => SetRoomPlayers(CreateRoomPlayers(match, userId =>
                GetActiveSpectatorHandlers().FirstOrDefault(handler => handler.UserId == userId)?.Username
                ?? ladder.Teams.SelectMany(team => team.Players).FirstOrDefault(player => player.OnlineID == userId)?.Username
                ?? $"User {userId}"));

        internal static MatchRoomPlayerInfo[] CreateRoomPlayers(MultiplayerMatch match, Func<int, string?> getUsername)
        {
            var players = new List<MatchRoomPlayerInfo>();
            int redSlot = 0;
            int blueSlot = 0;
            for (int i = 0; i < match.SlotUserIds.Length; i++)
            {
                int userId = match.SlotUserIds[i];
                if (userId <= 0)
                    continue;

                TeamColour? team = match.SlotTeams[i] switch
                {
                    1 => TeamColour.Blue,
                    2 => TeamColour.Red,
                    _ => null,
                };
                int? slot = team == TeamColour.Red ? redSlot++ : team == TeamColour.Blue ? blueSlot++ : null;
                players.Add(new MatchRoomPlayerInfo(userId, getUsername(userId), team, slot));
            }
            return players.ToArray();
        }

        private void updateMatchState(MultiplayerMatch match, bool forceUpdate)
        {
            HasActiveMatch.Value = true;
            updateRoomPlayers(match);

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
                    workingBeatmap.Value = beatmaps.GetWorkingBeatmap(localBeatmap);
                }
                else
                {
                    workingBeatmap.Value = new DummyWorkingBeatmap(audio, textures);

                    beatmapLookupCache.GetBeatmapAsync(match.BeatmapId).ContinueWith(t =>
                    {
                        var apiBeatmap = t.GetResultSafely();

                        if (apiBeatmap?.BeatmapSet != null)
                        {
                            if (!beatmaps.IsAvailableLocally(apiBeatmap))
                                beatmapDownloader.Download(apiBeatmap.BeatmapSet);
                        }
                    });
                }
            }

            var rulesetInfo = rulesets.GetRuleset(match.PlayMode);
            if (rulesetInfo != null)
                ruleset.Value = rulesetInfo;

            Ruleset.Value = rulesetInfo;
            Mods.Value = (LegacyMods)match.Mods;
        }

        private void updatePlaybackState(MultiplayerMatch match, MultiplayerMatch? previousMatch)
        {
            bool beatmapAvailableLocally = beatmaps.QueryBeatmap(b => b.OnlineID == match.BeatmapId) != null;

            if (match.InProgress && (previousMatch == null || !previousMatch.InProgress))
                SpectatorDataSource.Reset();

            if (!match.InProgress && previousMatch?.InProgress == true)
            {
                waitingForIdle = 0;
            }

            if ((match.InProgress && State.Value is not (TourneyState.Playing or TourneyState.WaitingForClients))
                || previousMatch?.InProgress == true && !match.InProgress
                || !match.InProgress && State.Value != TourneyState.Ranking)
                State.Value = SelectTourneyState(match.InProgress, previousMatch?.InProgress == true, beatmapAvailableLocally);

            if (match.InProgress && !beatmapAvailableLocally)
            {
                Logger.Log($"StableMatchIPCInfo: match {match.Id} is in progress but beatmap {match.BeatmapId} is unavailable locally; staying idle until download completes.");
            }
        }

        internal static TourneyState SelectTourneyState(bool inProgress, bool wasInProgress, bool beatmapAvailableLocally)
        {
            if (inProgress)
                return beatmapAvailableLocally ? TourneyState.WaitingForClients : TourneyState.Idle;

            return wasInProgress ? TourneyState.Ranking : TourneyState.Idle;
        }

        private void resetRoomState()
        {
            SpectatorDataSource.Reset();
            clearSpectatorHandlers();
            HasActiveMatch.Value = false;
            SetRoomPlayers(Array.Empty<MatchRoomPlayerInfo>());
            lastBeatmapId = 0;
            receivedMessages.Clear();
            lastMatchHistoryFetchId = null;
            waitingForIdle = 0;
            State.Value = TourneyState.Idle;
            if (ChatChannel.Value != null)
                chatManager.LeaveChannel(ChatChannel.Value);
            ChatChannel.Value = null;
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
                        Schedule(() =>
                        {
                            if (lastMatchHistoryFetchId != matchId)
                                return;
                            if (ChatChannel.Value != null)
                                chatManager.LeaveChannel(ChatChannel.Value);
                            ChatChannel.Value = chatManager.JoinChannel(new Channel
                            {
                                Id = channelId,
                                Type = ChannelType.Multiplayer,
                                Name = $"#mp_{matchId}",
                            });
                        });
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
            for (int i = 0; i < 16; i++)
            {
                int userId = match.SlotUserIds[i];

                if (spectatorHandlers[i]?.UserId != userId)
                {
                    expireHandler(i);

                    if (userId > 0)
                    {
                        var handler = new StableSpectatorHandler(userId, credentialsUsername, credentialsPasswordHash);
                        handler.OnFramesReceived += bundle => Schedule(() =>
                        {
                            if (!GetActiveSpectatorHandlers().Contains(handler))
                                return;
                            if (CurrentMatch.Value is { } currentMatch && handler.Username != null
                                && RoomPlayers.Any(player => player.UserId == userId && player.Username != handler.Username))
                                updateRoomPlayers(currentMatch);
                            var handlerRuleset = handler.Ruleset.Value?.CreateInstance();
                            int? beatmapId = handler.Beatmap.Value?.BeatmapInfo.OnlineID;
                            if (handlerRuleset == null || beatmapId == null)
                                return;
                            SpectatorDataSource.ReceiveFrames(userId, new SpectatorState
                            {
                                BeatmapID = beatmapId,
                                RulesetID = handlerRuleset.RulesetInfo.OnlineID,
                                Mods = handler.CreateMods(handlerRuleset).Select(m => new APIMod(m)).ToArray(),
                                State = SpectatedUserState.Playing,
                            }, bundle);
                        });
                        handler.OnReplayActionReceived += (action, _) => Schedule(() =>
                        {
                            void applyAction()
                            {
                                if (GetActiveSpectatorHandlers().Contains(handler))
                                    SpectatorDataSource.ReceiveAction(userId, action);
                            }
                            if (action == ReplayAction.Completion)
                                Schedule(applyAction);
                            else
                                applyAction();
                        });
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

            SpectatorDataSource.RemoveUser(spectatorHandlers[index]!.UserId);
            spectatorHandlers[index]!.Expire();
            spectatorHandlers[index] = null;
        }

        private void clearSpectatorHandlers()
        {
            for (int i = 0; i < 16; i++)
                expireHandler(i);
        }

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
        }

        protected override int[] GetTeamIds(TeamColour colour)
            => RoomPlayers.Where(player => player.Team == colour).Select(player => player.UserId).ToArray();

        public override bool SupportsAuthoritativeScores => false;

        public override void RefreshChatChannel()
        {
            if (lastMatchHistoryFetchId is long matchId)
                fetchChannelId((int)matchId);
        }

        public override bool PostChatMessage(string message)
        {
            if (ChatChannel.Value == null || string.IsNullOrWhiteSpace(message))
                return false;
            chatManager.PostMessage(message, target: ChatChannel.Value);
            return true;
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
