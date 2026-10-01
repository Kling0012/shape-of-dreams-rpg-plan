using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using URPUnlocker.API;
using URPUnlocker.Core;

namespace SodRpg.Mod
{
    /// <summary>Rendering and background FPS overrides; no per-frame work.</summary>
    internal sealed class PerformanceTuner : IDisposable
    {
        private readonly HashSet<string> _warnings = new HashSet<string>();
        private readonly Dictionary<UnlockedURPAsset, AssetSettings> _assets = new Dictionary<UnlockedURPAsset, AssetSettings>();
        private readonly Dictionary<GraphicsManager, SavedSetting<Quality3Levels?>> _effects = new Dictionary<GraphicsManager, SavedSetting<Quality3Levels?>>();
        private readonly SavedSetting<float> _lodBias;
        private readonly SavedSetting<float> _bakeInterval;
        private readonly Action _settingsChanged;
        private DreamforgeConfig _config;
        private bool _focused = true;
        private bool _disposed;
        private bool _settingsSubscribed, _sceneSubscribed;
        private bool _savedFrameRate, _savedVSync;
        private int _frameRate, _vSync;

        public LightweightMode Mode => _config?.lightweight ?? LightweightMode.Off;

        public PerformanceTuner()
        {
            _lodBias = new SavedSetting<float>(this, "lodBias", () => QualitySettings.lodBias, value => QualitySettings.lodBias = value);
            _bakeInterval = new SavedSetting<float>(this, "BakeMeshInterval", () => Dew.BakeMeshInterval, value => Dew.BakeMeshInterval = value);
            _settingsChanged = Reapply;
        }

        public void Start(DreamforgeConfig config, bool focused)
        {
            _focused = focused;
            try
            {
                DewSave.onSettingsChanged += _settingsChanged;
                _settingsSubscribed = true;
            }
            catch (Exception ex) { WarnOnce("settings notification", ex.Message); }
            try
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                _sceneSubscribed = true;
            }
            catch (Exception ex) { WarnOnce("scene notification", ex.Message); }
            Configure(config);
        }

        public void Configure(DreamforgeConfig config)
        {
            _config = config;
            // The integer UI has a continuous range; normalize its forbidden 1-19 gap.
            int fps = Math.Max(0, Math.Min(60, config.backgroundFps));
            config.backgroundFps = fps > 0 && fps < 20 ? 20 : fps;
            Reapply();
        }

        public void SetFocus(bool focused)
        {
            if (_disposed) return;
            _focused = focused;
            ApplyBackgroundLimit();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Reapply();

        private void Reapply()
        {
            if (_disposed || _config == null) return;
            ApplyRendering();
            // The game's settings application also restores its foreground FPS limit.
            ApplyBackgroundLimit();
        }

        private void ApplyRendering()
        {
            LightweightMode mode = Mode;
            if (mode != LightweightMode.Off)
            {
                RememberCurrentAsset();
                if (mode == LightweightMode.Strong) RememberEffectManager();
            }

            foreach (var settings in _assets.Values) settings.Apply(mode);
            foreach (var entry in _effects)
            {
                if (entry.Key == null) continue;
                if (mode == LightweightMode.Strong) entry.Value.Set(Quality3Levels.Low);
                else entry.Value.Restore();
            }

            if (mode == LightweightMode.Off)
            {
                _lodBias.Restore();
                _bakeInterval.Restore();
            }
            else
            {
                if (_lodBias.Capture()) _lodBias.Set(_lodBias.Original * (mode == LightweightMode.Strong ? 0.5f : 0.7f));
                _bakeInterval.Set(mode == LightweightMode.Strong ? 0.2f : 0.15f);
            }
        }

        private void RememberCurrentAsset()
        {
            try
            {
                var asset = URPUnlockerAPI.CurrentUnlockedURPAsset;
                if (asset == null)
                {
                    WarnOnce("URP asset", "CurrentUnlockedURPAsset is unavailable; skipped until the next notification.");
                    return;
                }
                if (!_assets.ContainsKey(asset)) _assets.Add(asset, new AssetSettings(this, asset));
            }
            catch (Exception ex) { WarnOnce("URP asset", ex.Message); }
        }

        private void RememberEffectManager()
        {
            try
            {
                var manager = ManagerBase<GraphicsManager>.softInstance;
                if (manager == null)
                {
                    WarnOnce("effectQualityOverride", "GraphicsManager is unavailable; skipped until the next notification.");
                    return;
                }
                if (!_effects.ContainsKey(manager))
                {
                    _effects.Add(manager, new SavedSetting<Quality3Levels?>(this, "effectQualityOverride",
                        () => manager.effectQualityOverride, value => manager.effectQualityOverride = value));
                }
            }
            catch (Exception ex) { WarnOnce("effectQualityOverride", ex.Message); }
        }

        private void ApplyBackgroundLimit()
        {
            if (_config == null) return;
            if (_focused || _config.backgroundFps == 0)
            {
                RestoreForegroundLimit();
                return;
            }

            try
            {
                if (!_savedVSync)
                {
                    _vSync = QualitySettings.vSyncCount;
                    _savedVSync = true;
                }
                QualitySettings.vSyncCount = 0;
            }
            catch (Exception ex) { WarnOnce("background vSyncCount", ex.Message); }
            try
            {
                if (!_savedFrameRate)
                {
                    _frameRate = Application.targetFrameRate;
                    _savedFrameRate = true;
                }
                Application.targetFrameRate = _config.backgroundFps;
            }
            catch (Exception ex) { WarnOnce("background targetFrameRate", ex.Message); }
        }

        private void RestoreForegroundLimit()
        {
            if (!_savedFrameRate && !_savedVSync) return;
            try
            {
                var manager = ManagerBase<FPSManager>.softInstance;
                if (manager != null)
                {
                    manager.UpdateFPSLimit();
                    _savedFrameRate = _savedVSync = false;
                    return;
                }
                WarnOnce("FPSManager.UpdateFPSLimit", "FPSManager is unavailable; restoring saved values.");
            }
            catch (Exception ex) { WarnOnce("FPSManager.UpdateFPSLimit", ex.Message); }

            if (_savedVSync)
            {
                try
                {
                    QualitySettings.vSyncCount = _vSync;
                    _savedVSync = false;
                }
                catch (Exception ex) { WarnOnce("background vSyncCount", ex.Message); }
            }
            if (_savedFrameRate)
            {
                try
                {
                    Application.targetFrameRate = _frameRate;
                    _savedFrameRate = false;
                }
                catch (Exception ex) { WarnOnce("background targetFrameRate", ex.Message); }
            }
        }

        private void WarnOnce(string item, string message)
        {
            if (_warnings.Add(item)) Log.Warn("Performance " + item + ": " + message);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_settingsSubscribed)
            {
                try { DewSave.onSettingsChanged -= _settingsChanged; }
                catch (Exception ex) { WarnOnce("settings notification", ex.Message); }
            }
            if (_sceneSubscribed)
            {
                try { SceneManager.sceneLoaded -= OnSceneLoaded; }
                catch (Exception ex) { WarnOnce("scene notification", ex.Message); }
            }
            foreach (var settings in _assets.Values) settings.Apply(LightweightMode.Off);
            foreach (var entry in _effects)
                if (entry.Key != null) entry.Value.Restore();
            _lodBias.Restore();
            _bakeInterval.Restore();
            RestoreForegroundLimit();
        }

