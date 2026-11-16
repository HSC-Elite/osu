// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Tournament.Models
{
    /// <summary>
    /// Resolves the same finite, stage-based schedule for the editor, broadcast and map picker.
    /// </summary>
    public static class BanPickFlow
    {
        public static TeamColour Opposite(TeamColour team) => team == TeamColour.Red ? TeamColour.Blue : TeamColour.Red;

        public static IReadOnlyList<BanPickFlowSlot> Resolve(TournamentRound round, IEnumerable<BeatmapChoice> choices, TeamColour firstTeam)
        {
            var slots = new List<BanPickFlowSlot>();

            foreach (var group in round.BanPickFlowGroups)
            {
                for (int i = 0; i < group.TotalStep; i++)
                    slots.Add(new BanPickFlowSlot(slots.Count, group, group.Steps[i % group.Steps.Count]));
            }

            var recordedChoices = choices.ToArray();

            // Reserve explicit positions first, regardless of where they occur in the history.
            // Invalid or duplicate positions never fall back to a different stage.
            foreach (var choice in recordedChoices.Where(c => c.FlowStepIndex.HasValue))
            {
                var slot = slots.ElementAtOrDefault(choice.FlowStepIndex!.Value);
                if (slot != null && slot.Choice == null && slot.Step.CurrentAction.Value == choice.Type)
                    slot.Choice = choice;
            }

            // Legacy choices may be entered out of flow order. Fill the earliest matching vacancy,
            // preserving the recorded order within each action type.
            foreach (var choice in recordedChoices.Where(c => !c.FlowStepIndex.HasValue))
            {
                var slot = slots.FirstOrDefault(s => s.Choice == null && s.Step.CurrentAction.Value == choice.Type);
                if (slot != null)
                    slot.Choice = choice;
            }

            TeamColour previous = TeamColour.Red; // Legacy files used a virtual red previous team.

            foreach (var slot in slots)
            {
                slot.Team = slot.Step.TeamRule.Value switch
                {
                    BanPickTeamRule.First => firstTeam,
                    BanPickTeamRule.Second => Opposite(firstTeam),
                    BanPickTeamRule.Red => TeamColour.Red,
                    BanPickTeamRule.Blue => TeamColour.Blue,
                    BanPickTeamRule.Previous => previous,
                    BanPickTeamRule.Opponent => Opposite(previous),
                    _ => slot.Step.SwapFromLastColor.Value ? Opposite(previous) : previous
                };
                previous = slot.Choice?.Team ?? slot.Team;
            }

            return slots;
        }

        public static BanPickFlowSlot? Next(TournamentMatch match)
        {
            if (match.Round.Value == null || match.Completed.Value)
                return null;

            return Resolve(match.Round.Value, match.PicksBans, match.FirstPickTeam.Value).FirstOrDefault(s => s.Choice == null);
        }

        /// <summary>Creates the ordinary tournament schedule: one protect and ban per team, then alternating picks and a separate TB.</summary>
        public static IReadOnlyList<BanPickFlowGroup> CreateTemplate(int bestOf)
        {
            if (bestOf < 3 || bestOf % 2 == 0)
                throw new ArgumentOutOfRangeException(nameof(bestOf));

            return new[]
            {
                createStage("保图", ChoiceType.Protected, 2),
                createStage("禁图", ChoiceType.Ban, 2),
                createStage("选图", ChoiceType.Pick, bestOf - 1)
            };
        }

        private static BanPickFlowGroup createStage(string name, ChoiceType action, int count)
        {
            var group = new BanPickFlowGroup { Name = { Value = name } };
            for (int i = 0; i < count; i++)
            {
                group.Steps.Add(new BanPickFlowStep
                {
                    CurrentAction = { Value = action },
                    TeamRule = { Value = i % 2 == 0 ? BanPickTeamRule.First : BanPickTeamRule.Second }
                });
            }
            return group;
        }

        public static IEnumerable<string> Validate(TournamentRound round)
        {
            if (round.BanPickFlowGroups.Count == 0)
                yield return "尚未设置流程：可以从模板生成，或添加阶段。";

            foreach (var group in round.BanPickFlowGroups)
            {
                if (group.Steps.Count == 0)
                    yield return $"阶段“{group.Name.Value}”没有操作。";
            }

            var slots = Resolve(round, Array.Empty<BeatmapChoice>(), TeamColour.Red);
            if (slots.Count > 0 && slots[0].Step.TeamRule.Value is BanPickTeamRule.Previous or BanPickTeamRule.Opponent)
                yield return "第一步请明确选择先手队、后手队、红队或蓝队。";
            if (round.BanPickFlowBestOf.Value is int generated && generated != round.BestOf.Value)
                yield return $"流程按 BO{generated} 生成，当前为 BO{round.BestOf.Value}；请检查或重新生成。";
            if (round.SeparateTiebreaker.Value && slots.Any(s => s.Step.CurrentAction.Value == ChoiceType.TB))
                yield return "流程已包含 TB，请关闭单独安排 TB，避免重复展示。";

            int picks = slots.Count(s => s.Step.CurrentAction.Value is ChoiceType.Pick or ChoiceType.TB) + (round.SeparateTiebreaker.Value ? 1 : 0);
            if (slots.Count > 0 && picks != round.BestOf.Value)
                yield return $"已安排 {picks} 个比赛位置（含单独 TB），当前 BO{round.BestOf.Value}。";
        }

        public static string ActionName(ChoiceType action) => action switch
        {
            ChoiceType.Protected => "保护",
            ChoiceType.Ban => "禁图",
            ChoiceType.Pick => "选图",
            _ => "TB"
        };
    }

    public class BanPickFlowSlot
    {
        public int Index { get; }
        public BanPickFlowGroup Group { get; }
        public BanPickFlowStep Step { get; }
        public TeamColour Team { get; internal set; }
        public BeatmapChoice? Choice { get; internal set; }

        public BanPickFlowSlot(int index, BanPickFlowGroup group, BanPickFlowStep step)
        {
            Index = index;
            Group = group;
            Step = step;
        }
    }
}
