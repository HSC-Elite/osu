// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Overlays.Dialog;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Editors;
using osu.Game.Tournament.Screens.Gameplay.Components.RoundInformation;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneBanPickGroupFlowEditor : TournamentScreenTestScene
    {
        private BanPickGroupFlowEditor editor = null!;
        private TournamentRound round = null!;

        [SetUpSteps]
        public override void SetUpSteps()
        {
            base.SetUpSteps();
            AddStep("load ordinary BO7 editor", () =>
            {
                RemoveAll(d => d != DialogOverlay, true);
                round = new TournamentRound { Name = { Value = "BO7 普通比赛" }, BestOf = { Value = 7 }, SeparateTiebreaker = { Value = true } };
                round.BanPickFlowGroups.AddRange(BanPickFlow.CreateTemplate(7));
                Add(editor = new BanPickGroupFlowEditor(round, new RoundEditorScreen()) { Width = 0.85f });
            });
            AddUntilStep("editor loaded", () => editor.IsLoaded);
        }

        [Test]
        public void TestStageEditingAndLivePreview()
        {
            AddAssert("three stage cards", () => editor.ChildrenOfType<BanPickGroupFlowEditor.BanPickGroupRow>().Count(), () => Is.EqualTo(3));
            AddAssert("three preview sections", () => editor.ChildrenOfType<BanPickStageDisplay>().Count(), () => Is.EqualTo(3));
            AddStep("rename pick stage", () => round.BanPickFlowGroups[2].Name.Value = "自定义选图");
            AddUntilStep("preview renamed", () => editor.ChildrenOfType<TournamentSpriteText>().Any(t => t.Text == "自定义选图"));
            AddStep("add stage", () => editor.ChildrenOfType<TourneyButton>().Single(b => b.Text == "+ 添加阶段").TriggerClick());
            AddUntilStep("four stage cards", () => editor.ChildrenOfType<BanPickGroupFlowEditor.BanPickGroupRow>().Count() == 4);
            AddStep("add pair", () => editor.ChildrenOfType<BanPickGroupFlowEditor.BanPickGroupRow>().Last().ChildrenOfType<TourneyButton>()
                                                         .Single(b => b.Text == "+ 双方各选一次").TriggerClick());
            AddUntilStep("pair created", () => round.BanPickFlowGroups.Last().Steps.Count == 2);
            AddUntilStep("preview updated", () => editor.ChildrenOfType<BanPickStageDisplay>().Count() == 4);
            AddStep("duplicate stage", () => editor.ChildrenOfType<BanPickGroupFlowEditor.BanPickGroupRow>().Last().ChildrenOfType<TourneyButton>()
                                                               .Single(b => b.Text == "复制阶段").TriggerClick());
            AddUntilStep("five stages", () => round.BanPickFlowGroups.Count == 5);
            AddStep("edit clone", () => round.BanPickFlowGroups.Last().Steps[0].CurrentAction.Value = ChoiceType.Ban);
            AddAssert("source independent", () => round.BanPickFlowGroups[3].Steps[0].CurrentAction.Value, () => Is.EqualTo(ChoiceType.Pick));
            AddStep("move clone up", () => editor.ChildrenOfType<BanPickGroupFlowEditor.BanPickGroupRow>().Last().ChildrenOfType<TourneyButton>()
                                                             .Single(b => b.Text == "上移阶段").TriggerClick());
            AddUntilStep("clone reordered", () => round.BanPickFlowGroups[3].Steps[0].CurrentAction.Value == ChoiceType.Ban);
            AddStep("clear model", () => round.BanPickFlowGroups.Clear());
            AddUntilStep("cards removed", () => !editor.ChildrenOfType<BanPickGroupFlowEditor.BanPickGroupRow>().Any());
            AddUntilStep("preview cleared", () => !editor.ChildrenOfType<BanPickStageDisplay>().Any());
        }

        [Test]
        public void TestExistingFlowRequiresConfirmation()
        {
            BanPickFlowGroup original = null!;
            AddStep("customize existing flow", () =>
            {
                original = round.BanPickFlowGroups[2];
                original.Name.Value = "保留我的自定义流程";
                round.SeparateTiebreaker.Value = false;
                round.BestOf.Value = 9;
            });
            AddStep("request template", () => editor.ChildrenOfType<TourneyButton>().Single(b => b.Text == "按当前 BO 生成普通比赛模板").TriggerClick());
            AddUntilStep("confirmation visible", () => DialogOverlay.CurrentDialog != null);
            AddAssert("existing content unchanged", () => ReferenceEquals(round.BanPickFlowGroups[2], original) && !round.SeparateTiebreaker.Value);
            AddStep("cancel replacement", () => DialogOverlay.CurrentDialog!.PerformAction<PopupDialogCancelButton>());
            AddAssert("cancel preserved custom name", () => original.Name.Value, () => Is.EqualTo("保留我的自定义流程"));
            AddAssert("cancel preserved settings", () => !round.SeparateTiebreaker.Value && ReferenceEquals(round.BanPickFlowGroups[2], original));
            AddStep("request again", () => editor.ChildrenOfType<TourneyButton>().Single(b => b.Text == "按当前 BO 生成普通比赛模板").TriggerClick());
            AddUntilStep("confirmation visible again", () => DialogOverlay.CurrentDialog != null);
            AddStep("confirm replacement", () => DialogOverlay.CurrentDialog!.PerformAction<PopupDialogDangerousButton>());
            AddUntilStep("BO9 ordinary template applied", () => round.BanPickFlowGroups.Count == 3 && round.BanPickFlowGroups[2].Steps.Count == 8);
            AddAssert("separate TB restored after confirmation", () => round.SeparateTiebreaker.Value);
            AddAssert("provenance updated", () => round.BanPickFlowBestOf.Value, () => Is.EqualTo(9));
        }

        [Test]
        public void TestGenerateStandardTemplate()
        {
            AddStep("clear model", () => round.BanPickFlowGroups.Clear());
            AddStep("generate", () => editor.ChildrenOfType<TourneyButton>().Single(b => b.Text == "按当前 BO 生成普通比赛模板").TriggerClick());
            AddUntilStep("three stages", () => round.BanPickFlowGroups.Count == 3);
            AddAssert("six ordinary picks and separate TB", () => round.BanPickFlowGroups[2].Steps.Count == 6 && round.SeparateTiebreaker.Value);
            AddStep("change BO", () => round.BestOf.Value = 9);
            AddUntilStep("BO mismatch shown", () => editor.ChildrenOfType<TournamentSpriteText>().Any(t => t.Text.ToString().Contains("流程按 BO7")));
            AddStep("set even BO", () => round.BestOf.Value = 8);
            AddStep("attempt generation", () => editor.ChildrenOfType<TourneyButton>().Single(b => b.Text == "按当前 BO 生成普通比赛模板").TriggerClick());
            AddAssert("invalid BO reported", () => editor.ChildrenOfType<TournamentSpriteText>().Any(t => t.Text.ToString().Contains("BO 必须为奇数")));
            AddAssert("existing flow retained", () => round.BanPickFlowGroups[2].Steps.Count, () => Is.EqualTo(6));
        }
    }
}
