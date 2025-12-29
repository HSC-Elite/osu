// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring.Legacy;

namespace osu.Game.Screens.Play.HUD
{
    public abstract partial class GameplayScoreCounter : ScoreCounter
    {
        private Bindable<ScoringMode> scoreDisplayMode = null!;

        private Bindable<long> totalScoreBindable = null!;

        [Resolved]
        private IModMultiplierProvider? modMultiplierProvider { get; set; }

        protected GameplayScoreCounter()
            : base(8)
        {
        }

        private double? multiplier;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config, ScoreProcessor scoreProcessor)
        {
            totalScoreBindable = scoreProcessor.TotalScore.GetBoundCopy();
            totalScoreBindable.BindValueChanged(_ => updateDisplayScore());

            scoreDisplayMode = config.GetBindable<ScoringMode>(OsuSetting.ScoreDisplayMode);
            updateDisplayScore();

            void updateDisplayScore()
            {
                if (modMultiplierProvider != null && multiplier == null)
                {
                    multiplier = scoreProcessor.Mods.Value.Aggregate(1.0, (acc, mod) => acc * (modMultiplierProvider.GetModMultiplierFromMod(mod) ?? 1.0));
                }

                Current.Value = (long)(scoreProcessor.GetDisplayScore(scoreDisplayMode.Value) * multiplier ?? 1);
            }
        }
    }
}
