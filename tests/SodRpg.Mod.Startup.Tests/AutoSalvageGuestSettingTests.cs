using System;
using System.IO;
using System.Reflection;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // #1049: 協力プレイに接続中のゲストが記録タブで自動分解の設定を変えたとき、変更がすぐ
    // セッションの実効設定へ渡ること（ConfigureAutoSalvage。本物の ClientSession では
    // _autoSalvageBelow を更新し、QueueAutoSalvage / TickAutoSalvage はこの値を使う）と、
    // 設定が保存されること（SaveConfigsToDisk）を、本物の DreamforgeMod に対して確かめる。
    // 記録タブ（DreamforgeUi.cs の DrawAutoSalvageSettings）は cfg.autoSalvageRarity を書き換えてから
    // 保存コールバック（現状は SaveLevelEssenceConfig。Start が DreamforgeUi へ渡す）を呼ぶので、
    // ここでは同じ流れを再現する。修正で個人設定の保存コールバックを別のメソッドへ分ける場合は、
    // ChangeRecordsTabSetting の Invoke 先をそのメソッドへ切り替えてよい。この試験が固定するのは
    // 挙動（実効設定への反映と保存）であって、コールバックの名前ではない。
    public sealed class AutoSalvageGuestSettingTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "dreamforge-auto-salvage-guest-" + Guid.NewGuid().ToString("N"));
        private readonly DreamforgeMod _mod = new DreamforgeMod();
        private readonly ClientSession _session = new ClientSession(null, null);
        private readonly bool _server = Mirror.NetworkServer.active;
        private readonly bool _client = Mirror.NetworkClient.active;
        private string _configPath;

        public AutoSalvageGuestSettingTests()
        {
            Directory.CreateDirectory(_dir);
            Log.Errors.Clear();
            Log.Warnings.Clear();
            Log.Infos.Clear();
            _configPath = Path.Combine(_dir, "config.json");
            _mod.configFilePath = _ => _configPath;
            // Start() は重いため呼ばない。Start が _session へ行うのと同じ形で、セッションだけ差し替える。
            typeof(DreamforgeMod).GetField("_session", Hidden).SetValue(_mod, _session);
        }

        /// <summary>保存値（起動時の設定）を読み込む。「自動分解を有効にした状態で起動する」に相当。</summary>
        private void BootWith(AutoSalvageRarity saved)
        {
            File.WriteAllText(_configPath, "{\"autoSalvageRarity\":" + (int)saved + "}");
            _mod.LoadConfigsToDisk();
            Assert.Equal(saved, _mod.config.autoSalvageRarity);
        }

        /// <summary>記録タブでの変更（cfg を書き換えてから保存コールバックを呼ぶ。DreamforgeUi.cs と同じ順）。</summary>
        private void ChangeRecordsTabSetting(AutoSalvageRarity choice)
        {
            _mod.config.autoSalvageRarity = choice;
            try { typeof(DreamforgeMod).GetMethod("SaveLevelEssenceConfig", Hidden).Invoke(_mod, null); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        [Fact]
        public void Guest_turning_auto_salvage_off_applies_the_session_and_saves()
        {
            BootWith(AutoSalvageRarity.BelowEpic); // 自動分解を有効にした状態で起動する
            Mirror.NetworkServer.active = false;
            Mirror.NetworkClient.active = true; // 協力プレイにゲストとして接続中
            ChangeRecordsTabSetting(AutoSalvageRarity.None); // 記録タブで「しない」へ変更する
            // 変更直後に実効設定が変わり（以降の新規ドロップも待機中の候補も分解されない）、設定も保存される。
            Assert.True(_session.AutoSalvageApplied == AutoSalvageRarity.None && _mod.SaveConfigsToDiskCalls >= 1,
                "guest On->Off must apply to the session and save: applied=" + _session.AutoSalvageApplied
                + ", saveCalls=" + _mod.SaveConfigsToDiskCalls);
        }

        [Fact]
        public void Guest_turning_on_and_changing_the_threshold_apply_immediately()
        {
            BootWith(AutoSalvageRarity.None);
            Mirror.NetworkServer.active = false;
            Mirror.NetworkClient.active = true;
            ChangeRecordsTabSetting(AutoSalvageRarity.BelowRare); // Off→On
            Assert.Equal(AutoSalvageRarity.BelowRare, _session.AutoSalvageApplied);
            ChangeRecordsTabSetting(AutoSalvageRarity.BelowLegendary); // 閾値を上げる
            Assert.Equal(AutoSalvageRarity.BelowLegendary, _session.AutoSalvageApplied);
        }

        [Fact]
        public void Host_and_offline_changes_still_apply_and_save()
        {
            BootWith(AutoSalvageRarity.BelowEpic);
            // ホストとして接続中（サーバとクライアントの両方が動く）。
            Mirror.NetworkServer.active = true;
            Mirror.NetworkClient.active = true;
            ChangeRecordsTabSetting(AutoSalvageRarity.None);
            Assert.Equal(AutoSalvageRarity.None, _session.AutoSalvageApplied);
            Assert.True(_mod.SaveConfigsToDiskCalls >= 1);
            // オフライン（どちらも動いていない）でも、これまでどおり反映と保存が起きる。
            Mirror.NetworkServer.active = false;
            Mirror.NetworkClient.active = false;
            ChangeRecordsTabSetting(AutoSalvageRarity.BelowRare);
            Assert.Equal(AutoSalvageRarity.BelowRare, _session.AutoSalvageApplied);
        }

        [Fact]
        public void Guest_change_does_not_touch_host_only_level_essence_settings()
        {
            BootWith(AutoSalvageRarity.BelowEpic);
            Mirror.NetworkServer.active = false;
            Mirror.NetworkClient.active = true;
            _mod.config.levelEssenceEveryLevels = 0; // 範囲外（1〜99）
            _mod.config.levelEssenceCap = 99; // 範囲外（4〜8）
            ChangeRecordsTabSetting(AutoSalvageRarity.None);
            // ホスト専用の設定はゲストから正規化も適用もされない（権限の制約を緩めない）。
            Assert.Equal(0, _mod.config.levelEssenceEveryLevels);
            Assert.Equal(99, _mod.config.levelEssenceCap);
            Assert.DoesNotContain(Log.Infos, m => m.Contains("Level essence slots"));
        }

        public void Dispose()
        {
            Mirror.NetworkServer.active = _server;
            Mirror.NetworkClient.active = _client;
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }
}
