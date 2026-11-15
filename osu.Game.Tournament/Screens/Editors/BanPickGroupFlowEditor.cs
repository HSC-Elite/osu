// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Overlays.Settings;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Gameplay.Components.RoundInformation;
using osuTK;

namespace osu.Game.Tournament.Screens.Editors
{
    public partial class BanPickGroupFlowEditor : TournamentScreen
    {
        private readonly TournamentRound round;
        private readonly TournamentScreen parentScreen;
        private readonly Bindable<TeamColour> previewFirstTeam = new Bindable<TeamColour>();
        private FillFlowContainer stages = null!;
        private FillFlowContainer preview = null!;
        private TournamentSpriteText summary = null!;
        private TournamentSpriteText validation = null!;
        private BanPickFlowSubscription? subscription;

        [Resolved]
        private TournamentSceneManager? sceneManager { get; set; }

        [Resolved]
        private IDialogOverlay? dialogOverlay { get; set; }

        public BanPickGroupFlowEditor(TournamentRound round, TournamentScreen parentScreen)
        {
            this.round = round;
            this.parentScreen = parentScreen;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Gray(0.12f) },
                new OsuScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 16),
                        Padding = new MarginPadding { Top = 24, Left = 24, Right = 24, Bottom = 120 },
                        Children = new Drawable[]
                        {
                            text($"{round.Name.Value} · 选禁流程", 28),
                            text("按阶段安排选禁；下方预览与比赛展示使用同一条流程。模板生成后可以自由修改。", 18),
                            row(button("按当前 BO 生成比赛模板", generate, 340)),
                            text("双方各保护一张 → 双方各禁图一张 → 交替选图；TB 单独安排。", 18),
                            summary = text("", 20),
                            row(new LabelledEnumDropdown<TeamColour>(true)
                            {
                                Label = "预览：先手队颜色", Width = 360, Current = previewFirstTeam
                            }),
                            new BasicScrollContainer(Direction.Horizontal)
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 150,
                                Child = preview = new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(24, 0)
                                }
                            },
                            validation = new TournamentSpriteText
                            {
                                RelativeSizeAxes = Axes.X,
                                AllowMultiline = true,
                                Font = OsuFont.Default.With(size: 18)
                            },
                            stages = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 14)
                            },
                            button("+ 添加阶段", () => round.BanPickFlowGroups.Add(new BanPickFlowGroup { Name = { Value = "新阶段" } }), 240)
                        }
                    }
                },
                new ControlPanel
                {
                    Children = new Drawable[]
                    {
                        button("复制到其他轮次", applyToAllRounds, 240),
                        button("清空流程", () => confirm("清空所有选禁阶段？", () => round.BanPickFlowGroups.Clear()), 240),
                        new TournamentSpriteText
                        {
                            RelativeSizeAxes = Axes.X,
                            AllowMultiline = true,
                            Font = OsuFont.Default.With(size: 16),
                            Text = "比赛中的先手队颜色在图池控制面板设置。"
                        }
                    }
                },
                new BackButton
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    State = { Value = Visibility.Visible },
                    Action = () => sceneManager?.SetScreen(parentScreen.GetType())
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            // Flatten legacy repetitions once, so every visible row is one actual operation.
            bool firstStep = true;

            foreach (var group in round.BanPickFlowGroups)
            {
                if (group.RepeatCount.Value != 0)
                {
                    var expanded = group.DeepClone();
                    group.RepeatCount.Value = 0;
                    group.Steps.Clear();
                    group.Steps.AddRange(expanded.Steps);
                }

                foreach (var step in group.Steps)
                {
                    if (step.TeamRule.Value == BanPickTeamRule.Legacy)
                    {
                        step.TeamRule.Value = firstStep
                            ? step.SwapFromLastColor.Value ? BanPickTeamRule.Blue : BanPickTeamRule.Red
                            : step.SwapFromLastColor.Value
                                ? BanPickTeamRule.Opponent
                                : BanPickTeamRule.Previous;
                    }

                    firstStep = false;
                }
            }

            round.BanPickFlowGroups.CollectionChanged += stagesChanged;
            subscription = new BanPickFlowSubscription(round, () => Scheduler.AddOnce(refreshPreview));
            previewFirstTeam.BindValueChanged(_ => refreshPreview());
            refreshStages();
            refreshPreview();
        }

        private void stagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Scheduler.AddOnce(refreshStages);

        private void refreshStages()
        {
            stages.Clear();
            foreach (var group in round.BanPickFlowGroups)
                stages.Add(new BanPickGroupRow(round, group));
        }

        private void refreshPreview()
        {
            var slots = BanPickFlow.Resolve(round, Array.Empty<BeatmapChoice>(), previewFirstTeam.Value);
            summary.Text = $"BO{round.BestOf.Value} · {round.BanPickFlowGroups.Count} 个阶段 · {slots.Count} 次操作" + (round.SeparateTiebreaker.Value ? " + 独立 TB" : "");
            validation.Text = string.Join("\n", BanPickFlow.Validate(round));
            preview.Clear();
            foreach (var group in round.BanPickFlowGroups)
                preview.Add(new BanPickStageDisplay(group.Name.Value, slots.Where(s => s.Group == group)));
            if (round.SeparateTiebreaker.Value)
                preview.Add(text("TB\n决胜时手动选择", 18));
        }

        private void generate()
        {
            if (round.BestOf.Value % 2 == 0)
            {
                validation.Text = "BO 必须为奇数，请先在轮次编辑器中调整。";
                return;
            }

            int bestOf = round.BestOf.Value;
            var generated = BanPickFlow.CreateTemplate(bestOf);

            void apply()
            {
                round.BanPickFlowGroups.Clear();
                round.BanPickFlowGroups.AddRange(generated);
                round.SeparateTiebreaker.Value = true;
                round.BanPickFlowBestOf.Value = bestOf;
            }

            if (round.BanPickFlowGroups.Count == 0)
                apply();
            else
                confirm($"用 BO{bestOf} 普通比赛模板覆盖现有选禁流程？取消将保留现有内容。", apply);
        }

        private void applyToAllRounds()
        {
            var targets = LadderInfo.Rounds.Where(r => r != round).ToArray();
            if (targets.Length == 0)
                return;

            confirm("覆盖以下轮次的选禁流程：" + string.Join("、", targets.Select(r => r.Name.Value)), () =>
            {
                foreach (var target in targets)
                {
                    target.BanPickFlowGroups.Clear();
                    target.BanPickFlowGroups.AddRange(round.BanPickFlowGroups.Select(g => g.DeepClone()));
                    target.SeparateTiebreaker.Value = round.SeparateTiebreaker.Value;
                    target.BanPickFlowBestOf.Value = round.BanPickFlowBestOf.Value;
                }
            });
        }

        private void confirm(string message, Action action) => dialogOverlay?.Push(new ReplaceFlowDialog(message, action));

        private partial class ReplaceFlowDialog : DangerousActionDialog
        {
            public ReplaceFlowDialog(string message, Action action)
            {
                HeaderText = message;
                DangerousAction = action;
            }
        }

        private static TournamentSpriteText text(string value, float size) => new TournamentSpriteText { Text = value, Font = OsuFont.Default.With(size: size) };

        private static TourneyButton button(string label, Action action, float width = 110) => new TourneyButton
        {
            Text = label, Action = action, RelativeSizeAxes = Axes.None, Width = width
        };

        private static FillFlowContainer row(params Drawable[] children)
        {
            // Settings controls default to relative widths. Rows use pixel widths and wrap as needed.
            foreach (var child in children)
                child.RelativeSizeAxes = Axes.None;
            return new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Full,
                Spacing = new Vector2(10), Children = children
            };
        }

        private partial class ActionDropdown : LabelledDropdown<ChoiceType>
        {
            public ActionDropdown()
                : base(true)
            {
                Items = Enum.GetValues<ChoiceType>();
            }

            protected override OsuDropdown<ChoiceType> CreateDropdown() => new ActionOptions();

            private partial class ActionOptions : OsuDropdown<ChoiceType>
            {
                protected override LocalisableString GenerateItemText(ChoiceType item) => BanPickFlow.ActionName(item);
            }
        }

        private static void move<T>(BindableList<T> list, T item, int delta)
        {
            int index = list.IndexOf(item);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= list.Count)
                return;

            list.RemoveAt(index);
            list.Insert(target, item);
        }

        protected override void Dispose(bool isDisposing)
        {
            subscription?.Dispose();
            round.BanPickFlowGroups.CollectionChanged -= stagesChanged;
            base.Dispose(isDisposing);
        }

        public partial class BanPickGroupRow : CompositeDrawable, IModelBacked<BanPickFlowGroup>
        {
            private readonly TournamentRound round;
            public BanPickFlowGroup Model { get; }
            private FillFlowContainer stepsContainer = null!;

            public BanPickGroupRow(TournamentRound round, BanPickFlowGroup model)
            {
                this.round = round;
                Model = model;
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Masking = true;
                CornerRadius = 10;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Gray(0.18f) },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(16),
                        Spacing = new Vector2(0, 10),
                        Children = new Drawable[]
                        {
                            row(
                                new SettingsTextBox { LabelText = "阶段名称", Width = 340, Current = Model.Name },
                                button("上移阶段", () => move(round.BanPickFlowGroups, Model, -1)),
                                button("下移阶段", () => move(round.BanPickFlowGroups, Model, 1)),
                                button("复制阶段", () => round.BanPickFlowGroups.Insert(round.BanPickFlowGroups.IndexOf(Model) + 1, Model.DeepClone())),
                                button("删除阶段", () => round.BanPickFlowGroups.Remove(Model))),
                            stepsContainer = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical, Spacing = new Vector2(0, 8)
                            },
                            row(
                                button("+ 添加操作", () => Model.Steps.Add(new BanPickFlowStep
                                {
                                    CurrentAction = { Value = Model.Steps.LastOrDefault()?.CurrentAction.Value ?? ChoiceType.Pick },
                                    TeamRule = { Value = Model.Steps.Count == 0 ? BanPickTeamRule.First : BanPickTeamRule.Opponent }
                                }), 160),
                                button("+ 双方各禁一次", () => addPair(ChoiceType.Ban), 190),
                                button("+ 双方各选一次", () => addPair(ChoiceType.Pick), 190),
                                button("+ 双方各保一次", () => addPair(ChoiceType.Protected), 190))
                        }
                    }
                };
                Model.Steps.CollectionChanged += stepsChanged;
                refreshSteps();
            }

            private void addPair(ChoiceType action)
            {
                foreach (var team in new[] { BanPickTeamRule.First, BanPickTeamRule.Second })
                    Model.Steps.Add(new BanPickFlowStep { CurrentAction = { Value = action }, TeamRule = { Value = team } });
            }

            private void stepsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Scheduler.AddOnce(refreshSteps);

            private void refreshSteps()
            {
                stepsContainer.Clear();

                for (int i = 0; i < Model.Steps.Count; i++)
                {
                    var step = Model.Steps[i];
                    stepsContainer.Add(row(
                        text($"{i + 1:D2}", 20),
                        new ActionDropdown { Label = "操作", Width = 260, Current = step.CurrentAction },
                        new LabelledEnumDropdown<BanPickTeamRule>(true)
                        {
                            Label = "执行方", Width = 340, Current = step.TeamRule,
                            Items = Enum.GetValues<BanPickTeamRule>().Where(t => t != BanPickTeamRule.Legacy)
                        },
                        button("上移", () => move(Model.Steps, step, -1)),
                        button("下移", () => move(Model.Steps, step, 1)),
                        button("删除", () => Model.Steps.Remove(step))));
                }
            }

            protected override void Dispose(bool isDisposing)
            {
                Model.Steps.CollectionChanged -= stepsChanged;
                base.Dispose(isDisposing);
            }
        }
    }
}
