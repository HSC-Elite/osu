// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Gameplay.Components.RoundInformation;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tournament.Screens.Gameplay.Components
{
    public partial class MultCoinRoundInformationPreview : RoundInformationPreview
    {

        protected override void UpdateContent()
        {
            if (LadderInfo.CurrentMatch.Value?.Round.Value == null)
                return;

            var banMapDetail = new MapDetailContent("禁图");
            BeatmapChoice?[] banChoices = LadderInfo.CurrentMatch.Value.PicksBans.Where(b => b.Type == ChoiceType.Ban).ToArray();
            var remainChoices = LadderInfo.CurrentMatch.Value.PicksBans.Except(banChoices);
            banChoices = banChoices.Concat(Enumerable.Repeat((BeatmapChoice?)null, (LadderInfo.CurrentMatch.Value.Round.Value?.BanCount.Value ?? 2) * 2 - banChoices.Length)).ToArray();

            var firstHalfPickDetail = new MapDetailContent("上半场");
            var secondHalfPickDetail = new MapDetailContent("下半场");

            int? bestOf = LadderInfo.CurrentMatch.Value.Round.Value?.BestOf.Value - 1;

            int firstHalfMapCount = (bestOf / 2 % 2 == 0 ? bestOf / 2 : bestOf / 2 + 1) ?? 99;
            int secondHalfMapCount = bestOf - firstHalfMapCount ?? 0;

            var firstHalfPickChoice = remainChoices.Take(firstHalfMapCount).Concat(Enumerable.Repeat((BeatmapChoice?)null, firstHalfMapCount - remainChoices.Take(firstHalfMapCount).Count()));
            var secondHalfPickChoice = remainChoices.Skip(firstHalfMapCount).Take(secondHalfMapCount)
                                                    .Concat(Enumerable.Repeat((BeatmapChoice?)null, secondHalfMapCount - remainChoices.Skip(firstHalfMapCount).Take(secondHalfMapCount).Count()));

            MapContentContainer.Add(banMapDetail);
            MapContentContainer.Add(createDivideLine());
            MapContentContainer.Add(firstHalfPickDetail);
            MapContentContainer.Add(createDivideLine());
            MapContentContainer.Add(secondHalfPickDetail);
            MapContentContainer.Add(createDivideLine());

            var TBMap = LadderInfo.CurrentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(map => map.Mods == "TB");

            if (TBMap != null)
            {
                MapContentContainer.Add(new TbMapBox(TBMap));
            }

            Scheduler.Add(() =>
            {
                banMapDetail.UpdateBeatmap(banChoices);
                firstHalfPickDetail.UpdateBeatmap(firstHalfPickChoice);
                secondHalfPickDetail.UpdateBeatmap(secondHalfPickChoice);
            });
        }

        private Box createDivideLine() => new Box
        {
            Colour = Color4Extensions.FromHex("#E5E5E5"),
            Height = 38.5f,
            Width = 1f,
            Margin = new MarginPadding
            {
                Top = 37f
            }
        };

        private MapBox createTBMapBox(bool isSelected)
        {
            MapBox mapbox = new MapBox();

            mapbox.Margin = new MarginPadding
            {
                Vertical = 36f,
                Horizontal = 16f
            };
            mapbox.CenterLine.Colour = isSelected ? new Color4(197, 60, 100, 255) : Color4.Gray;
            mapbox.TopMapContainer.Add(new TournamentSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "决胜局",
                Colour = Color4Extensions.FromHex("#E5E5E5"),
            });

            var TBMap = LadderInfo.CurrentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(map => map.Mods == "TB");

            if (TBMap == null)
                return mapbox;

            mapbox.BottomMapContainer.Add(new TournamentModIcon("TB")
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                FillMode = FillMode.Fill
            });

            return mapbox;
        }

        private static Drawable createMapBoxContent(string mapName, Color4 backgroundColor, Color4 textColor)
        {
            return new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = backgroundColor,
                    },
                    new TournamentSpriteText
                    {
                        Text = mapName,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = textColor,
                        Font = OsuFont.Torus.With(size: 20),
                        Shadow = true,
                    }
                }
            };
        }

        private partial class MapDetailContent : CompositeDrawable
        {
            private FillFlowContainer banMapContent = null!;

            [Resolved]
            private LadderInfo ladderInfo { get; set; } = null!;

            private readonly string headerName;

            public MapDetailContent(string headerName)
            {
                this.headerName = headerName;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                AutoSizeAxes = Axes.Both;
                Margin = new MarginPadding(16);

                InternalChild = new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        createHeaderSection(headerName),
                        banMapContent = new FillFlowContainer
                        {
                            Spacing = new Vector2(20),
                            Direction = FillDirection.Horizontal,
                            AutoSizeAxes = Axes.Both,
                        }
                    }
                };
            }

            public void UpdateBeatmap(IEnumerable<BeatmapChoice?> maps)
            {
                TournamentRound? round = ladderInfo.CurrentMatch.Value?.Round.Value;

                if (round == null)
                    return;

                banMapContent.Clear();

                banMapContent.ChildrenEnumerable = maps.Select(map =>
                {
                    var mapBox = new MapBox();

                    if (map == null)
                        return mapBox;

                    var roundBeatmap = round.Beatmaps.FirstOrDefault(roundMap => roundMap.ID == map.BeatmapID);
                    if (roundBeatmap == null)
                        return mapBox;

                    mapBox.CenterLine.Colour = map.Team == TeamColour.Red
                        ? new Color4(212, 48, 48, 255)
                        : new Color4(42, 130, 228, 255);

                    var modColor = ladderInfo.ModColors.FirstOrDefault(m => m.ModName == roundBeatmap.Mods);

                    Color4 backgroundColor = map.Type == ChoiceType.Ban ? Color4.Gray : modColor?.BackgroundColor ?? Color4.Gray;
                    Color4 textColor = map.Type == ChoiceType.Ban ? new Color4(229, 229, 229, 255) : modColor?.TextColor ?? new Color4(229, 229, 229, 255);

                    var modArray = round.Beatmaps.Where(b => b.Mods == roundBeatmap.Mods).ToArray();

                    int id = Array.FindIndex(modArray, b => b.ID == roundBeatmap.ID) + 1;

                    var mapBoxContent = createMapBoxContent($"{roundBeatmap.Mods}{id}", backgroundColor, textColor);

                    if (map.Team == TeamColour.Red)
                    {
                        mapBox.BottomMapContainer.Add(mapBoxContent);
                    }
                    else
                    {
                        mapBox.TopMapContainer.Add(mapBoxContent);
                    }

                    return mapBox;
                });
            }

            private static Drawable createHeaderSection(string text)
            {
                return new GridContainer
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 20f,
                    ColumnDimensions = new Dimension[]
                    {
                        new Dimension(GridSizeMode.Distributed),
                        new Dimension(GridSizeMode.AutoSize),
                        new Dimension(GridSizeMode.Distributed)
                    },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            // 左边的线条
                            new Box
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 1,
                                Colour = Color4Extensions.FromHex("#E5E5E5"),
                            },
                            // 文字
                            new Container
                            {
                                AutoSizeAxes = Axes.X,
                                RelativeSizeAxes = Axes.Y,
                                Anchor = Anchor.TopLeft,
                                Origin = Anchor.CentreLeft,
                                Margin = new MarginPadding { Horizontal = 10f },
                                Child = new TournamentSpriteText
                                {
                                    Text = text,
                                    Font = OsuFont.Torus.With(size: 20),
                                    Colour = Color4Extensions.FromHex("#E5E5E5"),
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                },
                            },
                            // 右边的线条
                            new Box
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 1,
                                Colour = Color4Extensions.FromHex("#E5E5E5"),
                            }
                        }
                    }
                };
            }
        }

        private partial class MapBox : CompositeDrawable
        {
            public MapBox()
            {
                Height = 50;
                Width = 42;

                InternalChildren = new Drawable[]
                {
                    TopMapContainer = new Container
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Height = 18,
                        RelativeSizeAxes = Axes.X,
                    },
                    CenterLine = new Box
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Height = 3.6f,
                        RelativeSizeAxes = Axes.X,
                        Colour = Color4.Gray,
                    },
                    BottomMapContainer = new Container
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Height = 18,
                        RelativeSizeAxes = Axes.X,
                    },
                };
            }

            public Container TopMapContainer { get; }

            public Container BottomMapContainer { get; }

            public Box CenterLine { get; }
        }
    }
}
