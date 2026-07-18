using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Simulation;

namespace EmergencyPriority
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(EmergencyPriority)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting ActiveSetting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            ActiveSetting = new Setting(this);
            ActiveSetting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEn(ActiveSetting));
            AssetDatabase.global.LoadSettings(nameof(EmergencyPriority), ActiveSetting, new Setting(this));
            // Belt-and-suspenders: write every settings change straight to disk the moment it's applied, so a crash or
            // non-clean exit can't lose it (CS2 otherwise only flushes the ModSetting .coc on a clean Quit-to-Desktop).
            ActiveSetting.onSettingsApplied += OnSettingsApplied;

            // After the stuck detector so a freshly raised Stuck flag is converted to a repath before the vehicle
            // AI systems (which run in the same phase) can take their delete branch.
            updateSystem.UpdateAfter<EmergencyRepathSystem, StuckMovingObjectSystem>(SystemUpdatePhase.GameSimulation);
            // Keep platform achievements enabled while the mod is active.
            updateSystem.UpdateAt<AchievementEnablerSystem>(SystemUpdatePhase.GameSimulation);

            log.Info("[SelfTest] EmergencyPriority loaded (despawn guard + auto re-route).");
        }

        // Persist a settings change to disk as soon as it is applied. Guarded because ApplyAndSave re-raises
        // onSettingsApplied, which would otherwise recurse.
        private static bool s_savingReentrant;
        private static void OnSettingsApplied(Game.Settings.Setting setting)
        {
            if (s_savingReentrant)
                return;
            s_savingReentrant = true;
            try { ActiveSetting?.ApplyAndSave(); }
            finally { s_savingReentrant = false; }
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (ActiveSetting != null)
            {
                ActiveSetting.onSettingsApplied -= OnSettingsApplied;
                ActiveSetting.UnregisterInOptionsUI();
                ActiveSetting = null;
            }
        }
    }

    // Minimal English locale (full localization once mechanics are proven, same pipeline as EconomyTweaks).
    public class LocaleEn : IDictionarySource
    {
        private readonly Setting m_S;
        public LocaleEn(Setting setting) { m_S = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_S.GetSettingsLocaleID(), "Emergency Priority" },
                { m_S.GetOptionTabLocaleID(Setting.Section), "Main" },
                { m_S.GetOptionGroupLocaleID(Setting.Group), "Responding vehicles" },
                { m_S.GetOptionLabelLocaleID(nameof(Setting.Enabled)), "Enable Emergency Priority" },
                { m_S.GetOptionDescLocaleID(nameof(Setting.Enabled)), "Keeps responding fire engines, ambulances and police cars alive and moving through traffic jams. Off = vanilla behaviour." },
                { m_S.GetOptionLabelLocaleID(nameof(Setting.DespawnGuard)), "Prevent stuck responders from despawning" },
                { m_S.GetOptionDescLocaleID(nameof(Setting.DespawnGuard)), "Vanilla deletes a responding vehicle once it is flagged as stuck in traffic, and the emergency waits for a re-dispatch. With the guard, the vehicle looks for a new route instead." },
                { m_S.GetOptionLabelLocaleID(nameof(Setting.AutoReroute)), "Re-route around congestion" },
                { m_S.GetOptionDescLocaleID(nameof(Setting.AutoReroute)), "Vanilla plans the route once, at dispatch. With this on, a responder that sits blocked in a jam requests a fresh route that accounts for current traffic." },
                { m_S.GetOptionLabelLocaleID(nameof(Setting.RerouteAfterSeconds)), "Re-route after (seconds blocked)" },
                { m_S.GetOptionDescLocaleID(nameof(Setting.RerouteAfterSeconds)), "How long a responder must be continuously stuck behind traffic before it looks for a detour." },

                { m_S.GetOptionGroupLocaleID(Setting.GroupGeneral), "General" },
                { m_S.GetOptionLabelLocaleID(nameof(Setting.EnableAchievements)), "Keep achievements enabled" },
                { m_S.GetOptionDescLocaleID(nameof(Setting.EnableAchievements)), "Cities: Skylines II disables achievements whenever any mod is active. This re-enables them. Safe to leave on." },
            };
        }

        public void Unload() { }
    }
}
