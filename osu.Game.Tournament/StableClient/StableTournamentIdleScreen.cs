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

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            ladderInfo.PlayersPerTeam.BindValueChanged(_ => updateLayout(), true);
        }

        private void updateLayout()
        {
            InternalChild = createGrid();
        }

        private StableTournamentGrid createGrid()
        {
            int playersPerTeam = ladderInfo.PlayersPerTeam.Value;
            var grid = new StableTournamentGrid(playersPerTeam);

            var match = stableIpc.CurrentMatch.Value;
            if (match == null) return grid;

            for (int i = 0; i < 16; i++)
            {
                int userId = match.SlotUserIds[i];
                if (userId <= 0) continue;

                // 使用我们新创建的、绑定了 StableIpc 的 IdlePlayer 组件
                var idlePlayer = new StableTournamentIdlePlayer(i, match.SlotTeams[i] == 1 ? TeamColour.Blue : TeamColour.Red);

                if (match.SlotTeams[i] == 1)
                    grid.AddBluePlayer(idlePlayer);
                else if (match.SlotTeams[i] == 2)
                    grid.AddRedPlayer(idlePlayer);
            }

            return grid;
        }
    }
}
