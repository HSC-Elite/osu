namespace osu.Game.Tournament.MultiWindow
{
    public class TournamentWindowSyncMessage
    {
        public TournamentWindowSyncMessageType MessageType;

        public string? ControlPanelLayoutJson;

        public string? ControlKey;

        public string? ControlProperty;

        public string? JsonValue;
    }

    public enum TournamentWindowSyncMessageType
    {
        RequestState,
        StateSnapshot,
        ActivateWindow,
        CloseWindow,
        ControlPanelLayoutChanged,
        ControlStateChanged,
    }
}
