using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    internal sealed partial class HostAuthority
    {
        private bool _specialEnemyDiagnostics;
        internal bool SpecialEnemyDiagnostics
        {
            get => _specialEnemyDiagnostics;
            set { _specialEnemyDiagnostics = value; _loggedDeathBurst = !value; }
        }
        private int _roomNightmareCount, _roomNemesisCount, _roomVariantCount;
        private NightmareAffix _roomNightmareKinds;
        private bool _specialEnemyLogFailed;

        partial void FlushSpecialEnemySummary()
        {
            if (_roomNightmareCount == 0 && _roomNemesisCount == 0 && _roomVariantCount == 0) return;
            int nightmares = _roomNightmareCount, nemeses = _roomNemesisCount, variants = _roomVariantCount;
            var kinds = _roomNightmareKinds;
            _roomNightmareCount = _roomNemesisCount = _roomVariantCount = 0;
            _roomNightmareKinds = NightmareAffix.None;
            if (_specialEnemyLogFailed) return;
            try
            {
                Log.Info($"Host: special enemies room summary: nightmares={nightmares} affixes={kinds} nemeses={nemeses} variants={variants}");
            }
            catch (System.Exception ex)
            {
                _specialEnemyLogFailed = true;
                try { Log.Warn("Host: special enemy summary disabled; combat continues: " + ex.Message); }
                catch (System.Exception) { }
            }
        }
    }
}
