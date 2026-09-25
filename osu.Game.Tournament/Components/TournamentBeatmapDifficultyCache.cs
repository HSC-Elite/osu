// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Database;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Calculates and caches Tournament beatmap difficulty attributes from local .osu files.
    /// </summary>
    public partial class TournamentBeatmapDifficultyCache : MemoryCachingComponent<TournamentBeatmapDifficultyCache.DifficultyCacheLookup, DifficultyAttributes?>
    {
        private readonly TournamentBeatmapManager beatmapManager;
        private readonly RulesetStore rulesets;

        public TournamentBeatmapDifficultyCache(TournamentBeatmapManager beatmapManager, RulesetStore rulesets)
        {
            this.beatmapManager = beatmapManager;
            this.rulesets = rulesets;
        }

        public async Task<DifficultyAttributes?> GetDifficultyAsync(
            TournamentBeatmap beatmap,
            RulesetInfo ruleset,
            LegacyMods mods,
            bool downloadIfMissing = true,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(beatmap);
            ArgumentNullException.ThrowIfNull(ruleset);

            if (!await beatmapManager.EnsureBeatmapAsync(beatmap, downloadIfMissing, cancellationToken).ConfigureAwait(false))
                return null;

            LegacyMods normalisedMods = normaliseMods(mods);

            var lookup = new DifficultyCacheLookup(
                beatmap.OnlineID,
                beatmap.MD5Hash,
                ruleset.ShortName,
                normalisedMods);

            return await GetAsync(lookup, cancellationToken).ConfigureAwait(false);
        }

        protected override Task<DifficultyAttributes?> ComputeValueAsync(DifficultyCacheLookup lookup, CancellationToken cancellationToken = default)
        {
            RulesetInfo? rulesetInfo = rulesets.GetRuleset(lookup.RulesetShortName);

            if (rulesetInfo == null)
                return Task.FromResult<DifficultyAttributes?>(null);

            return Task.Run(() =>
            {
                var beatmap = new TournamentBeatmap
                {
                    OnlineID = lookup.OnlineID,
                    MD5Hash = lookup.MD5Hash,
                };

                WorkingBeatmap workingBeatmap = beatmapManager.GetWorkingBeatmap(beatmap);
                Ruleset ruleset = rulesetInfo.CreateInstance();
                Mod[] convertedMods = ruleset.ConvertFromLegacyMods(lookup.Mods).ToArray();

                return ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(convertedMods, cancellationToken);
            }, cancellationToken)!;
        }

        private static LegacyMods normaliseMods(LegacyMods mods)
        {
            mods &= ~LegacyMods.FreeMod;
            mods &= ~LegacyMods.NoMod;
            return mods;
        }

        public readonly record struct DifficultyCacheLookup(
            int OnlineID,
            string MD5Hash,
            string RulesetShortName,
            LegacyMods Mods);
    }
}
