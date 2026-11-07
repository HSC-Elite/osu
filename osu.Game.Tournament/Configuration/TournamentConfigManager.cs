// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Configuration;
using osu.Framework.Platform;

namespace osu.Game.Tournament.Configuration
{
    public class TournamentConfigManager : IniConfigManager<StorageConfig>
    {
        protected override string Filename => "tournament.ini";

        private const string default_tournament = "default";

        public TournamentConfigManager(Storage storage)
            : base(storage)
        {
        }

        protected override void InitialiseDefaults()
        {
            base.InitialiseDefaults();

            SetDefault(StorageConfig.CurrentTournament, default_tournament);
            SetDefault(StorageConfig.StageHeight, 900, 480, 2160);
            SetDefault(StorageConfig.SpectatorMinimumBuffer, 1000, 0, 10000);
            SetDefault(StorageConfig.SpectatorBuffer, 3000, 0, 30000);
            SetDefault(StorageConfig.SpectatorBufferTimeout, 20000, 1000, 120000);
        }
    }

    public enum StorageConfig
    {
        CurrentTournament,
        StageHeight,
        SpectatorMinimumBuffer,
        SpectatorBuffer,
        SpectatorBufferTimeout,
    }
}
