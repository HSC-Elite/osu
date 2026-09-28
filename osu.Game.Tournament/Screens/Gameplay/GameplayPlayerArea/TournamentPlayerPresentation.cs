// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.OnlinePlay.Multiplayer.Spectate;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    internal abstract partial class TournamentPlayerPresentation : OsuScreen
    {
        public abstract bool PlayerLoaded { get; }
        public abstract IBindable<int>? Combo { get; }
        public abstract bool IsAudioSourceCandidate { get; }
        public abstract double CurrentTime { get; }
        public abstract Score? ReplayScore { get; }
        public abstract IAggregateAudioAdjustment ClockAdjustmentsFromMods { get; }

        public abstract void MarkFailedOrQuit();
        public abstract void ForceToResult();
        public abstract void SetMuted(bool muted);
    }

    internal interface ITournamentPlayerPresentationFactory
    {
        TournamentPlayerPresentation Create(TournamentPlayerSlot slot, Score score, SpectatorPlayerClock clock, TournamentLiveLeaderboardProvider leaderboardProvider);
    }

    internal sealed class LazerTournamentPlayerPresentationFactory : ITournamentPlayerPresentationFactory
    {
        public TournamentPlayerPresentation Create(TournamentPlayerSlot slot, Score score, SpectatorPlayerClock clock, TournamentLiveLeaderboardProvider leaderboardProvider)
        {
            if (!slot.UserId.HasValue)
                throw new ArgumentException("A presentation cannot be created for an unassigned slot.", nameof(slot));

            var playerArea = new PlayerArea(slot.UserId.Value, clock);
            return new TournamentPlayerGameplayScreen(playerArea, score, leaderboardProvider);
        }
    }
}
