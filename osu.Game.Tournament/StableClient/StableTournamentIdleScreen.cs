// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Screens;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.StableClient.IPC;

namespace osu.Game.Tournament.StableClient
{
    public partial class StableTournamentIdleScreen : OsuScreen
    {
        [Resolved]
        private StableMatchIPCInfo stableIpc { get; set; } = null!;

        [Resolved]
        private LadderInfo ladderInfo { get; set; } = null!;

        private StableTournamentGrid grid = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            ladderInfo.PlayersPerTeam.BindValueChanged(_ => rebuildLayout(), true);
        }

        private void rebuildLayout()
        {
            int playersPerTeam = ladderInfo.PlayersPerTeam.Value;
            grid = new StableTournamentGrid(playersPerTeam);

            for (int i = 0; i < grid.SlotCount; i++)
            {
                var idlePlayer = new StableTournamentIdlePlayer(i, playersPerTeam);
                grid.GetSlot(i).Add(idlePlayer.With(p => p.RelativeSizeAxes = Axes.Both));
            }

            InternalChild = grid;
        }
    }
}