        private sealed class AssetSettings
        {
            private readonly SavedSetting<float> _shadowDistance;
            private readonly SavedSetting<int> _cascades, _lights;
            private readonly SavedSetting<MsaaQuality> _msaa;

            public AssetSettings(PerformanceTuner owner, UnlockedURPAsset asset)
            {
                // Cache wrappers once per asset: Shadows allocates on every access.
                try
                {
                    var shadows = asset.Shadows;
                    _shadowDistance = new SavedSetting<float>(owner, "ShadowDistance", () => shadows.ShadowDistance, value => shadows.ShadowDistance = value);
                    _cascades = new SavedSetting<int>(owner, "CascadeCount", () => shadows.CascadeCount, value => shadows.CascadeCount = value);
                }
                catch (Exception ex)
                {
                    owner.WarnOnce("ShadowDistance", ex.Message);
                    owner.WarnOnce("CascadeCount", ex.Message);
                }
                try
                {
                    var lighting = asset.Lighting;
                    _lights = new SavedSetting<int>(owner, "MaxAdditionalLightsCount", () => lighting.MaxAdditionalLightsCount, value => lighting.MaxAdditionalLightsCount = value);
                }
                catch (Exception ex) { owner.WarnOnce("MaxAdditionalLightsCount", ex.Message); }
                try
                {
                    var quality = asset.Quality;
                    _msaa = new SavedSetting<MsaaQuality>(owner, "AntiAliasing", () => quality.AntiAliasing, value => quality.AntiAliasing = value);
                }
                catch (Exception ex) { owner.WarnOnce("AntiAliasing", ex.Message); }
            }

            public void Apply(LightweightMode mode)
            {
                if (mode == LightweightMode.Off)
                {
                    _shadowDistance?.Restore();
                    _cascades?.Restore();
                    _lights?.Restore();
                    _msaa?.Restore();
                    return;
                }
                bool strong = mode == LightweightMode.Strong;
                if (_shadowDistance?.Capture() == true) _shadowDistance.Set(_shadowDistance.Original * (strong ? 0.4f : 0.6f));
                if (_cascades?.Capture() == true) _cascades.Set(strong ? 1 : Math.Min(_cascades.Original, 2));
                if (_lights?.Capture() == true) _lights.Set(Math.Min(_lights.Original, 2));
                if (strong) _msaa?.Set(MsaaQuality.Disabled);
                else _msaa?.Restore();
            }
        }

        /// <summary>One baseline per target/member, captured only before its first override.</summary>
        private sealed class SavedSetting<T>
        {
            private readonly PerformanceTuner _owner;
            private readonly string _name;
            private readonly Func<T> _get;
            private readonly Action<T> _set;
            private bool _captured, _changed;
            public T Original { get; private set; }

            public SavedSetting(PerformanceTuner owner, string name, Func<T> get, Action<T> set)
            {
                _owner = owner;
                _name = name;
                _get = get;
                _set = set;
            }

            public bool Capture()
            {
                if (_captured) return true;
                try
                {
                    Original = _get();
                    _captured = true;
                    return true;
                }
                catch (Exception ex)
                {
                    _owner.WarnOnce(_name, ex.Message);
                    return false;
                }
            }

            public void Set(T value)
            {
                if (!Capture()) return;
                try
                {
                    // Retain restoration responsibility even if a setter partially fails.
                    _changed = true;
                    _set(value);
                }
                catch (Exception ex) { _owner.WarnOnce(_name, ex.Message); }
            }

            public void Restore()
            {
                if (!_changed) return;
                try
                {
                    _set(Original);
                    _changed = false;
                }
                catch (Exception ex) { _owner.WarnOnce(_name, ex.Message); }
            }
        }
    }
}
