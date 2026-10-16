using System;
using Mirror;
using UnityEngine;

namespace SodRpg.Mod
{
    // Use the game's existing SyncDictionary; no new MOD message or protocol version.
    internal static class AutoCastCoopSettings
    {
        private const string SettingsKey = "Dreamforge.AutoCastCoop.v1";
        private static bool _nativeCadence = true;
        private static float _nextPublish;
        private static bool _warned;

        internal static bool NativeCadence => _nativeCadence;

        internal static bool DisplayNativeCadence
        {
            get
            {
                if (!NetworkClient.active || NetworkServer.active) return _nativeCadence;
                try
                {
                    var manager = NetworkedManagerBase<GameSettingsManager>.softInstance;
                    return manager == null || !manager.customData.TryGetValue(SettingsKey, out string value) || value != "0";
                }
                catch (Exception ex) { Warn(ex); return true; }
            }
        }

        internal static void Configure(bool enabled)
        {
            _nativeCadence = enabled;
            _nextPublish = 0f;
            Tick();
        }

        internal static void Tick()
        {
            if (!NetworkServer.active || Time.unscaledTime < _nextPublish) return;
            _nextPublish = Time.unscaledTime + 1f;
            try
            {
                var manager = NetworkedManagerBase<GameSettingsManager>.softInstance;
                if (manager == null) return;
                string value = _nativeCadence ? "1" : "0";
                if (!manager.customData.TryGetValue(SettingsKey, out string old) || old != value)
                    manager.customData[SettingsKey] = value;
            }
            catch (Exception ex) { Warn(ex); }
        }

        private static void Warn(Exception ex)
        {
            if (_warned) return;
            _warned = true;
            Log.Warn("Automatic casting setting display could not be shared; host casting remains active: " + ex.Message);
        }
    }
}
