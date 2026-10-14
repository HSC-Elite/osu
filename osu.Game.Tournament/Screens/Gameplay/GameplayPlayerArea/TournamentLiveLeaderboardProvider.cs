// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Caching;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics.Containers;
using osu.Framework.Timing;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Spectator;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osu.Game.Users;
using osuTK.Graphics;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    internal partial class TournamentLiveLeaderboardProvider : CompositeComponent, IGameplayLeaderboardProvider
    {
        public IBindableList<GameplayLeaderboardScore> Scores => scores;

        private readonly BindableList<GameplayLeaderboardScore> scores = new BindableList<GameplayLeaderboardScore>();
        private readonly Dictionary<int, MatchRoomPlayerInfo> players = new Dictionary<int, MatchRoomPlayerInfo>();
        private readonly Dictionary<int, SpectatorScoreProcessor> scoreProcessors = new Dictionary<int, SpectatorScoreProcessor>();
        private readonly Dictionary<int, TournamentLivePerformanceProcessor> performanceProcessors = new Dictionary<int, TournamentLivePerformanceProcessor>();
        private readonly HashSet<int> quitUsers = new HashSet<int>();
        private readonly IBindableDictionary<int, SpectatorState> watchedUserStates = new BindableDictionary<int, SpectatorState>();
        private readonly Bindable<ScoringMode> scoringMode = new Bindable<ScoringMode>();
        private readonly Cached sorting = new Cached();

        [Resolved]
        private ISpectatorDataSource spectatorDataSource { get; set; } = null!;

        [Resolved]
        private UserLookupCache userLookupCache { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private CancellationToken cancellationToken;
        private bool loadStarted;
        private bool watchingUsers;

        public TournamentLiveLeaderboardProvider(MatchRoomPlayerInfo[] players)
        {
            foreach (var player in players)
                AddPlayer(player);
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config, CancellationToken cancellationToken)
        {
            loadStarted = true;
            this.cancellationToken = cancellationToken;
            config.BindWith(OsuSetting.ScoreDisplayMode, scoringMode);

            foreach (var processor in scoreProcessors.Values)
                processor.Mode.BindTo(scoringMode);

            MatchRoomPlayerInfo[] playersToLookup = players.Values.ToArray();

            if (playersToLookup.Length == 0)
                return;

            userLookupCache.GetUsersAsync(playersToLookup.Select(p => p.UserId).ToArray(), cancellationToken)
                           .ContinueWith(task => Schedule(() =>
                           {
                               var lookedUpUsers = task.GetResultSafely();

                               for (int i = 0; i < playersToLookup.Length; i++)
                               {
                                   if (i >= lookedUpUsers.Length)
                                       break;

                                   addLeaderboardScore(playersToLookup[i], lookedUpUsers[i]);
                               }
                           }), cancellationToken);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            watchedUserStates.BindTo(spectatorDataSource.WatchedUserStates);
            watchedUserStates.BindCollectionChanged(onWatchedUserStatesChanged, true);

            watchingUsers = true;

            foreach (var player in players.Values)
                spectatorDataSource.WatchUser(player.UserId);

            Scheduler.AddDelayed(sort, 1000, true);
        }

        public Mod[] GetPlayerMods(int userId)
            => scoreProcessors.TryGetValue(userId, out var processor) ? processor.Mods.ToArray() : Array.Empty<Mod>();

        public long GetPlayerPerformancePoints(int userId)
            => performanceProcessors.TryGetValue(userId, out var processor) ? processor.PerformancePoints.Value : 0;

        public IBindable<int>? GetPlayerCombo(int userId)
            => scoreProcessors.TryGetValue(userId, out var processor) ? processor.Combo : null;

        public void AddPlayer(MatchRoomPlayerInfo player)
        {
            if (players.TryGetValue(player.UserId, out var previous))
            {
                players[player.UserId] = player;

                if (previous.Team != player.Team && scores.FirstOrDefault(score => score.User.OnlineID == player.UserId) is { } existingScore)
                {
                    scores.Remove(existingScore);
                    addLeaderboardScore(player, existingScore.User, existingScore.Tracked);
                }

                return;
            }

            players.Add(player.UserId, player);
            createScoreProcessor(player.UserId);

            if (!loadStarted)
                return;

            if (watchingUsers)
                spectatorDataSource.WatchUser(player.UserId);

            userLookupCache.GetUsersAsync(new[] { player.UserId }, cancellationToken)
                           .ContinueWith(task => Schedule(() =>
                           {
                               if (!IsAlive || !players.TryGetValue(player.UserId, out var currentPlayer))
                                   return;

                               var users = task.GetResultSafely();
                               addLeaderboardScore(currentPlayer, users.FirstOrDefault());
                           }), cancellationToken);
        }

        public void RemovePlayer(int userId)
        {
            if (!players.Remove(userId))
                return;

            var existingScore = scores.FirstOrDefault(score => score.User.OnlineID == userId);

            if (existingScore != null)
                scores.Remove(existingScore);

            if (scoreProcessors.Remove(userId, out var processor))
                processor.Expire();

            if (performanceProcessors.Remove(userId, out var performanceProcessor))
                performanceProcessor.Expire();

            quitUsers.Remove(userId);

            if (watchingUsers)
                spectatorDataSource.StopWatchingUser(userId);

            sorting.Invalidate();
        }

        public void AddClock(int userId, IClock clock)
        {
            if (!scoreProcessors.TryGetValue(userId, out var processor))
                throw new ArgumentException("Provided user is not tracked by this leaderboard.", nameof(userId));

            processor.ReferenceClock = clock;
            performanceProcessors[userId].SetReferenceClock(clock);
        }

        protected override void Update()
        {
            base.Update();

            foreach (var processor in scoreProcessors.Values)
                processor.UpdateScore();

            sort();
        }

        private void onWatchedUserStatesChanged(object? sender, NotifyDictionaryChangedEventArgs<int, SpectatorState> e)
        {
            if (e.Action is not (NotifyDictionaryChangedAction.Add or NotifyDictionaryChangedAction.Replace) || e.NewItems == null)
                return;

            foreach ((int userId, SpectatorState state) in e.NewItems)
            {
                if (state.State == SpectatedUserState.Quit)
                {
                    quitUsers.Add(userId);
                    var score = scores.FirstOrDefault(s => s.User.OnlineID == userId);
                    score?.HasQuit.Value = true;
                }
            }
        }

        private SpectatorScoreProcessor createScoreProcessor(int userId)
        {
            var processor = new SpectatorScoreProcessor(userId);
            processor.TotalScore.BindValueChanged(_ => sorting.Invalidate());

            if (loadStarted)
                processor.Mode.BindTo(scoringMode);

            AddInternal(processor);
            scoreProcessors[userId] = processor;

            var performanceProcessor = new TournamentLivePerformanceProcessor(userId);
            AddInternal(performanceProcessor);
            performanceProcessors[userId] = performanceProcessor;

            return processor;
        }

        private void addLeaderboardScore(MatchRoomPlayerInfo player, IUser? user = null, bool? tracked = null)
        {
            if (!players.TryGetValue(player.UserId, out var currentPlayer) || scores.Any(score => score.User.OnlineID == player.UserId))
                return;

            player = currentPlayer;

            user ??= new APIUser
            {
                Id = player.UserId,
                Username = player.Username ?? $"User {player.UserId}",
            };

            if (!scoreProcessors.TryGetValue(player.UserId, out var processor))
                processor = createScoreProcessor(player.UserId);

            var leaderboardScore = new GameplayLeaderboardScore(
                user,
                processor,
                tracked ?? user.OnlineID == api.LocalUser.Value.Id,
                GameplayLeaderboardScore.ComboDisplayMode.Current)
            {
                TeamColour = getTeamColour(player.Team),
                HasQuit = { Value = quitUsers.Contains(player.UserId) },
            };
            leaderboardScore.DisplayOrder.BindValueChanged(_ => sorting.Invalidate(), true);
            scores.Add(leaderboardScore);
            sorting.Invalidate();
        }

        private Color4? getTeamColour(TeamColour? team)
        {
            return team switch
            {
                TeamColour.Red => colours.TeamColourRed.Lighten(1.2f),
                TeamColour.Blue => colours.TeamColourBlue.Lighten(1.2f),
                _ => null,
            };
        }

        private void sort()
        {
            if (sorting.IsValid)
                return;

            var ordered = scores.OrderByDescending(s => s.TotalScore.Value)
                                .ThenBy(s => s.TotalScoreTiebreaker)
                                .ToArray();

            for (int i = 0; i < ordered.Length; i++)
            {
                ordered[i].DisplayOrder.Value = i;
                ordered[i].Position.Value = i + 1;
            }

            sorting.Validate();
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (spectatorDataSource.IsNotNull())
            {
                foreach (int userId in players.Keys)
                    spectatorDataSource.StopWatchingUser(userId);
            }
        }
    }
}
