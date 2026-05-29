// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.OnlinePlay.Multiplayer.Spectate;
using osu.Game.Screens.Select.Leaderboards;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    internal partial class TournamentPlayerSlot : CompositeDrawable
    {
        public TeamColour TeamColour { get; }
        public int Index { get; }
        public int? UserId { get; private set; }

        public bool HasGameplay => gameplayScreen != null;
        public bool PlayerLoaded => gameplayScreen?.PlayerArea.PlayerLoaded == true;
        public PlayerArea? PlayerArea => gameplayScreen?.PlayerArea;

        private readonly OsuScreenStack stack;
        private readonly IdlePlayerScreen idleScreen;
        private TournamentPlayerGameplayScreen? gameplayScreen;

        public TournamentPlayerSlot(TeamColour colour, int index)
        {
            TeamColour = colour;
            Index = index;
            RelativeSizeAxes = Axes.Both;

            InternalChild = stack = new OsuScreenStack
            {
                RelativeSizeAxes = Axes.Both,
            };

            stack.Push(idleScreen = new IdlePlayerScreen(index, colour));
        }

        public void ApplySlotInfo(TournamentPlayerSlotInfo info)
        {
            if (UserId != info.User?.UserID)
                ResetToIdle();

            UserId = info.User?.UserID;
        }

        public void SetSmallLogo(bool small) => idleScreen.SmallOsuLogo = small;

        public void StartGameplay(Score score, SpectatorPlayerClock clock, MultiSpectatorLeaderboardProvider leaderboardProvider)
        {
            if (HasGameplay)
                return;

            if (UserId == null)
                return;

            var playerArea = new PlayerArea(UserId.Value, clock);
            gameplayScreen = new TournamentPlayerGameplayScreen(playerArea, score, leaderboardProvider);
            stack.Push(gameplayScreen);
        }

        public void ResetToIdle()
        {
            if (stack.CurrentScreen != idleScreen)
                stack.Exit();

            gameplayScreen = null;
        }

        public void MarkFailedOrQuit()
        {
            if (gameplayScreen?.PlayerArea != null)
                gameplayScreen.PlayerArea.FadeColour(Colour4.Gray, 400, Easing.OutQuint);
        }

        public void ForceToResult()
        {
            if (gameplayScreen?.PlayerArea.Player is MultiSpectatorPlayer spectatorPlayer)
                spectatorPlayer.ForceToResult();
        }
    }
}
