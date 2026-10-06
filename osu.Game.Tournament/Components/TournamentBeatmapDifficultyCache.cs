// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Database;
using osu.Game.Online.API;
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
        private readonly Dictionary<TimedDifficultyLookup, Task<List<TimedDifficultyAttributes>?>> timedDifficultyTasks = new Dictionary<TimedDifficultyLookup, Task<List<TimedDifficultyAttributes>?>>();

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

        public Task<List<TimedDifficultyAttributes>?> GetTimedDifficultyAttributesAsync(TournamentBeatmap beatmap, RulesetInfo ruleset, Mod[] mods)
        {
            var lookup = new TimedDifficultyLookup(
                beatmap.OnlineID,
                beatmap.MD5Hash,
                ruleset.ShortName,
                JsonConvert.SerializeObject(mods.Select(mod => new APIMod(mod)).OrderBy(mod => mod.Acronym)));

            if (!timedDifficultyTasks.TryGetValue(lookup, out var task))
                timedDifficultyTasks.Add(lookup, task = computeTimedDifficultyAsync(beatmap, ruleset, mods));

            return task;
        }

        private async Task<List<TimedDifficultyAttributes>?> computeTimedDifficultyAsync(TournamentBeatmap beatmap, RulesetInfo rulesetInfo, Mod[] mods)
        {
            if (!await beatmapManager.EnsureBeatmapAsync(beatmap, downloadIfMissing: false).ConfigureAwait(false))
                return null;

            WorkingBeatmap workingBeatmap = beatmapManager.GetWorkingBeatmap(beatmap);
            Ruleset ruleset = rulesetInfo.CreateInstance();

            return await Task.Run(() => ruleset.CreateDifficultyCalculator(workingBeatmap).CalculateTimed(mods)).ConfigureAwait(false);
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

        private readonly record struct TimedDifficultyLookup(int OnlineID, string MD5Hash, string RulesetShortName, string Mods);
    }
}
