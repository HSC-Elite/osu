namespace osu.Game.Tournament.MultiWindow
{
    public class TournamentWindowActionMessage
    {
        public TournamentWindowActionType ActionType;

        public string? ControlKey;

        public string? ControlOperation;

        public string? JsonValue;
    }

    public enum TournamentWindowActionType
    {
        ControlOperation,
    }
}
