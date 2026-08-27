// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Configuration;
using osu.Framework.Platform;

namespace osu.Game.Tournament.Configuration
{
    public class TournamentConfigManager : IniConfigManager<TournamentConfig>
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

            SetDefault(TournamentConfig.CurrentTournament, default_tournament);
            SetDefault(TournamentConfig.CaptureFrameRate, 60, 30, 360);
            SetDefault(TournamentConfig.UseExternalStageDisplay, false);
        }
    }

    public enum TournamentConfig
    {
        CurrentTournament,
        CaptureFrameRate,
        UseExternalStageDisplay,
    }
}
