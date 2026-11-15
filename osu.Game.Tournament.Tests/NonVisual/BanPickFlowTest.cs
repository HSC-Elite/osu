// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class BanPickFlowTest
    {
        [TestCase(3)]
        [TestCase(7)]
        [TestCase(9)]
        [TestCase(23)]
        public void TestStandardTemplate(int bestOf)
        {
            var round = createRound(bestOf, false, true);
            var slots = BanPickFlow.Resolve(round, Array.Empty<BeatmapChoice>(), TeamColour.Blue);
            Assert.That(slots.Count(s => s.Step.CurrentAction.Value == ChoiceType.Protected), Is.EqualTo(2));
            Assert.That(slots.Count(s => s.Step.CurrentAction.Value == ChoiceType.Ban), Is.EqualTo(2));
            var picks = slots.Where(s => s.Step.CurrentAction.Value == ChoiceType.Pick).ToArray();
            Assert.That(picks.Length, Is.EqualTo(bestOf - 1));
            Assert.That(picks.Select(s => s.Team), Is.EqualTo(Enumerable.Range(0, bestOf - 1).Select(i => i % 2 == 0 ? TeamColour.Blue : TeamColour.Red)));
            Assert.That(BanPickFlow.Validate(round), Is.Empty);
        }

        [Test]
        public void TestSplitSevenIntoFourAndThree()
        {
            var round = createRound(7, true, false);
            Assert.That(round.BanPickFlowGroups.Select(g => g.Steps.Count), Is.EqualTo(new[] { 2, 2, 4, 3 }));
            Assert.That(BanPickFlow.Validate(round), Is.Empty);
            var slots = BanPickFlow.Resolve(round, Array.Empty<BeatmapChoice>(), TeamColour.Red);
            Assert.That(slots[8].Group.Name.Value, Is.EqualTo("下半场"));
            Assert.That(slots[8].Team, Is.EqualTo(TeamColour.Red));
        }

        [Test]
        public void TestOddHalfContinuesAlternation()
        {
            var round = createRound(7, true, false, 3);
            var slots = BanPickFlow.Resolve(round, Array.Empty<BeatmapChoice>(), TeamColour.Red);
            Assert.That(slots[7].Group.Name.Value, Is.EqualTo("下半场"));
            Assert.That(slots[7].Team, Is.EqualTo(TeamColour.Blue));
        }

        [Test]
        public void TestLegacyRepeatAndRelativeTeams()
        {
            var round = new TournamentRound();
            var group = new BanPickFlowGroup { RepeatCount = { Value = 2 } };
            group.Steps.Add(new BanPickFlowStep { SwapFromLastColor = { Value = true } });
            round.BanPickFlowGroups.Add(group);
            var slots = BanPickFlow.Resolve(round, Array.Empty<BeatmapChoice>(), TeamColour.Red);
            Assert.That(slots.Select(s => s.Team), Is.EqualTo(new[] { TeamColour.Blue, TeamColour.Red, TeamColour.Blue }));
            var clone = group.DeepClone();
            Assert.That(clone.Steps.Count, Is.EqualTo(3));
            Assert.That(clone.RepeatCount.Value, Is.Zero);
            clone.Steps[0].CurrentAction.Value = ChoiceType.Ban;
            Assert.That(group.Steps[0].CurrentAction.Value, Is.EqualTo(ChoiceType.Pick));
            Assert.That(clone.Steps[1].CurrentAction.Value, Is.EqualTo(ChoiceType.Pick));
        }

        [Test]
        public void TestActualTeamDrivesRelativeNextStep()
        {
            var round = createRound(7, true, false);
            round.BanPickFlowGroups[0].Steps[1].TeamRule.Value = BanPickTeamRule.Opponent;
            var choice = new BeatmapChoice { Type = ChoiceType.Protected, Team = TeamColour.Blue, FlowStepIndex = 0 };
            var slots = BanPickFlow.Resolve(round, new[] { choice }, TeamColour.Red);
            Assert.That(slots[1].Team, Is.EqualTo(TeamColour.Red));
        }

        [Test]
        public void TestUndoDoesNotMoveSecondHalfIntoFirstHalf()
        {
            var round = createRound(7, true, false);
            var match = new TournamentMatch { Round = { Value = round } };
            var planned = BanPickFlow.Resolve(round, Array.Empty<BeatmapChoice>(), TeamColour.Red);
            foreach (var slot in planned)
                match.PicksBans.Add(new BeatmapChoice { Type = slot.Step.CurrentAction.Value, Team = slot.Team, FlowStepIndex = slot.Index });
            Assert.That(BanPickFlow.Next(match), Is.Null);
            var secondHalfChoice = match.PicksBans[8];
            match.PicksBans.RemoveAt(4);
            var resolved = BanPickFlow.Resolve(round, match.PicksBans, TeamColour.Red);
            Assert.That(resolved[4].Choice, Is.Null);
            Assert.That(resolved[8].Choice, Is.SameAs(secondHalfChoice));
            Assert.That(BanPickFlow.Next(match)!.Index, Is.EqualTo(4));
        }

        [Test]
        public void TestLegacyHistoryIsAssignedChronologically()
        {
            var round = createRound(7, true, false);
            var choices = Enumerable.Range(0, 5).Select(_ => new BeatmapChoice { Type = ChoiceType.Pick }).ToArray();
            var slots = BanPickFlow.Resolve(round, choices, TeamColour.Red);
            Assert.That(slots.Skip(4).Take(4).Select(s => s.Choice), Is.EqualTo(choices.Take(4)));
            Assert.That(slots[8].Choice, Is.SameAs(choices[4]));
        }

        [Test]
        public void TestEmptyAndCompletedFlow()
        {
            var round = new TournamentRound();
            round.BanPickFlowGroups.Add(new BanPickFlowGroup());
            var match = new TournamentMatch { Round = { Value = round } };
            Assert.That(BanPickFlow.Next(match), Is.Null);
            round.BanPickFlowGroups.AddRange(BanPickFlow.CreateTemplate(7));
            match.Completed.Value = true;
            Assert.That(BanPickFlow.Next(match), Is.Null);
            match.Completed.Value = false;
            Assert.That(BanPickFlow.Next(match), Is.Not.Null);
        }

        [Test]
        public void TestSerializationPreservesScheduleAndPositions()
        {
            var round = createRound(7, true, false);
            var match = new TournamentMatch { FirstPickTeam = { Value = TeamColour.Blue } };
            match.PicksBans.Add(new BeatmapChoice { Type = ChoiceType.Pick, FlowStepIndex = 8 });
            var copy = JsonConvert.DeserializeObject<TournamentRound>(JsonConvert.SerializeObject(round))!;
            var matchCopy = JsonConvert.DeserializeObject<TournamentMatch>(JsonConvert.SerializeObject(match))!;
            Assert.That(copy.SeparateTiebreaker.Value, Is.False);
            Assert.That(copy.BanPickFlowBestOf.Value, Is.EqualTo(7));
            Assert.That(copy.BanPickFlowGroups[3].Steps.Count, Is.EqualTo(3));
            Assert.That(matchCopy.FirstPickTeam.Value, Is.EqualTo(TeamColour.Blue));
            Assert.That(matchCopy.PicksBans[0].FlowStepIndex, Is.EqualTo(8));
            Assert.That(BanPickFlow.Resolve(copy, matchCopy.PicksBans, matchCopy.FirstPickTeam.Value)[8].Choice, Is.SameAs(matchCopy.PicksBans[0]));
        }

        [Test]
        public void TestOldJsonStillLoads()
        {
            const string json = "{\"Name\":\"old\",\"RepeatCount\":1,\"Steps\":[{\"CurrentAction\":\"Ban\",\"SwapFromLastColor\":true}]}";
            var group = JsonConvert.DeserializeObject<BanPickFlowGroup>(json)!;
            var round = new TournamentRound();
            round.BanPickFlowGroups.Add(group);
            Assert.That(BanPickFlow.Resolve(round, Array.Empty<BeatmapChoice>(), TeamColour.Red).Select(s => s.Team), Is.EqualTo(new[] { TeamColour.Blue, TeamColour.Red }));
        }

        [Test]
        public void TestNestedSubscriptionsAndDisposal()
        {
            var round = createRound(7, false, true);
            int notifications = 0;
            var subscription = new BanPickFlowSubscription(round, () => notifications++);
            round.BanPickFlowGroups[0].Name.Value = "renamed";
            round.BanPickFlowGroups[0].Steps.Add(new BanPickFlowStep());
            round.BanPickFlowGroups[0].Steps.Last().TeamRule.Value = BanPickTeamRule.Blue;
            Assert.That(notifications, Is.EqualTo(3));
            var removed = round.BanPickFlowGroups[0];
            round.BanPickFlowGroups.Remove(removed);
            removed.Name.Value = "removed";
            Assert.That(notifications, Is.EqualTo(4));
            subscription.Dispose();
            round.BestOf.Value = 9;
            Assert.That(notifications, Is.EqualTo(4));
        }

        [Test]
        public void TestChangedBestOfDoesNotOverwriteCustomizations()
        {
            var round = createRound(7, false, true);
            round.BanPickFlowGroups[2].Name.Value = "custom";
            round.BestOf.Value = 9;
            Assert.That(round.BanPickFlowGroups[2].Name.Value, Is.EqualTo("custom"));
            Assert.That(round.BanPickFlowGroups[2].Steps.Count, Is.EqualTo(6));
            Assert.That(BanPickFlow.Validate(round).Any(message => message.Contains("BO7")), Is.True);
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(8)]
        public void TestInvalidBestOf(int bestOf)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BanPickFlow.CreateTemplate(bestOf));
        }

        private static TournamentRound createRound(int bestOf, bool split, bool separateTiebreaker, int? firstHalfCount = null)
        {
            var round = new TournamentRound
            {
                BestOf = { Value = bestOf }, SeparateTiebreaker = { Value = separateTiebreaker }, BanPickFlowBestOf = { Value = bestOf }
            };
            round.BanPickFlowGroups.AddRange(BanPickFlow.CreateTemplate(bestOf));
            // Custom stage layouts are still editable; they are not a second built-in template.
            var picks = round.BanPickFlowGroups[2];
            if (!separateTiebreaker)
                picks.Steps.Add(new BanPickFlowStep { TeamRule = { Value = BanPickTeamRule.First } });
            if (split)
            {
                int count = firstHalfCount ?? (picks.Steps.Count + 1) / 2;
                var later = new BanPickFlowGroup { Name = { Value = "下半场" } };
                later.Steps.AddRange(picks.Steps.Skip(count).ToArray());
                while (picks.Steps.Count > count)
                    picks.Steps.RemoveAt(count);
                picks.Name.Value = "上半场";
                round.BanPickFlowGroups.Add(later);
            }
            return round;
        }
    }
}
