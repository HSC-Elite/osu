// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Scoring
{
    /// <summary>
    /// Uses stable ScoreV2 multipliers for legacy mods, retaining lazer multipliers for lazer-only mods.
    /// </summary>
    public class OsuStableScoreV2MultiplierCalculator : OsuScoreMultiplierCalculatorV2
    {
        public OsuStableScoreV2MultiplierCalculator(ScoreMultiplierContext context)
            : base(context)
        {
            // Match stable ModManager.ScoreMultiplierV2 for osu!standard.
            Single<OsuModEasy>(hasMultiplier: 0.5);
            Single<OsuModNoFail>(hasMultiplier: 1);
            Single<OsuModHalfTime>(hasMultiplier: 0.3);
            Single<OsuModHardRock>(hasMultiplier: 1.10);
            Single<OsuModDoubleTime>(hasMultiplier: 1.20);
            Single<OsuModNightcore>(hasMultiplier: 1.20);
            Single<OsuModHidden>(hasMultiplier: 1.06);
            Single<OsuModFlashlight>(hasMultiplier: 1.12);
            Single<OsuModSpunOut>(hasMultiplier: 0.9);
            Single<OsuModRelax>(hasMultiplier: 0);
            Single<OsuModAutopilot>(hasMultiplier: 0);

            // Classic supplies stable gameplay behaviour, without an additional score penalty.
            Single<OsuModClassic>(hasMultiplier: 1);
        }
    }
}
