// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Runtime.InteropServices;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Shaders.Types;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Utils;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.IPC.MemoryIPC;
using osu.Game.Tournament.Models;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tournament.Screens.Gameplay.Components
{
    public partial class TeamMultCoin : CompositeDrawable
    {
        private readonly TeamColour colour;
        private readonly bool flip;
        private Box multCoinBar = null!;
        private DiffBar diffBar = null!;
        private RollingMultCoinContainer multCounter = null!;
        private FillFlowContainer barContainer = null!;
        private RollingMultDiffNumberContainer diffCounter = null!;

        private const float bar_width_when_1000_coin = 230f;
        private const float bar_steepness = 0.6f;

        // 当前队伍的分数
        private readonly BindableLong ourScore = new BindableLong();

        // 对方队伍的分数
        private readonly BindableLong oppoScore = new BindableLong();

        [Resolved]
        private MatchIPCInfo matchIpc { get; set; } = null!;

        private MemoryBasedIPCWithMatchListener ipc => (matchIpc as MemoryBasedIPCWithMatchListener)!;

        [Resolved]
        private LadderInfo ladderInfo { get; set; } = null!;

        public bool AllowAnimation { get; set; } = true;

        private bool isTB
        {
            get
            {
                var lastPick = currentMatch.Value?.PicksBans.LastOrDefault();
                var tbMap = currentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(map => map.Mods == "TB");

                if (lastPick == null || tbMap == null)
                    return false;

                return lastPick.Type == ChoiceType.Pick && lastPick.BeatmapID == tbMap.ID;
            }
        }

        public TeamMultCoin(TeamColour colour)
        {
            this.colour = colour;
            flip = colour == TeamColour.Blue;

            coin.BindValueChanged(d =>
            {
                triggerAnimationWhenMatchFinished(d.OldValue ?? 0, d.NewValue ?? 0);
            }, true);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var anchor = flip ? Anchor.BottomLeft : Anchor.BottomRight;

            Height = 22.5f;
            AutoSizeAxes = Axes.X;

            InternalChildren = new Drawable[]
            {
                new FillFlowContainer
                {
                    Height = 7f,
                    AutoSizeAxes = Axes.X,
                    Direction = FillDirection.Horizontal,
                    Anchor = anchor,
                    Origin = anchor,
                    Spacing = new Vector2(5, 0),
                    Children = new Drawable[]
                    {
                        barContainer = new FillFlowContainer
                        {
                            Name = "barContainer",
                            RelativeSizeAxes = Axes.Y,
                            AutoSizeAxes = Axes.X,
                            Anchor = anchor,
                            Origin = anchor,
                            Spacing = new Vector2(-4.2f, 0),
                            Children = new Drawable[]
                            {
                                multCoinBar = new Box
                                {
                                    RelativeSizeAxes = Axes.Y,
                                    Anchor = anchor,
                                    Origin = anchor,
                                    Shear = new Vector2((flip ? 1 : -1) * bar_steepness, 0),
                                    Colour = TournamentGame.GetTeamColour(colour),
                                },
                                diffBar = new DiffBar(colour)
                                {
                                    RelativeSizeAxes = Axes.Y,
                                    Anchor = anchor,
                                    Origin = anchor,
                                    Shear = new Vector2((flip ? 1 : -1) * bar_steepness, 0),
                                }
                            }
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Y,
                            AutoSizeAxes = Axes.X,
                            Anchor = anchor,
                            Origin = anchor,
                            Children = new Drawable[]
                            {
                                new FillFlowContainer
                                {
                                    Anchor = flip ? Anchor.TopLeft : Anchor.TopRight,
                                    Origin = flip ? Anchor.TopLeft : Anchor.TopRight,
                                    AutoSizeAxes = Axes.Both,
                                    Y = -2,
                                    Direction = FillDirection.Horizontal,
                                    Children = new Drawable[]
                                    {
                                        multCounter = new RollingMultCoinContainer
                                        {
                                            Anchor = flip ? Anchor.CentreLeft : Anchor.CentreRight,
                                            Origin = flip ? Anchor.CentreLeft : Anchor.CentreRight,
                                            Margin = new MarginPadding { Horizontal = 3f },
                                        },
                                        diffCounter = new RollingMultDiffNumberContainer
                                        {
                                            Anchor = flip ? Anchor.CentreLeft : Anchor.CentreRight,
                                            Origin = flip ? Anchor.CentreLeft : Anchor.CentreRight,
                                            Colour = TournamentGame.GetTeamColour(colour).Lighten(0.5f),
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            if (colour == TeamColour.Red)
            {
                ourScore.BindTo(ipc.Score1);
                oppoScore.BindTo(ipc.Score2);
            }
            else
            {
                ourScore.BindTo(ipc.Score2);
                oppoScore.BindTo(ipc.Score1);
            }

            ipc.MatchAborted += () => triggerAnimationWhenMatchFinished(coin.Value ?? 0, coin.Value ?? 0);
            //ipc.MatchFinished += _ => triggerAnimationWhenMatchFinished(coin.Value ?? 0, coin.Value ?? 0);

            /*Scheduler.AddDelayed(() =>
            {
                var leftColor = getRandomColour();
                var rightColor = getRandomColour();

                diffBar.Box.FadeColour(ColourInfo.GradientHorizontal(leftColor, rightColor), 1000);
            }, 1000, true);*/

            diffBar.Box.Colour = Color4.Yellow;
        }

        private Color4 getRandomColour()
        {
            return colour == TeamColour.Red
                ? Color4Extensions.FromHSV(RNG.NextSingle(346, 438) % 360, 1, 1)
                : Color4Extensions.FromHSV(RNG.NextSingle(174, 246), 1, 1);
        }

        #region match update

        private readonly Bindable<double?> coin = new Bindable<double?>();
        private readonly Bindable<double?> oppoCoin = new Bindable<double?>();
        private readonly Bindable<TournamentMatch?> currentMatch = new Bindable<TournamentMatch?>();
        private readonly Bindable<TournamentTeam?> currentTeam = new Bindable<TournamentTeam?>();

        private void matchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            coin.UnbindBindings();
            oppoCoin.UnbindBindings();
            currentTeam.UnbindBindings();

            Scheduler.AddOnce(updateMatch);
        }

        private void updateMatch()
        {
            var match = currentMatch.Value;

            if (match != null)
            {
                coin.BindTo(colour == TeamColour.Red ? match.Team1Coin : match.Team2Coin);
                oppoCoin.BindTo(colour == TeamColour.Blue ? match.Team1Coin : match.Team2Coin);
                currentTeam.BindTo(colour == TeamColour.Red ? match.Team1 : match.Team2);
            }
        }

        #endregion

        protected override void LoadComplete()
        {
            base.LoadComplete();

            currentMatch.BindValueChanged(matchChanged);
            currentMatch.BindTo(ladderInfo.CurrentMatch);
        }

        protected override void Update()
        {
            base.Update();

            if (ipc.State.Value == TourneyState.Playing)
            {
                updateDiff(true);
                diffBar.Progress = (float)Math.Clamp(ipc.PlayTime / ipc.Beatmap.Value?.Length ?? 1, 0, 1);
            }
        }

        private void updateScore(bool animate, double? score = null)
        {
            score ??= coin.Value ?? 0;

            if (animate && AllowAnimation)
            {
                multCounter.Current.Value = score.Value;
                multCoinBar.ResizeWidthTo(calculateBarWidth(score.Value), 400, Easing.OutQuint);
                return;
            }

            multCounter.DisplayedCount = score.Value;
            multCounter.Current.Value = score.Value;
            multCoinBar.ResizeWidthTo(calculateBarWidth(score.Value));
        }

        private void updateDiff(bool animate, double? diff = null)
        {
            diff ??= calculateDiffFromIpc();

            if (diff == 0)
            {
                diffCounter.FadeOut(100);
            }
            else
            {
                diffCounter.FadeIn(100);
            }

            if (animate && AllowAnimation)
            {
                diffCounter.Current.Value = diff.Value;
                diffBar.ResizeWidthTo(calculateBarWidth(diff.Value), 400, Easing.OutQuint);

                return;
            }

            diffCounter.DisplayedCount = diff.Value;
            diffCounter.Current.Value = diff.Value;
            diffBar.ResizeWidthTo(calculateBarWidth(diff.Value));
        }

        private static float calculateBarWidth(double coin) => (float)coin / 1000 * bar_width_when_1000_coin;

        private double calculateDiffFromIpc()
        {
            if (ourScore.Value > oppoScore.Value)
            {
                return TournamentGame.WINNER_BONUS + (isTB ? TournamentGame.EXTRA_WINNER_BONUS_TB : 0);
            }

            double diff =
                Math.Min(Math.Round((double)ourScore.Value / oppoScore.Value * 100, 2, MidpointRounding.AwayFromZero),
                    TournamentGame.LOSS_MAX_OBTAINABLE);

            return double.IsNaN(diff) ? 0 : diff;
        }

        // TODO: 动画效果是否需要FinishTransform
        private void triggerAnimationWhenMatchFinished(double oldAmount, double newAmount) => Scheduler.AddOnce(() =>
        {
            diffBar.Progress = 1;
            double diff = newAmount - oldAmount;

            updateScore(false, oldAmount);
            updateDiff(true, diff);

            using (BeginDelayedSequence(2000))
            {
                updateScore(true);
                updateDiff(true, 0);
            }
        });

        private partial class RollingMultDiffNumberContainer : RollingCounter<double>
        {
            protected override double RollingDuration => 1000;

            protected override Easing RollingEasing => Easing.Out;

            protected override LocalisableString FormatCount(double count)
            {
                char sign = Math.Sign(count) == -1 ? '-' : '+';

                return $"{sign}${Math.Abs(count):N2}";
            }

            protected override OsuSpriteText CreateSpriteText() => base.CreateSpriteText().With(t =>
            {
                t.Font = OsuFont.Torus.With(size: 19);
            });
        }

        private partial class DiffBar : CompositeDrawable
        {
            public Box Box { get; }

            public float Progress
            {
                get => Box.Width;
                set => Box.Width = value;
            }

            public ColourInfo BoxColor
            {
                get => Box.Colour;
                set => Box.Colour = value;
            }

            public DiffBar(TeamColour team)
            {
                InternalChild = new Container
                {
                    Masking = true,
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        Box = new Box
                        {
                            Anchor = team == TeamColour.Red ? Anchor.CentreRight : Anchor.CentreLeft,
                            Origin = team == TeamColour.Red ? Anchor.CentreRight : Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.Both,
                        },
                    }
                };

                AddInternal(new AnalyticParallelogramBorder
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Yellow,
                    BorderThickness = 0.75f,
                });
            }
        }

        private partial class AnalyticParallelogramBorder : Drawable
        {
            private const float anti_aliasing_margin = 2f;

            private IShader shader = null!;
            private float borderThickness = 1.5f;

            public float BorderThickness
            {
                get => borderThickness;
                set
                {
                    if (!float.IsFinite(value))
                        throw new ArgumentException($"{nameof(BorderThickness)} must be finite, but is {value}.");

                    value = Math.Max(0, value);

                    if (borderThickness == value)
                        return;

                    borderThickness = value;
                    Invalidate(Invalidation.DrawNode);
                }
            }

            [BackgroundDependencyLoader]
            private void load(ShaderManager shaders)
            {
                shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, "TournamentParallelogramBorder");
            }

            protected override DrawNode CreateDrawNode() => new AnalyticParallelogramBorderDrawNode(this);

            private class AnalyticParallelogramBorderDrawNode : DrawNode
            {
                protected new AnalyticParallelogramBorder Source => (AnalyticParallelogramBorder)base.Source;

                private Quad screenSpaceDrawQuad;
                private Vector4 drawRectangle;
                private IShader shader = null!;
                private float borderThickness;
                private IVertexBatch<TexturedVertex2D>? quadBatch;
                private IUniformBuffer<AnalyticParallelogramBorderParameters>? parametersBuffer;

                public AnalyticParallelogramBorderDrawNode(AnalyticParallelogramBorder source)
                    : base(source)
                {
                }

                public override void ApplyState()
                {
                    base.ApplyState();

                    float left = Math.Min(0, Source.DrawWidth);
                    float right = Math.Max(0, Source.DrawWidth);
                    float width = right - left;

                    screenSpaceDrawQuad = Source.ToScreenSpace(new RectangleF(
                        left - anti_aliasing_margin,
                        -anti_aliasing_margin,
                        width + anti_aliasing_margin * 2,
                        Source.DrawHeight + anti_aliasing_margin * 2));
                    drawRectangle = new Vector4(left, 0, right, Source.DrawHeight);
                    shader = Source.shader;
                    borderThickness = Source.borderThickness;
                }

                protected override void Draw(IRenderer renderer)
                {
                    base.Draw(renderer);

                    if (drawRectangle.Z <= drawRectangle.X || drawRectangle.W <= drawRectangle.Y)
                        return;

                    if (!renderer.BindTexture(renderer.WhitePixel))
                        return;

                    parametersBuffer ??= renderer.CreateUniformBuffer<AnalyticParallelogramBorderParameters>();
                    parametersBuffer.Data = new AnalyticParallelogramBorderParameters
                    {
                        BorderThickness = borderThickness,
                    };

                    shader.Bind();
                    shader.BindUniformBlock("m_TournamentParallelogramBorderParameters", parametersBuffer);

                    quadBatch ??= renderer.CreateQuadBatch<TexturedVertex2D>(1, 2);

                    var vertexAction = quadBatch.AddAction;

                    vertexAction(new TexturedVertex2D(renderer)
                    {
                        Position = screenSpaceDrawQuad.BottomLeft,
                        TexturePosition = new Vector2(drawRectangle.X - anti_aliasing_margin, drawRectangle.W + anti_aliasing_margin),
                        TextureRect = drawRectangle,
                        Colour = DrawColourInfo.Colour.BottomLeft.SRGB,
                    });
                    vertexAction(new TexturedVertex2D(renderer)
                    {
                        Position = screenSpaceDrawQuad.BottomRight,
                        TexturePosition = new Vector2(drawRectangle.Z + anti_aliasing_margin, drawRectangle.W + anti_aliasing_margin),
                        TextureRect = drawRectangle,
                        Colour = DrawColourInfo.Colour.BottomRight.SRGB,
                    });
                    vertexAction(new TexturedVertex2D(renderer)
                    {
                        Position = screenSpaceDrawQuad.TopRight,
                        TexturePosition = new Vector2(drawRectangle.Z + anti_aliasing_margin, -anti_aliasing_margin),
                        TextureRect = drawRectangle,
                        Colour = DrawColourInfo.Colour.TopRight.SRGB,
                    });
                    vertexAction(new TexturedVertex2D(renderer)
                    {
                        Position = screenSpaceDrawQuad.TopLeft,
                        TexturePosition = new Vector2(drawRectangle.X - anti_aliasing_margin, -anti_aliasing_margin),
                        TextureRect = drawRectangle,
                        Colour = DrawColourInfo.Colour.TopLeft.SRGB,
                    });

                    shader.Unbind();
                }

                protected override void Dispose(bool isDisposing)
                {
                    base.Dispose(isDisposing);
                    quadBatch?.Dispose();
                    parametersBuffer?.Dispose();
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                private record struct AnalyticParallelogramBorderParameters
                {
                    public UniformFloat BorderThickness;
                    private UniformPadding12 padding;
                }
            }
        }

        private partial class RollingMultCoinContainer : RollingCounter<double>
        {
            protected override double RollingDuration => 1000;

            protected override Easing RollingEasing => Easing.Out;

            protected override OsuSpriteText CreateSpriteText() => base.CreateSpriteText()
                                                                       .With(s => s.Font = OsuFont.Torus.With(size: 24));

            protected override LocalisableString FormatCount(double count) => $"${count:N2}";
        }

        private partial class MultCoinTextContainer : FillFlowContainer, IHasText
        {
            public LocalisableString Text
            {
                get => wholePart.Text;
                set
                {
                    string[] split = value.ToString().Split(".");

                    wholePart.Text = split[0] + ".";
                    fractionPart.Text = split[1];
                }
            }

            private readonly SpriteText wholePart;
            private readonly SpriteText fractionPart;

            public MultCoinTextContainer()
            {
                AutoSizeAxes = Axes.X;
                RelativeSizeAxes = Axes.Y;

                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Font = OsuFont.Torus.With(size: 27),
                        Text = "$"
                    },
                    wholePart = new OsuSpriteText
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Font = OsuFont.Torus.With(size: 25),
                    },
                    fractionPart = new OsuSpriteText
                    {
                        Margin = new MarginPadding { Bottom = 1f },
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Font = OsuFont.Torus.With(size: 20),
                    }
                };
            }
        }
    }
}
