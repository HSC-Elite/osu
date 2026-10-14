// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ExceptionExtensions;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Database;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Game.Scoring;
using osu.Game.Screens.OnlinePlay;
using osu.Game.Screens.OnlinePlay.Multiplayer;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.IPC
{
    public partial class LazerRoomMatchInfo : MatchIPCInfo
    {
        [Resolved]
        private MultiplayerClient client { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private BeatmapLookupCache beatmapLookupCache { get; set; } = null!;

        [Resolved]
        private Bindable<WorkingBeatmap> workingBeatmap { get; set; } = null!;

        private readonly OnlinePlayBeatmapAvailabilityTracker beatmapAvailabilityTracker = new MultiplayerBeatmapAvailabilityTracker();
        private IAPIProvider api = null!;

        private readonly Bindable<Room?> currentRoom = new Bindable<Room?>();

        private ChannelManager chatManager = null!;
        private Channel? joinedChatChannel;
        private long? resultRoomId;
        private long? resultPlaylistItemId;

        public IBindable<Room?> CurrentRoom => currentRoom;

        private long lastPlaylistItemId;

        public LazerRoomMatchInfo()
        {
            AddInternal(beatmapAvailabilityTracker);
        }

        [BackgroundDependencyLoader]
        private void load(IAPIProvider api)
        {
            this.api = api;
            AddInternal(chatManager = new ChannelManager(api));
        }

        public override void RequestAuthoritativeScores(Action<IReadOnlyList<TournamentPlayerScoreResult>?> onComplete)
        {
            MultiplayerRoom? room = client.Room;
            long? roomId = resultRoomId ?? room?.RoomID;
            long? playlistItemId = resultPlaylistItemId ?? room?.Settings.PlaylistItemId;

            if (!api.IsLoggedIn || roomId is not > 0 || playlistItemId is not > 0)
            {
                onComplete(null);
                return;
            }

            var request = new IndexPlaylistScoresRequest(roomId.Value, playlistItemId.Value);
            request.Success += response =>
            {
                var results = response.Scores
                                      .Where(score => score.User?.Id > 0)
                                      .Select(score => new TournamentPlayerScoreResult(score.User.Id, createScoreInfo(score)))
                                      .ToArray();

                onComplete(results);
            };
            request.Failure += _ => onComplete(null);
            api.Queue(request);
        }

        private ScoreInfo createScoreInfo(MultiplayerScore score)
        {
            RulesetInfo ruleset = rulesets.GetRuleset(score.RulesetId) ?? new RulesetInfo { OnlineID = score.RulesetId };
            return new ScoreInfo(new BeatmapInfo { OnlineID = score.BeatmapId }, ruleset)
            {
                OnlineID = score.ID,
                TotalScore = score.TotalScore,
                TotalScoreWithoutMods = score.TotalScore,
                Accuracy = score.Accuracy,
                MaxCombo = score.MaxCombo,
                Passed = score.Passed,
                PP = score.PP,
                Statistics = score.Statistics,
                MaximumStatistics = score.MaximumStatistics,
                APIMods = score.Mods ?? Array.Empty<APIMod>(),
            };
        }

        public void Join(Room room, string? password, Action<Room>? onSuccess = null, Action<string, Exception?>? onFailure = null) => Schedule(() =>
        {
            if (!client.IsConnected.Value)
            {
                return;
            }

            client.JoinRoom(room, password).ContinueWith(result =>
            {
                if (result.IsCompletedSuccessfully)
                {
                    Scheduler.Add(() =>
                    {
                        currentRoom.Value = room;
                        HasActiveMatch.Value = true;
                        onSuccess?.Invoke(room);
                    });
                }
                else
                {
                    Scheduler.Add(() =>
                    {
                        currentRoom.Value = null;
                        Exception? exception = result.Exception?.AsSingular();

                        onFailure ??= (m, e) => Logger.Error(e, m);

                        if (exception?.GetHubExceptionMessage() is string message)
                            onFailure?.Invoke(message, exception);
                        else
                            onFailure?.Invoke($"Failed to join multiplayer room. {exception?.Message}", exception);
                    });
                }
            });
        });

        public void Left()
        {
            if (currentRoom.Value == null) return;

            client.LeaveRoom().FireAndForget();
            currentRoom.Value = null;
            HasActiveMatch.Value = false;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            HasActiveMatch.Value = client.Room != null;

            currentRoom.BindValueChanged(onRoomUpdated);
            beatmapAvailabilityTracker.Availability.BindValueChanged(onBeatmapAvailabilityChanged, true);

            Ladder.CurrentMatch.BindValueChanged(_ =>
            {
                updateUsers();
            }, true);

            State.BindValueChanged(_ => captureResultContext(), true);

            client.RoomUpdated += onRoomUpdated;
            client.SettingsChanged += onSettingsChanged;
            client.ItemChanged += onItemChanged;
            client.GameplayAborted += onGameplayAborted;
            client.LoadRequested += onLoadRequested;
            client.UserJoined += _ => updateUsers();
            client.UserLeft += _ => updateUsers();
            client.UserKicked += _ => updateUsers();
            client.UserStateChanged += (_, _) => updateUsers();
            client.ResultsReady += () =>
            {
                if (State.Value == TourneyState.Playing)
                {
                    State.Value = TourneyState.Ranking;
                    Logger.Log("Switching to ranking");
                }
            };
        }

        private void onRoomUpdated() => Scheduler.AddOnce(() =>
        {
            HasActiveMatch.Value = client.Room != null;

            if (currentRoom.Value != null && client.Room == null)
            {
                Logger.Log("exiting room");

                currentRoom.Value = null;
                return;
            }

            if (client.LocalUser != null && client.LocalUser.State != MultiplayerUserState.Spectating)
            {
                client.ToggleSpectate().FireAndForget();
            }

            Logger.Log($"Room status {client.Room?.State} {client.Room?.MatchState} {currentRoom.Value?.Status}");
        });

        private void captureResultContext()
        {
            if (State.Value != TourneyState.Playing || client.Room == null)
                return;

            resultRoomId = client.Room.RoomID;
            resultPlaylistItemId = client.Room.Settings.PlaylistItemId;
        }

        private void onGameplayAborted(GameplayAbortReason reason)
        {
            State.Value = TourneyState.Idle;
        }

        internal override void ForceReSpectate()
        {
            if (client.Room?.State != MultiplayerRoomState.Playing)
                return;

            onLoadRequested();
        }

        private void onLoadRequested()
        {
            SetLiveLeaderboardProvider(null);

            Scheduler.AddOnce(() =>
            {
                State.Value = TourneyState.Idle;

                updateGameplayState();

                if (!workingBeatmap.IsDefault)
                    State.Value = TourneyState.WaitingForClients;
            });
        }

        private void onSettingsChanged(MultiplayerRoomSettings settings)
        {
            if (settings.PlaylistItemId != lastPlaylistItemId)
            {
                onActivePlaylistItemChanged();
                lastPlaylistItemId = settings.PlaylistItemId;
            }
        }

        private void onItemChanged(MultiplayerPlaylistItem item)
        {
            if (item.ID == client.Room?.Settings.PlaylistItemId)
                onActivePlaylistItemChanged();
        }

        private void onActivePlaylistItemChanged()
        {
            if (client.Room == null)
                return;

            Scheduler.AddOnce(updateGameplayState);
        }

        private void updateGameplayState()
        {
            if (client.Room == null || client.LocalUser == null)
                return;

            beatmapLookUpCancellation?.Cancel();

            MultiplayerPlaylistItem item = client.Room.CurrentPlaylistItem;
            int gameplayBeatmapId = client.LocalUser.BeatmapId ?? item.BeatmapID;
            int gameplayRulesetId = client.LocalUser.RulesetId ?? item.RulesetID;

            RulesetInfo ruleset = rulesets.GetRuleset(gameplayRulesetId)!;
            Ruleset rulesetInstance = ruleset.CreateInstance();

            switchWorkingBeatmap(gameplayBeatmapId);

            var existing = Ladder.CurrentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(b => b.ID == gameplayBeatmapId);

            var itemMods = client.LocalUser.Mods.Concat(item.RequiredMods).Select(m => m.ToMod(rulesetInstance)).ToArray();

            if (existing != null)
            {
                Beatmap.Value = existing.Beatmap;
                string modStr = existing.Mods;

                var mod = rulesetInstance.CreateModFromAcronym(modStr);

                Mods.Value = mod != null ? rulesetInstance.ConvertToLegacyMods(new[] { mod }) : rulesetInstance.ConvertToLegacyMods(itemMods);
                return;
            }

            beatmapLookUpCancellation = new CancellationTokenSource();

            beatmapLookupCache.GetBeatmapAsync(gameplayBeatmapId, beatmapLookUpCancellation.Token).ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully)
                    return;

                Scheduler.Add(() =>
                {
                    APIBeatmap? beatmapSet = t.GetResultSafely();
                    if (beatmapSet == null)
                        return;

                    Beatmap.Value = new TournamentBeatmap(beatmapSet);
                    Mods.Value = LegacyMods.None;
                });
            });
        }

        private int? pendingBeatmapId;

        private void switchWorkingBeatmap(int gameplayBeatmapId)
        {
            // 不要在可能旁观或加载中的时候切换谱面 转为pending
            if (!(State.Value == TourneyState.WaitingForClients || State.Value == TourneyState.Playing || State.Value == TourneyState.Ranking))
            {
                var localBeatmap = beatmapManager.QueryBeatmap($@"{nameof(BeatmapInfo.OnlineID)} == $0 AND {nameof(BeatmapInfo.MD5Hash)} == {nameof(BeatmapInfo.OnlineMD5Hash)}", gameplayBeatmapId);
                workingBeatmap.Value = beatmapManager.GetWorkingBeatmap(localBeatmap);
                pendingBeatmapId = null;
            }
            else
            {
                pendingBeatmapId = gameplayBeatmapId;
            }
        }

        private CancellationTokenSource? beatmapLookUpCancellation;

        private void onBeatmapAvailabilityChanged(ValueChangedEvent<BeatmapAvailability> e)
        {
            if (client.Room == null || client.LocalUser == null)
                return;

            client.ChangeBeatmapAvailability(e.NewValue).FireAndForget();

            if (e.NewValue.State == DownloadState.LocallyAvailable)
            {
                updateGameplayState();

                // Optimistically enter spectator if the match is in progress while spectating.
                if (client.LocalUser.State == MultiplayerUserState.Spectating && (client.Room.State == MultiplayerRoomState.WaitingForLoad || client.Room.State == MultiplayerRoomState.Playing))
                    onLoadRequested();
            }
        }

        private void onRoomUpdated(ValueChangedEvent<Room?> room)
        {
            if (room.OldValue != null)
            {
                room.OldValue.PropertyChanged -= onRoomPropertyChanged;
            }

            if (room.NewValue != null)
            {
                room.NewValue.PropertyChanged += onRoomPropertyChanged;
            }

            updateChannel();
            updateUsers();
        }

        private void onRoomPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Room.ChannelId))
                updateChannel();
        }

        private void updateChannel(bool force = false)
        {
            var room = currentRoom.Value;

            if (room?.RoomID == null || room.ChannelId == 0)
            {
                leaveChatChannel();
                ChatChannel.Value = null;
                return;
            }

            if (!force && joinedChatChannel?.Id == room.ChannelId)
                return;

            leaveChatChannel();

            joinedChatChannel = chatManager.JoinChannel(new Channel
            {
                Id = room.ChannelId,
                Type = ChannelType.Multiplayer,
                Name = $"#lazermp_{room.RoomID.Value}",
            });

            ChatChannel.Value = joinedChatChannel;
        }

        public override void RefreshChatChannel() => updateChannel(force: true);

        private void leaveChatChannel()
        {
            if (joinedChatChannel == null)
                return;

            chatManager.LeaveChannel(joinedChatChannel);
            joinedChatChannel = null;
        }

        public override bool PostChatMessage(string message)
        {
            if (joinedChatChannel == null || string.IsNullOrWhiteSpace(message))
                return false;

            chatManager.PostMessage(message, target: joinedChatChannel);
            return true;
        }

        protected override void Dispose(bool isDisposing)
        {
            leaveChatChannel();
            base.Dispose(isDisposing);
        }

        private double waitingForIdle;
        private const int time_to_idle_from_ranking = 25 * 1000;

        private void updateUsers() => Scheduler.AddOnce(() =>
        {
            if (client.Room == null || client.LocalUser == null)
            {
                updateRoomPlayers(Array.Empty<MatchRoomPlayerInfo>());
                return;
            }

            var activeUsers = client.Room.Users.Where(p => p.State != MultiplayerUserState.Spectating).ToArray();
            var slotByUserId = new Dictionary<int, (TeamColour Team, int SlotIndex)>();

            foreach ((TeamColour team, int slotIndex, MultiplayerRoomUser user) in activeUsers
                                                                                   .Where(user => GetTeamIds(TeamColour.Red).Contains(user.UserID))
                                                                                   .Select((user, index) => (TeamColour.Red, index, user))
                                                                                   .Concat(activeUsers
                                                                                           .Where(user => GetTeamIds(TeamColour.Blue).Contains(user.UserID))
                                                                                           .Select((user, index) => (TeamColour.Blue, index, user))))
            {
                slotByUserId[user.UserID] = (team, slotIndex);
            }

            updateRoomPlayers(activeUsers.Select(user =>
            {
                slotByUserId.TryGetValue(user.UserID, out var slot);

                return new MatchRoomPlayerInfo(
                    user.UserID,
                    user.User?.Username,
                    slotByUserId.ContainsKey(user.UserID) ? slot.Team : null,
                    slotByUserId.ContainsKey(user.UserID) ? slot.SlotIndex : null,
                    user.State);
            }));
        });

        private void updateRoomPlayers(IEnumerable<MatchRoomPlayerInfo> players)
            => SetRoomPlayers(players);

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

            if (pendingBeatmapId != null && State.Value == TourneyState.Idle)
            {
                switchWorkingBeatmap(pendingBeatmapId.Value);
                pendingBeatmapId = null;
            }
        }
    }
}
