// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterfaceV2;
using osuTK;

namespace osu.Game.Tournament.Screens.Setup
{
    public partial class RoundBeatmapDownloadAction : LabelledDrawable<Drawable>
    {
        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private TournamentGameBase gameBase { get; set; } = null!;

        private Box progressBar = null!;
        private TournamentSpriteText text = null!;
        private RoundedButton forceDownloadButton = null!;
        private RoundedButton downloadButton = null!;

        public RoundBeatmapDownloadAction()
            : base(true)
        {
            Label = "下载谱面";
        }

        protected override Drawable CreateComponent() => new Container
        {
            AutoSizeAxes = Axes.Y,
            RelativeSizeAxes = Axes.X,
            Children = new Drawable[]
            {
                text = new TournamentSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    AutoSizeAxes = Axes.Both,
                    Spacing = new Vector2(10, 0),
                    Children = new Drawable[]
                    {
                        forceDownloadButton = new RoundedButton
                        {
                            Size = new Vector2(120, 40),
                            Text = "重下载所有谱面",
                            Action = () => downloadBeatmap(true)
                        },
                        downloadButton = new RoundedButton
                        {
                            Size = new Vector2(120, 40),
                            Text = "下载所有谱面",
                            Action = () => downloadBeatmap(false)
                        }
                    }
                }
            }
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();

            AddInternal(progressBar = new Box
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                RelativeSizeAxes = Axes.X,
                Height = 5f,
                Width = 0f,
                Colour = colours.Blue,
            });
        }

        private void downloadBeatmap(bool forceRedownload)
        {
            Task.Run(async () =>
            {
                await gameBase.DownloadAllRoundBeatmapOsuFile(forceRedownload, new Progress<TournamentGameBase.BeatmapDownloadProgress>(i => Scheduler.Add(() =>
                {
                    progressBar.Width = i.Ratio;

                    if (i.Completed == i.Total)
                    {
                        text.Text = $"谱面下载完成, {i.Failed}个下载失败";
                        return;
                    }

                    text.Text = $"正在下载{i.BeatmapId}, 已下载 {i.Completed} / {i.Total}, 失败 {i.Failed}";
                }))).ConfigureAwait(false);
            });
        }
    }
}
