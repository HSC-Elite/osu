// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.Multiplayer;
using osu.Game.Tournament.IPC.MemoryIPC;
using osu.Game.Tournament.Models;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tournament.Tests.IPC.MemoryIPC
{
    public partial class TestSceneStableMemoryReader : TournamentTestScene
    {
        private readonly Bindable<ProcessItem> selectedProcess = new Bindable<ProcessItem>(ProcessItem.None);

        private StableMemoryReader reader = new StableMemoryReader();

        private OsuDropdown<ProcessItem> processDropdown = null!;
        private RoundedButton attachButton = null!;
        private OsuSpriteText statusText = null!;
        private OsuSpriteText dataText = null!;

        private bool attaching;
        private int attachAttempt;
        private string lastAttachResult = "No process attached.";
        private string lastReadResult = "Waiting for an attached stable process.";
        private double nextReadAt;

        public TestSceneStableMemoryReader()
        {
            Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 900,
                Height = 620,
                Masking = true,
                CornerRadius = 8,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black.Opacity(0.75f),
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(20),
                        Spacing = new Vector2(10),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = "Stable memory reader",
                                Font = OsuFont.GetFont(size: 24, weight: FontWeight.Bold),
                            },
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 44,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10),
                                Depth = float.MinValue,
                                Children = new Drawable[]
                                {
                                    processDropdown = new OsuDropdown<ProcessItem>
                                    {
                                        Width = 610,
                                        Current = selectedProcess,
                                        Depth = float.MinValue
                                    },
                                    new RoundedButton
                                    {
                                        Text = "Refresh",
                                        Width = 110,
                                        Action = refreshProcesses,
                                    },
                                    attachButton = new RoundedButton
                                    {
                                        Text = "Attach",
                                        Width = 110,
                                        Action = () => attachSelectedProcess().FireAndForget(),
                                    },
                                }
                            },
                            statusText = new OsuSpriteText
                            {
                                RelativeSizeAxes = Axes.X,
                                Font = OsuFont.GetFont(size: 16),
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 410,
                                Masking = true,
                                CornerRadius = 6,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = Color4.Black.Opacity(0.45f),
                                    },
                                    dataText = new OsuSpriteText
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Padding = new MarginPadding(14),
                                        Font = OsuFont.GetFont(size: 16),
                                    },
                                }
                            },
                        }
                    },
                }
            });

            selectedProcess.BindValueChanged(_ => updateAttachButtonState(), true);
            refreshProcesses();
            updateReadout();
        }

        private void refreshProcesses()
        {
            int? previousProcessId = selectedProcess.Value.ProcessId;

            ProcessItem[] items = Process.GetProcesses()
                                         .Select(createProcessItem)
                                         .Where(item => item != null)
                                         .Cast<ProcessItem>()
                                         .OrderByDescending(item => item.IsLikelyStable)
                                         .ThenByDescending(item => item.HasWindowTitle)
                                         .ThenBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
                                         .ThenBy(item => item.ProcessId)
                                         .Prepend(ProcessItem.None)
                                         .ToArray();

            processDropdown.Items = items;
            selectedProcess.Value = items.FirstOrDefault(item => item.ProcessId == previousProcessId)
                                    ?? items.FirstOrDefault(item => item.IsLikelyStable)
                                    ?? ProcessItem.None;

            lastAttachResult = $"Process list refreshed. {items.Length - 1} processes available.";
            updateAttachButtonState();
            updateReadout();
        }

        private static ProcessItem? createProcessItem(Process process)
        {
            try
            {
                return new ProcessItem(process.Id, process.ProcessName, process.MainWindowTitle);
            }
            catch
            {
                return null;
            }
            finally
            {
                process.Dispose();
            }
        }

        private async Task attachSelectedProcess()
        {
            if (attaching)
                return;

            ProcessItem item = selectedProcess.Value;

            if (item.ProcessId == null)
                return;

            int attempt = ++attachAttempt;
            attaching = true;
            updateAttachButtonState();

            reader.Dispose();
            reader = new StableMemoryReader();

            lastAttachResult = $"Attaching to {item}...";
            lastReadResult = "Attach in progress.";
            updateReadout();

            try
            {
                Process process = Process.GetProcessById(item.ProcessId.Value);
                bool attached = await reader.AttachToProcessAsync(process).ConfigureAwait(false);

                Schedule(() =>
                {
                    if (attempt != attachAttempt)
                        return;

                    attaching = false;
                    lastAttachResult = attached
                        ? $"Attached to {item}."
                        : $"Failed to attach to {item}. Stable osu! must be running as osu!.exe.";
                    updateAttachButtonState();
                    updateReadout();
                });
            }
            catch (Exception ex)
            {
                Schedule(() =>
                {
                    if (attempt != attachAttempt)
                        return;

                    attaching = false;
                    lastAttachResult = $"Attach failed: {ex.GetType().Name}: {ex.Message}";
                    updateAttachButtonState();
                    updateReadout();
                });
            }
        }

        private void updateAttachButtonState()
        {
            if (attachButton == null)
                return;

            attachButton.Enabled.Value = !attaching && selectedProcess.Value.ProcessId != null;
        }

        protected override void Update()
        {
            base.Update();

            if (Time.Current < nextReadAt)
                return;

            nextReadAt = Time.Current + 500;
            updateReadout();
        }

        private void updateReadout()
        {
            if (statusText == null || dataText == null)
                return;

            statusText.Text =
                $"Selected: {selectedProcess.Value}\n" +
                $"Reader status: {reader.Status} | Handle: 0x{reader.ProcessHandle.ToInt64():X} | {lastAttachResult}";

            List<string> lines = new List<string>
            {
                $"Attached process: {describeAttachedProcess()}",
            };

            if (!OperatingSystem.IsWindows())
            {
                lastReadResult = "StableMemoryReader is only supported on Windows.";
                lines.Insert(0, $"Last read: {lastReadResult}");
                dataText.Text = string.Join('\n', lines);
                return;
            }

            try
            {
                TournamentUser? user = reader.GetTournamentUser();
                GameplayData? gameplay = reader.GetGameplayData();

                lines.Add($"Beatmap ID: {reader.GetBeatmapId()}");
                lines.Add($"Current mods: {reader.GetMods()}");

                if (user == null)
                {
                    lines.Add("Tournament user: <no data>");
                }
                else
                {
                    lines.Add("Tournament user:");
                    lines.Add($"  Username: {user.Username}");
                    lines.Add($"  Online ID: {user.OnlineID}");
                }

                if (gameplay == null)
                {
                    lines.Add("Gameplay data: <no data>");
                }
                else
                {
                    lines.Add("Gameplay data:");
                    lines.Add($"  Player: {gameplay.PlayerName}");
                    lines.Add($"  Ruleset ID: {gameplay.RulesetId}");
                    lines.Add($"  Mods: {gameplay.Mods}");
                    lines.Add($"  Score: {gameplay.Score:N0}");
                    lines.Add($"  Accuracy: {gameplay.Accuracy:0.00}%");
                    lines.Add($"  HP: {gameplay.PlayerHP:0.000} (smooth {gameplay.PlayerHPSmooth:0.000})");
                    lines.Add($"  Combo: {gameplay.Combo:N0} / {gameplay.MaxCombo:N0}");
                    lines.Add($"  Hits: 300={gameplay.Hit300:N0}, 100={gameplay.Hit100:N0}, 50={gameplay.Hit50:N0}, miss={gameplay.HitMiss:N0}");
                    lines.Add($"  Geki/Katu: {gameplay.HitGeki:N0}/{gameplay.HitKatu:N0}");
                }

                lastReadResult = reader.Status == AttachStatus.Attached ? "OK" : "Reader is not attached.";
            }
            catch (Exception ex)
            {
                lastReadResult = $"{ex.GetType().Name}: {ex.Message}";
                lines.Add($"Read error: {lastReadResult}");
            }

            lines.Insert(0, $"Last read: {lastReadResult}");
            dataText.Text = string.Join('\n', lines);
        }

        private string describeAttachedProcess()
        {
            Process? process = reader.Process;

            if (process == null)
                return "<none>";

            try
            {
                return process.HasExited ? $"{process.ProcessName} [{process.Id}] exited" : $"{process.ProcessName} [{process.Id}]";
            }
            catch (Exception ex)
            {
                return $"<unavailable: {ex.GetType().Name}>";
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            reader.Dispose();
            base.Dispose(isDisposing);
        }

        private sealed class ProcessItem
        {
            public static readonly ProcessItem None = new ProcessItem(null, "<select a process>", string.Empty);

            public int? ProcessId { get; }
            public string ProcessName { get; }
            public string WindowTitle { get; }

            public bool HasWindowTitle => !string.IsNullOrWhiteSpace(WindowTitle);

            public bool IsLikelyStable => ProcessName.Equals("osu!", StringComparison.OrdinalIgnoreCase)
                                          || WindowTitle.Contains("osu!", StringComparison.OrdinalIgnoreCase);

            public ProcessItem(int? processId, string processName, string windowTitle)
            {
                ProcessId = processId;
                ProcessName = processName;
                WindowTitle = windowTitle;
            }

            public override string ToString()
            {
                if (ProcessId == null)
                    return ProcessName;

                string title = HasWindowTitle ? $" - {WindowTitle}" : string.Empty;
                return $"{ProcessName} [{ProcessId}]{title}";
            }
        }
    }
}
