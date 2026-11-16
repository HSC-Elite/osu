// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Gameplay.Components;
using osu.Game.Tournament.Screens.Gameplay.Components.RoundInformation;

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
            AddUntilStep("late protects displayed", () => this.ChildrenOfType<ProtectMapBox>().Count() == 2);
            AddUntilStep("late bans displayed", () => this.ChildrenOfType<BanMapBox>().Count() == 2);
            AddAssert("picks still displayed", () => this.ChildrenOfType<PickMapBox>().Count(), () => Is.EqualTo(2));
        }

        [Test]
        public void TestLiveStageChanges()
        {
            TournamentRound round = null!;
            RoundInformationPreview preview = null!;
            AddStep("create custom BO7 stages", () =>
            {
                Clear();
                round = new TournamentRound { BestOf = { Value = 7 }, SeparateTiebreaker = { Value = false } };
                round.BanPickFlowGroups.AddRange(BanPickFlow.CreateTemplate(7));
                var picks = round.BanPickFlowGroups[2];
                picks.Name.Value = "自定义前置阶段";
                var later = new BanPickFlowGroup { Name = { Value = "自定义后续阶段" } };
                later.Steps.AddRange(picks.Steps.Skip(4).Select(s => s.DeepClone()));
                later.Steps.Add(new BanPickFlowStep { TeamRule = { Value = BanPickTeamRule.First } });
                while (picks.Steps.Count > 4)
                    picks.Steps.RemoveAt(4);
                round.BanPickFlowGroups.Add(later);
                Ladder.CurrentMatch.Value!.Round.Value = round;
                Ladder.CurrentMatch.Value.PicksBans.Clear();
                Add(new RefCountedBackbufferProvider
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = preview = new RoundInformationPreview()
                });
            });
            AddUntilStep("configured slots displayed", () => preview.ChildrenOfType<UnselectMapBox>().Count() == 11);
            AddAssert("original height", () => preview.Height, () => Is.EqualTo(110));
            AddAssert("editor renderer not used for broadcast", () => !preview.ChildrenOfType<BanPickStageDisplay>().Any());
            AddStep("rename lower half", () => round.BanPickFlowGroups[3].Name.Value = "第二阶段");
            AddAssert("stage names do not add broadcast headings", () => !preview.ChildrenOfType<TournamentSpriteText>().Any(t => t.Text == "第二阶段"));
            AddStep("change first team", () => Ladder.CurrentMatch.Value!.FirstPickTeam.Value = TeamColour.Blue);
            AddAssert("pending slots keep original appearance", () => !preview.ChildrenOfType<TournamentSpriteText>().Any(t => t.Text == "蓝队" || t.Text == "红队"));
            AddStep("add a pick", () => round.BanPickFlowGroups[3].Steps.Add(new BanPickFlowStep { TeamRule = { Value = BanPickTeamRule.First } }));
            AddUntilStep("new slot displayed", () => preview.ChildrenOfType<UnselectMapBox>().Count() == 12);
            AddStep("switch round without switching match", () =>
            {
                var replacement = new TournamentRound();
                replacement.BanPickFlowGroups.AddRange(BanPickFlow.CreateTemplate(9));
                Ladder.CurrentMatch.Value!.Round.Value = replacement;
            });
            AddUntilStep("replacement shown", () => preview.ChildrenOfType<UnselectMapBox>().Count() == 12);
            AddStep("clear flow", () => Ladder.CurrentMatch.Value!.Round.Value!.BanPickFlowGroups.Clear());
            AddUntilStep("stages cleared", () => !preview.ChildrenOfType<MapBox>().Any());
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
