using osu.Framework.Bindables;

namespace osu.Game.Tournament.MultiWindow
{
    public class TournamentStageState
    {
        public readonly Bindable<string> ControlPanelLayoutJson = new Bindable<string>(string.Empty);
    }
}
