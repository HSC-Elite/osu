// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
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
        private readonly OsuSpriteText matchText;
        private readonly OsuSpriteText gameText;
        private readonly OsuSpriteText eventText;
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
                    matchText = createText(),
                    gameText = createText(),
                    eventText = createText(),
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
                listeningText.Text = "API 监听: 分数处理器不可用";
                stateText.Text = $"客户端状态: {ipc.State.Value}";
                waitingText.Text = "等待结算 API: 不可用";
                matchText.Text = "Match ID: 无";
                gameText.Text = "当前 Game ID: 无";
                eventText.Text = "最新 Event ID: 0";
                requestText.Text = "最近 API 请求: 不可用";
                return;
            }

            listeningText.Text = $"API 监听: {(scoreProcessor.CurrentlyListening.Value ? "监听中" : "未监听")}";
            stateText.Text = $"客户端状态: {ipc.State.Value}";
            waitingText.Text = $"等待结算 API: {(scoreProcessor.WaitingForAuthoritativeResult.Value ? "是" : "否")}";
            matchText.Text = $"Match ID: {(scoreProcessor.CurrentMatchID > 0 ? scoreProcessor.CurrentMatchID.ToString() : "无")}";
            gameText.Text = $"当前 Game ID: {(scoreProcessor.CurrentApiGameID > 0 ? scoreProcessor.CurrentApiGameID.ToString() : "无")}";
            eventText.Text = $"最新 Event ID: {scoreProcessor.LatestMatchEventID}";
            requestText.Text = $"最近 API 请求: {scoreProcessor.LastAPIRequestStatus.Value}";
        }
    }
}
