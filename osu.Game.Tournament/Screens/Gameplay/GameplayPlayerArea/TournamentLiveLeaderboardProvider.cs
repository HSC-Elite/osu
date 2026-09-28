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
using osuTK.Graphics;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    internal partial class TournamentLiveLeaderboardProvider : CompositeComponent, IGameplayLeaderboardProvider
    {
        public IBindableList<GameplayLeaderboardScore> Scores => scores;

        private readonly BindableList<GameplayLeaderboardScore> scores = new BindableList<GameplayLeaderboardScore>();
        private readonly MatchRoomPlayerInfo[] players;
        private readonly Dictionary<int, SpectatorScoreProcessor> scoreProcessors = new Dictionary<int, SpectatorScoreProcessor>();
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

        public TournamentLiveLeaderboardProvider(MatchRoomPlayerInfo[] players)
        {
            this.players = players;
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config, IAPIProvider api, CancellationToken cancellationToken)
        {
            config.BindWith(OsuSetting.ScoreDisplayMode, scoringMode);

            foreach (var player in players)
            {
                var processor = new SpectatorScoreProcessor(player.UserId);
                processor.Mode.BindTo(scoringMode);
                processor.TotalScore.BindValueChanged(_ => sorting.Invalidate());
                AddInternal(processor);
                scoreProcessors[player.UserId] = processor;
            }

            userLookupCache.GetUsersAsync(players.Select(p => p.UserId).ToArray(), cancellationToken)
                           .ContinueWith(task => Schedule(() =>
                           {
                               var lookedUpUsers = task.GetResultSafely();

                               for (int i = 0; i < players.Length; i++)
                               {
                                   var player = players[i];
                                   var user = lookedUpUsers[i] ?? new APIUser
                                   {
                                       Id = player.UserId,
                                       Username = player.Username ?? $"User {player.UserId}",
                                   };

                                   var leaderboardScore = new GameplayLeaderboardScore(
                                       user,
                                       scoreProcessors[player.UserId],
                                       user.Id == api.LocalUser.Value.Id,
                                       GameplayLeaderboardScore.ComboDisplayMode.Current)
                                   {
                                       TeamColour = getTeamColour(player.Team),
                                       HasQuit = { Value = quitUsers.Contains(player.UserId) },
                                   };
                                   leaderboardScore.DisplayOrder.BindValueChanged(_ => sorting.Invalidate(), true);
                                   scores.Add(leaderboardScore);
                               }
                           }), cancellationToken);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            watchedUserStates.BindTo(spectatorDataSource.WatchedUserStates);
            watchedUserStates.BindCollectionChanged(onWatchedUserStatesChanged, true);

            foreach (var player in players)
                spectatorDataSource.WatchUser(player.UserId);

            Scheduler.AddDelayed(sort, 1000, true);
        }

        public Mod[] GetPlayerMods(int userId)
            => scoreProcessors.TryGetValue(userId, out var processor) ? processor.Mods.ToArray() : Array.Empty<Mod>();

        public void AddClock(int userId, osu.Framework.Timing.IClock clock)
        {
            if (!scoreProcessors.TryGetValue(userId, out var processor))
                throw new ArgumentException("Provided user is not tracked by this leaderboard.", nameof(userId));

            processor.ReferenceClock = clock;
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
            foreach ((int userId, SpectatorState state) in e.NewItems.AsNonNull())
            {
                if (state.State == SpectatedUserState.Quit)
                {
                    quitUsers.Add(userId);
                    var score = scores.FirstOrDefault(s => s.User.OnlineID == userId);
                    if (score != null)
                        score.HasQuit.Value = true;
                }
            }
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
                foreach (var player in players)
                    spectatorDataSource.StopWatchingUser(player.UserId);
            }
        }
    }
}
