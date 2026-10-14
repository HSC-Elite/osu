// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;

namespace osu.Game.Tournament.Screens.Gameplay.Components
{
    public partial class TournamentMatchScoreProcessorDetail : CompositeDrawable
    {
        private readonly OsuSpriteText listeningText;
        private readonly OsuSpriteText stateText;
        private readonly OsuSpriteText waitingText;
        private readonly OsuSpriteText requestText;

        [Resolved(canBeNull: true)]
        private TournamentMatchScoreProcessor? scoreProcessor { get; set; }

        [Resolved]
        private MatchIPCInfo ipc { get; set; } = null!;

        public TournamentMatchScoreProcessorDetail()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Children = new Drawable[]
                {
                    listeningText = createText(),
                    stateText = createText(),
                    waitingText = createText(),
                    requestText = createText(),
                },
            };
        }

        private static OsuSpriteText createText() => new TournamentSpriteText
        {
            Font = OsuFont.Torus.With(size: 10),
        };

        protected override void Update()
        {
            base.Update();

            if (scoreProcessor == null)
            {
                listeningText.Text = "权威结果源: 分数处理器不可用";
                stateText.Text = $"客户端状态: {ipc.State.Value}";
                waitingText.Text = "等待结算结果: 不可用";
                requestText.Text = "最近结果请求: 不可用";
                return;
            }

            listeningText.Text = $"权威结果源: {(ipc.HasActiveMatch.Value ? "可用" : "未连接")}";
            stateText.Text = $"客户端状态: {ipc.State.Value}";
            waitingText.Text = $"等待结算结果: {(scoreProcessor.WaitingForAuthoritativeResult.Value ? "是" : "否")}";
            requestText.Text = $"最近结果请求: {scoreProcessor.LastResultRequestStatus.Value}";
        }
    }
}
