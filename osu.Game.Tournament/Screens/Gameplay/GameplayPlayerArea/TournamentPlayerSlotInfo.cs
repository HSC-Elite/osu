// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.Multiplayer;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Screens.Gameplay.GameplayPlayerArea
{
    internal readonly record struct TournamentPlayerSlotInfo(TeamColour Colour, int Index, MultiplayerRoomUser? User);
}
