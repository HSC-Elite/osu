// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Metadata;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Spectator;
using osu.Game.Replays;
using osu.Game.Rulesets;
using osu.Game.Scoring;
using osu.Game.Screens.OnlinePlay.Multiplayer.Spectate;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select.Leaderboards;
using osu.Game.Screens.Spectate;
using osu.Game.Tournament.IPC;
using Realms;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    internal partial class TournamentSpectatorManager : CompositeDrawable
    {
        public bool HasActiveGameplay => slots.Any(s => s.HasGameplay);

        public bool AllGameplaySlotsLoaded => slots.Where(s => s.HasGameplay).All(s => s.PlayerLoaded);

        [Resolved]
        private SpectatorClient spectatorClient { get; set; } = null!;

        [Resolved]
        private MetadataClient metadataClient { get; set; } = null!;

        [Resolved]
        private UserLookupCache userLookupCache { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private MatchIPCInfo ipc { get; set; } = null!;

        [Resolved]
        private LazerRoomMatchInfo? lazerRoomInfo { get; set; }

        private readonly List<TournamentPlayerSlot> slots = new List<TournamentPlayerSlot>();
        private readonly Dictionary<int, TournamentPlayerSlot> slotsByUserId = new Dictionary<int, TournamentPlayerSlot>();
        private readonly Dictionary<int, APIUser> userMap = new Dictionary<int, APIUser>();
        private readonly Dictionary<int, SpectatorGameplayState> gameplayStates = new Dictionary<int, SpectatorGameplayState>();
        private readonly Dictionary<int, SpectatorPlayerClock> clocksByUserId = new Dictionary<int, SpectatorPlayerClock>();
        private readonly HashSet<int> watchedUsers = new HashSet<int>();
        private readonly IBindableDictionary<int, SpectatorState> watchedUserStates = new BindableDictionary<int, SpectatorState>();

        private MultiSpectatorLeaderboardProvider? leaderboardProvider;

        private MasterGameplayClockContainer? masterClockContainer;
        private SpectatorSyncManager? syncManager;
        private PlayerArea? currentAudioSource;
        private IAggregateAudioAdjustment? boundAdjustments;
        private IDisposable? realmSubscription;
        private IDisposable? userWatchToken;
        private bool readyForSpectatorStateUpdates;

        public TournamentSpectatorManager()
        {
            RelativeSizeAxes = Axes.Both;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            userWatchToken = metadataClient.BeginWatchingUserPresence();
            watchedUserStates.BindTo(spectatorClient.WatchedUserStates);
            watchedUserStates.BindCollectionChanged(onUserStatesChanged, true);
            realmSubscription = realm.RegisterForNotifications(
                realm => realm.All<BeatmapSetInfo>().Where(s => !s.DeletePending), beatmapsChanged);
        }

        public void BeginSpectating(WorkingBeatmap beatmap, IEnumerable<TournamentPlayerSlot> newSlots)
        {
            var newSlotArray = newSlots.ToArray();

            if (!IsLoaded)
            {
                Schedule(() => BeginSpectating(beatmap, newSlotArray));
                return;
            }

            resetSpectatorComponents();

            foreach (var slot in newSlotArray)
                slot.ResetToIdle();

            masterClockContainer = new MasterGameplayClockContainer(beatmap, 0);
            syncManager = new SpectatorSyncManager(masterClockContainer)
            {
                ReadyToStart = performInitialSeek,
            };

            AddInternal(masterClockContainer);
            AddInternal(syncManager);

            masterClockContainer.Reset();
            RefreshRoster(newSlotArray);
        }

        public void RefreshRoster(IEnumerable<TournamentPlayerSlot> newSlots)
        {
            var newSlotArray = newSlots.ToArray();

            if (!IsLoaded)
            {
                Schedule(() => RefreshRoster(newSlotArray));
                return;
            }

            slots.Clear();
            slots.AddRange(newSlotArray);

            var assignedSlots = slots.Where(s => s.UserId != null).ToArray();
            var desiredUserIds = assignedSlots.Select(s => s.UserId!.Value).ToHashSet();
            var oldSlotsByUserId = slotsByUserId.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            foreach (int removedUserId in watchedUsers.Except(desiredUserIds).ToArray())
                removeUser(removedUserId, true);

            slotsByUserId.Clear();

            foreach (var slot in assignedSlots)
            {
                int userId = slot.UserId!.Value;

                if (oldSlotsByUserId.TryGetValue(userId, out var oldSlot) && oldSlot != slot)
                    cleanupGameplay(userId);

                slotsByUserId[userId] = slot;
            }

            foreach (int userId in desiredUserIds.Except(watchedUsers).ToArray())
                addUser(userId);

            readyForSpectatorStateUpdates = true;

            foreach (int userId in desiredUserIds)
            {
                if (watchedUserStates.TryGetValue(userId, out var state))
                    onUserStateChanged(userId, state);
            }
        }

        public void ForceSpectate()
        {
            if (!IsLoaded)
                return;

            ResetAllSlots();

            foreach (int userId in watchedUsers.ToArray())
            {
                spectatorClient.StopWatchingUser(userId);
                spectatorClient.WatchUser(userId);

                if (watchedUserStates.TryGetValue(userId, out var state) && state.State == SpectatedUserState.Playing)
                    startGameplay(userId);
            }
        }

        public void ResetAllSlots()
        {
            cleanupAllGameplay();

            foreach (var slot in slots)
                slot.ResetToIdle();
        }

        public void ResetSlot(int userId)
        {
            cleanupGameplay(userId);

            if (slotsByUserId.TryGetValue(userId, out var slot))
                slot.ResetToIdle();
        }

        public void PanicToIdle()
        {
            ResetAllSlots();
            ipc.State.Value = TourneyState.Idle;
        }

        public void OnRanking()
        {
            Logger.Log("go ranking");

            Scheduler.AddDelayed(() =>
            {
                foreach (var slot in slots.Where(s => s.HasGameplay))
                    slot.ForceToResult();
            }, 5 * 1000);
        }

        protected override void Update()
        {
            base.Update();

            checkAudioSource();

            if (ipc.State.Value == TourneyState.WaitingForClients && HasActiveGameplay && AllGameplaySlotsLoaded)
                ipc.State.Value = TourneyState.Playing;
        }

        private void addUser(int userId)
        {
            watchedUsers.Add(userId);

            if (!userMap.ContainsKey(userId))
            {
                userMap[userId] = new APIUser
                {
                    Id = userId,
                    Username = findRoomUser(userId)?.User?.Username ?? $"User {userId}",
                };
            }

            spectatorClient.WatchUser(userId);

            userLookupCache.GetUsersAsync(new[] { userId }).ContinueWith(task => Schedule(() =>
            {
                var users = task.GetResultSafely();

                if (users.Length > 0 && users[0] != null)
                    userMap[userId] = users[0]!;

                readyForSpectatorStateUpdates = true;

                if (watchedUserStates.TryGetValue(userId, out var state))
                    onUserStateChanged(userId, state);
            }));
        }

        private void removeUser(int userId, bool stopWatching)
        {
            cleanupGameplay(userId);

            if (slotsByUserId.Remove(userId, out var slot) && slots.Contains(slot))
                slot.ResetToIdle();

            userMap.Remove(userId);
            watchedUsers.Remove(userId);

            if (stopWatching)
                spectatorClient.StopWatchingUser(userId);
        }

        private MultiSpectatorLeaderboardProvider? ensureLeaderboardProvider()
        {
            if (leaderboardProvider != null)
                return leaderboardProvider;

            var users = slotsByUserId.Keys
                                     .Select(id => findRoomUser(id) ?? new MultiplayerRoomUser(id))
                                     .ToArray();

            if (users.Length == 0)
                return null;

            var providerUserIds = users.Select(u => u.UserID).ToHashSet();
            var provider = leaderboardProvider = new MultiSpectatorLeaderboardProvider(users);

            if (lazerRoomInfo != null)
                lazerRoomInfo.LeaderboardProvider = provider;

            LoadComponentAsync(provider, loadedProvider =>
            {
                if (leaderboardProvider != loadedProvider)
                {
                    loadedProvider.Expire();
                    return;
                }

                AddInternal(loadedProvider);

                foreach ((int userId, var clock) in clocksByUserId)
                {
                    if (providerUserIds.Contains(userId))
                        loadedProvider.AddClock(userId, clock);
                }

                if (lazerRoomInfo != null)
                    lazerRoomInfo.LeaderboardProvider = loadedProvider;
            });

            return provider;
        }

        private void disposeLeaderboardProvider()
        {
            if (lazerRoomInfo != null)
                lazerRoomInfo.LeaderboardProvider = null;

            leaderboardProvider?.Expire();
            leaderboardProvider = null;
        }

        private MultiplayerRoomUser? findRoomUser(int userId)
            => lazerRoomInfo?.RoomUser.FirstOrDefault(u => u.UserID == userId);

        private void beatmapsChanged(IRealmCollection<BeatmapSetInfo> items, ChangeSet? changes)
        {
            if (changes?.InsertedIndices == null) return;

            foreach (int c in changes.InsertedIndices)
                beatmapUpdated(items[c]);
        }

        private void beatmapUpdated(BeatmapSetInfo beatmapSet)
        {
            foreach (int userId in watchedUsers)
            {
                if (!watchedUserStates.TryGetValue(userId, out var userState))
                    continue;

                if (beatmapSet.Beatmaps.Any(b => b.OnlineID == userState.BeatmapID))
                    startGameplay(userId);
            }
        }

        private void onUserStatesChanged(object? sender, NotifyDictionaryChangedEventArgs<int, SpectatorState> e)
        {
            switch (e.Action)
            {
                case NotifyDictionaryChangedAction.Add:
                case NotifyDictionaryChangedAction.Replace:
                    foreach ((int userId, SpectatorState state) in e.NewItems.AsNonNull())
                        onUserStateChanged(userId, state);
                    break;
            }
        }

        private void onUserStateChanged(int userId, SpectatorState newState)
        {
            if (!readyForSpectatorStateUpdates)
                return;

            if (newState.RulesetID == null || newState.BeatmapID == null)
                return;

            if (!slotsByUserId.ContainsKey(userId))
                return;

            switch (newState.State)
            {
                case SpectatedUserState.Playing:
                    startGameplay(userId);
                    break;

                case SpectatedUserState.Passed:
                    markReceivedAllFrames(userId);
                    PassGameplay(userId);
                    break;

                case SpectatedUserState.Failed:
                    failGameplay(userId);
                    break;

                case SpectatedUserState.Quit:
                    quitGameplay(userId);
                    break;
            }
        }

        private void startGameplay(int userId)
        {
            if (syncManager == null)
                return;

            if (!userMap.TryGetValue(userId, out var user))
                return;

            if (!slotsByUserId.TryGetValue(userId, out var slot))
                return;

            if (slot.HasGameplay)
                return;

            if (!watchedUserStates.TryGetValue(userId, out var spectatorState))
                return;

            Debug.Assert(userMap.ContainsKey(userId));

            var resolvedRuleset = rulesets.AvailableRulesets.FirstOrDefault(r => r.OnlineID == spectatorState.RulesetID)?.CreateInstance();
            if (resolvedRuleset == null)
                return;

            var resolvedBeatmap = beatmaps.QueryBeatmap(b => b.OnlineID == spectatorState.BeatmapID);
            if (resolvedBeatmap == null)
                return;

            var score = new Score
            {
                ScoreInfo = new ScoreInfo
                {
                    BeatmapInfo = resolvedBeatmap,
                    User = user,
                    Mods = spectatorState.Mods.Select(m => m.ToMod(resolvedRuleset)).ToArray(),
                    Ruleset = resolvedRuleset.RulesetInfo,
                },
                Replay = new Replay { HasReceivedAllFrames = false },
            };

            var gameplayState = new SpectatorGameplayState(score, resolvedRuleset, beatmaps.GetWorkingBeatmap(resolvedBeatmap));
            gameplayStates[userId] = gameplayState;
            startSlotGameplay(userId, gameplayState);
        }

        private void startSlotGameplay(int userId, SpectatorGameplayState gameplayState)
        {
            if (syncManager == null)
                return;

            var provider = ensureLeaderboardProvider();

            if (provider == null)
                return;

            if (!slotsByUserId.TryGetValue(userId, out var slot))
                return;

            if (slot.HasGameplay)
                return;

            var clock = syncManager.CreateManagedClock();

            if (syncManager.HasStarted)
                clock.Seek(syncManager.CurrentMasterTime);

            clocksByUserId[userId] = clock;
            slot.StartGameplay(gameplayState.Score, clock, provider);

            if (provider.IsLoaded)
                provider.AddClock(userId, clock);
        }

        private void markReceivedAllFrames(int userId)
        {
            if (gameplayStates.TryGetValue(userId, out var gameplayState))
                gameplayState.Score.Replay.HasReceivedAllFrames = true;
        }

        private void failGameplay(int userId)
        {
            if (!gameplayStates.ContainsKey(userId))
                return;

            markReceivedAllFrames(userId);
            gameplayStates.Remove(userId);
            FailGameplay(userId);
        }

        private void quitGameplay(int userId)
        {
            if (!gameplayStates.ContainsKey(userId))
                return;

            markReceivedAllFrames(userId);
            gameplayStates.Remove(userId);
            QuitGameplay(userId);
        }

        private void PassGameplay(int userId)
        {
            if (slotsByUserId.TryGetValue(userId, out var slot))
            {
                Scheduler.AddDelayed(() => slot.ForceToResult(), 5 * 1000);
            }
        }

        private void FailGameplay(int userId)
        {
            removeManagedClock(userId);

            if (slotsByUserId.TryGetValue(userId, out var slot))
                slot.MarkFailedOrQuit();
        }

        private void QuitGameplay(int userId)
        {
            removeManagedClock(userId);

            if (slotsByUserId.TryGetValue(userId, out var slot))
                slot.MarkFailedOrQuit();
        }

        private void cleanupAllGameplay()
        {
            foreach (int userId in clocksByUserId.Keys.ToArray())
                removeManagedClock(userId);

            gameplayStates.Clear();
            disposeLeaderboardProvider();
            currentAudioSource = null;
            boundAdjustments = null;
        }

        private void cleanupGameplay(int userId)
        {
            removeManagedClock(userId);
            gameplayStates.Remove(userId);
        }

        private void removeManagedClock(int userId)
        {
            if (!clocksByUserId.Remove(userId, out var clock))
                return;

            syncManager?.RemoveManagedClock(clock);
        }

        private IEnumerable<PlayerArea> activePlayerAreas => slots.Select(s => s.PlayerArea).OfType<PlayerArea>();

        private void checkAudioSource()
        {
            if (syncManager == null || masterClockContainer == null)
                return;

            var candidate = activePlayerAreas.Where(i => isCandidateAudioSource(i.SpectatorPlayerClock))
                                             .MinBy(i => Math.Abs(i.SpectatorPlayerClock.CurrentTime - syncManager.CurrentMasterTime));

            if (candidate == currentAudioSource)
                return;

            currentAudioSource = candidate;

            if (currentAudioSource != null)
                bindAudioAdjustments(currentAudioSource);

            foreach (var instance in activePlayerAreas)
                instance.Mute = instance != currentAudioSource;
        }

        private void bindAudioAdjustments(PlayerArea first)
        {
            if (masterClockContainer == null)
                return;

            if (boundAdjustments != null)
                masterClockContainer.AdjustmentsFromMods.UnbindAdjustments(boundAdjustments);

            boundAdjustments = first.ClockAdjustmentsFromMods;
            masterClockContainer.AdjustmentsFromMods.BindAdjustments(boundAdjustments);
        }

        private bool isCandidateAudioSource(SpectatorPlayerClock? clock)
            => clock?.IsRunning == true && !clock.IsCatchingUp && !clock.WaitingOnFrames;

        private void performInitialSeek()
        {
            if (masterClockContainer == null)
                return;

            var minFrameTimes = new List<double>();

            foreach (var instance in activePlayerAreas)
            {
                if (instance.Score == null)
                    continue;

                minFrameTimes.Add(instance.Score.Replay.Frames.MinBy(f => f.Time)?.Time ?? 0);
            }

            if (minFrameTimes.Count == 0)
            {
                masterClockContainer.Reset(0, true);
                return;
            }

            double mean = minFrameTimes.Average();
            minFrameTimes.RemoveAll(t => mean - t > 1000);

            double startTime = minFrameTimes.Min();

            if (startTime < 10000)
                startTime = 0;

            masterClockContainer.Reset(startTime, true);
            Logger.Log($"Multiplayer spectator seeking to initial time of {startTime}");
        }

        private void resetSpectatorComponents()
        {
            readyForSpectatorStateUpdates = false;
            cleanupAllGameplay();
            currentAudioSource = null;

            masterClockContainer?.Expire();
            syncManager?.Expire();
            masterClockContainer = null;
            syncManager = null;
        }

        protected override void Dispose(bool isDisposing)
        {
            disposeLeaderboardProvider();

            base.Dispose(isDisposing);

            if (spectatorClient.IsNotNull())
            {
                foreach (int userId in watchedUsers)
                    spectatorClient.StopWatchingUser(userId);
            }

            realmSubscription?.Dispose();
            userWatchToken?.Dispose();
        }
    }
}
