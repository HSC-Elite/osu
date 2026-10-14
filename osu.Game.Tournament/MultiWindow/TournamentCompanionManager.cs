using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Online.Multiplayer;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.MultiWindow
{
    [Cached]
    public partial class TournamentCompanionManager : Component
    {
        private readonly TournamentWindowRole role;
        private readonly string localPipeName;
        private readonly string remotePipeName;

        private readonly NamedPipeIpcProvider localProvider;
        private NamedPipeIpcProvider? remoteProvider;
        private Process? companionProcess;

        private int applyingRemoteState;

        public event Action<TournamentWindowActionMessage>? ActionMessageReceived;
        public event Action<TournamentWindowSyncMessage>? SyncMessageReceived;

        [Resolved]
        private TournamentStageState stageState { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        [Resolved]
        private LadderInfo? ladderInfo { get; set; }

        public TournamentCompanionManager(TournamentWindowRole role, string localPipeName, string remotePipeName)
        {
            this.role = role;
            this.localPipeName = localPipeName;
            this.remotePipeName = remotePipeName;

            localProvider = new NamedPipeIpcProvider(localPipeName);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            localProvider.MessageReceived += onMessageReceived;

            if (!localProvider.Bind())
                Logger.Log($"Failed to bind tournament companion IPC server ({localPipeName}).", LoggingTarget.Runtime, LogLevel.Error);

            if (role == TournamentWindowRole.Primary)
            {
                ladderInfo?.UseExternalStageDisplay.BindValueChanged(enabled =>
                {
                    if (enabled.NewValue)
                        OpenControlWindow();
                    else
                        CloseControlWindow();
                }, true);
            }

            stageState.ControlPanelLayoutJson.BindValueChanged(layout => broadcast(new TournamentWindowSyncMessage
            {
                MessageType = TournamentWindowSyncMessageType.ControlPanelLayoutChanged,
                ControlPanelLayoutJson = layout.NewValue,
            }));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (role == TournamentWindowRole.Control)
                requestInitialState();
        }

        public void OpenControlWindow()
        {
            if (role != TournamentWindowRole.Primary)
                return;

            if (companionProcess is { HasExited: false })
            {
                send(new IpcMessage
                {
                    Type = typeof(TournamentWindowSyncMessage).AssemblyQualifiedName,
                    Value = new TournamentWindowSyncMessage
                    {
                        MessageType = TournamentWindowSyncMessageType.ActivateWindow,
                    }
                });
                return;
            }

            try
            {
                companionProcess = Process.Start(createControlStartInfo());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to launch tournament control window.");
            }
        }

        public void CloseControlWindow()
        {
            if (role != TournamentWindowRole.Primary)
                return;

            if (companionProcess is not { HasExited: false })
                return;

            send(new IpcMessage
            {
                Type = typeof(TournamentWindowSyncMessage).AssemblyQualifiedName,
                Value = new TournamentWindowSyncMessage
                {
                    MessageType = TournamentWindowSyncMessageType.CloseWindow,
                }
            });

            try
            {
                if (!companionProcess.WaitForExit(1500))
                    companionProcess.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to close tournament control window cleanly: {ex.Message}", LoggingTarget.Runtime, LogLevel.Verbose);
            }
            finally
            {
                companionProcess = null;
            }
        }

        public void SendAction(TournamentWindowActionMessage message)
        {
            send(new IpcMessage
            {
                Type = typeof(TournamentWindowActionMessage).AssemblyQualifiedName,
                Value = message,
            });
        }

        public void BroadcastControlState(string key, string property, object? value)
        {
            broadcast(new TournamentWindowSyncMessage
            {
                MessageType = TournamentWindowSyncMessageType.ControlStateChanged,
                ControlKey = key,
                ControlProperty = property,
                JsonValue = value == null ? null : Newtonsoft.Json.JsonConvert.SerializeObject(value),
            });
        }

        private ProcessStartInfo createControlStartInfo()
        {
            string executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("Unable to determine the current process path.");
            string entryAssemblyPath = RuntimeInfo.EntryAssembly.Location;

            string arguments = $"\"{entryAssemblyPath}\" --tournament-control-window --tournament-sync-local={remotePipeName} --tournament-sync-remote={localPipeName}";

            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false,
            };

            if (Path.GetFileNameWithoutExtension(executablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.FileName = executablePath;
                startInfo.Arguments = arguments;
            }
            else
            {
                startInfo.FileName = executablePath;
                startInfo.Arguments = $"--tournament-control-window --tournament-sync-local={remotePipeName} --tournament-sync-remote={localPipeName}";
            }

            return startInfo;
        }

        private void requestInitialState()
        {
            Task.Run(async () =>
            {
                var response = await sendWithResponse(new TournamentWindowSyncMessage
                {
                    MessageType = TournamentWindowSyncMessageType.RequestState,
                }).ConfigureAwait(false);

                if (response != null)
                    Schedule(() => applyMessage(response));
            });
        }

        private IpcMessage? onMessageReceived(IpcMessage message)
        {
            if (message.Type == typeof(TournamentWindowActionMessage).AssemblyQualifiedName)
            {
                var actionMessage = (TournamentWindowActionMessage)message.Value;
                Schedule(() => ActionMessageReceived?.Invoke(actionMessage));
                return null;
            }

            if (message.Type != typeof(TournamentWindowSyncMessage).AssemblyQualifiedName)
                return null;

            var syncMessage = (TournamentWindowSyncMessage)message.Value;

            if (syncMessage.MessageType == TournamentWindowSyncMessageType.RequestState)
            {
                return new IpcMessage
                {
                    Type = typeof(TournamentWindowSyncMessage).AssemblyQualifiedName,
                    Value = createSnapshotMessage(),
                };
            }

            Schedule(() => applyMessage(syncMessage));
            return null;
        }

        private TournamentWindowSyncMessage createSnapshotMessage() => new TournamentWindowSyncMessage
        {
            MessageType = TournamentWindowSyncMessageType.StateSnapshot,
            ControlPanelLayoutJson = stageState.ControlPanelLayoutJson.Value,
        };

        private void applyMessage(TournamentWindowSyncMessage message)
        {
            applyingRemoteState++;

            try
            {
                switch (message.MessageType)
                {
                    case TournamentWindowSyncMessageType.ActivateWindow:
                        host.Window?.Raise();
                        break;

                    case TournamentWindowSyncMessageType.CloseWindow:
                        host.Exit();
                        break;

                    case TournamentWindowSyncMessageType.ControlPanelLayoutChanged:
                    case TournamentWindowSyncMessageType.StateSnapshot:
                        if (message.MessageType == TournamentWindowSyncMessageType.StateSnapshot || message.ControlPanelLayoutJson != null)
                            stageState.ControlPanelLayoutJson.Value = message.ControlPanelLayoutJson ?? string.Empty;

                        break;

                    case TournamentWindowSyncMessageType.ControlStateChanged:
                        SyncMessageReceived?.Invoke(message);
                        break;
                }
            }
            finally
            {
                applyingRemoteState--;
            }
        }

        private void broadcast(TournamentWindowSyncMessage message)
        {
            if (applyingRemoteState > 0)
                return;

            send(new IpcMessage
            {
                Type = typeof(TournamentWindowSyncMessage).AssemblyQualifiedName,
                Value = message,
            });
        }

        private void send(IpcMessage message)
        {
            getRemoteProvider().SendMessageAsync(message).FireAndForget(onError: ex =>
                Logger.Log($"Failed to send tournament companion IPC message: {ex.Message}", LoggingTarget.Runtime, LogLevel.Verbose));
        }

        private async Task<TournamentWindowSyncMessage?> sendWithResponse(TournamentWindowSyncMessage message)
        {
            try
            {
                var response = await getRemoteProvider().SendMessageWithResponseAsync(new IpcMessage
                {
                    Type = typeof(TournamentWindowSyncMessage).AssemblyQualifiedName,
                    Value = message,
                }).ConfigureAwait(false);

                return response?.Value as TournamentWindowSyncMessage;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to request tournament companion state: {ex.Message}", LoggingTarget.Runtime, LogLevel.Verbose);
                return null;
            }
        }

        private NamedPipeIpcProvider getRemoteProvider() => remoteProvider ??= new NamedPipeIpcProvider(remotePipeName);

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            remoteProvider?.Dispose();
            localProvider.Dispose();
        }
    }
}
