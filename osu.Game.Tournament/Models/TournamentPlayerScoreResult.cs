// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Scoring;

namespace osu.Game.Tournament.Models
{
    public sealed record TournamentPlayerScoreResult(int UserId, ScoreInfo ScoreInfo);
}
