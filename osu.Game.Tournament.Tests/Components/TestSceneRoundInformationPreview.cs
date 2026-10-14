// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Gameplay.Components;

namespace osu.Game.Tournament.Tests.Components
{
    public partial class TestSceneRoundInformationPreview : TournamentTestScene
    {
        [Test]
        public void TestRoundInformationPreview()
        {
            AddStep("Set BanPickFlowGroups", setBanPickFlowGroups);
            AddStep("Clear All", () =>
            {
                Clear();
                Ladder.CurrentMatch.Value!.PicksBans.Clear();
            });
            AddStep("Add Round Information preview", () => Add(new RefCountedBackbufferProvider
            {
                RelativeSizeAxes = Axes.Both,
                Child = new RoundInformationPreview
                {
                    Origin = Anchor.Centre,
                    Anchor = Anchor.Centre,
                }
            }));
            AddStep("Add a red pick", () => addChoice(TeamColour.Red, ChoiceType.Pick));
            AddStep("Add a blue pick", () => addChoice(TeamColour.Blue, ChoiceType.Pick));
            AddStep("Add a red protected", () => addChoice(TeamColour.Red, ChoiceType.Protected));
            AddStep("Add a blue protected", () => addChoice(TeamColour.Blue, ChoiceType.Protected));
            AddStep("Add a red ban", () => addChoice(TeamColour.Red, ChoiceType.Ban));
            AddStep("Add a blue ban", () => addChoice(TeamColour.Blue, ChoiceType.Ban));
        }

        private void addChoice(TeamColour colour, ChoiceType type)
        {
            Ladder.CurrentMatch.Value!.PicksBans.Add(new BeatmapChoice
            {
                BeatmapID = 1,
                Team = colour,
                Type = type,
            });
        }

        private void setBanPickFlowGroups()
        {
            var setBanPickFlowGroups = Ladder.CurrentMatch.Value!.Round.Value!.BanPickFlowGroups;
            setBanPickFlowGroups.Clear();
            setBanPickFlowGroups.AddRange(new[]
            {
                new BanPickFlowGroup
                {
                    Name = { Value = "protect" },
                    Steps =
                    {
                        new BanPickFlowStep
                        {
                            CurrentAction = { Value = ChoiceType.Protected }
                        },
                        new BanPickFlowStep
                        {
                            CurrentAction = { Value = ChoiceType.Protected },
                            SwapFromLastColor = { Value = true }
                        }
                    }
                },
                new BanPickFlowGroup
                {
                    Name = { Value = "ban" },
                    Steps =
                    {
                        new BanPickFlowStep
                        {
                            CurrentAction = { Value = ChoiceType.Ban }
                        },
                        new BanPickFlowStep
                        {
                            CurrentAction = { Value = ChoiceType.Ban },
                            SwapFromLastColor = { Value = true }
                        }
                    }
                },
                new BanPickFlowGroup
                {
                    Name = { Value = "pick" },
                    RepeatCount = { Value = 3 },
                    Steps =
                    {
                        new BanPickFlowStep
                        {
                            CurrentAction = { Value = ChoiceType.Pick },
                            SwapFromLastColor = { Value = true }
                        },
                        new BanPickFlowStep
                        {
                            CurrentAction = { Value = ChoiceType.Pick },
                            SwapFromLastColor = { Value = true }
                        }
                    }
                }
            });
        }
    }
}
