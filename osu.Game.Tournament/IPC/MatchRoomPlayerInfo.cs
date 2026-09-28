// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.Multiplayer;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.IPC
{
    public readonly record struct MatchRoomPlayerInfo(int UserId, string? Username, TeamColour? Team, int? SlotIndex, MultiplayerUserState? PlayerStatus = null);
}
