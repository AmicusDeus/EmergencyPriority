using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace EmergencyPriority
{
    [FileLocation(nameof(EmergencyPriority))]
    public class Setting : ModSetting
    {
        public const string Section = "Main";
        public const string Group = "Emergency";

        public Setting(IMod mod) : base(mod) { }

        // NOTE: initializers double as the settings-migration failsafe (missing keys in an old .coc keep these
        // values instead of defaulting to 0/false).

        // Master switch. OFF = pure vanilla (stuck responders get deleted, routes never re-evaluated).
        [SettingsUISection(Section, Group)]
        public bool Enabled { get; set; } = true;

        // Vanilla deletes a responding vehicle outright once the stuck detector flags it. The guard converts that
        // give-up into a fresh pathfind instead, so the unit keeps responding.
        [SettingsUISection(Section, Group)]
        public bool DespawnGuard { get; set; } = true;

        // Re-route a responder that has been sitting behind a blocker for a while — vanilla only prices congestion
        // at dispatch time and never re-evaluates the route en route.
        [SettingsUISection(Section, Group)]
        public bool AutoReroute { get; set; } = true;

        // Seconds a responder must be continuously blocked before it looks for a new route (sim-time seconds).
        [SettingsUISlider(min = 2f, max = 30f, step = 1f, unit = "integer")]
        [SettingsUISection(Section, Group)]
        public int RerouteAfterSeconds { get; set; } = 5;

        public override void SetDefaults()
        {
            Enabled = true;
            DespawnGuard = true;
            AutoReroute = true;
            RerouteAfterSeconds = 5;
        }
    }
}
