using System;
using System.IO;
using SodRpg.Core.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SodRpg.Mod
{
    using Line = SodRpg.Core.Game.Line;
    using Power = SodRpg.Core.Game.Power;
    using Rarity = SodRpg.Core.Game.Rarity;
    using Slot = SodRpg.Core.Game.Slot;
    using Stat = SodRpg.Core.Game.Stat;

    /// <summary>
    /// MODの入口。ゲームの ModBehaviour として読み込まれ、各PCの ClientSession と、ホスト時の HostAuthority を動かす。
    /// ゲームプレイを変えるため isAlteringGameplay を立てる（ロビーにMODアイコンが出る）。
    /// </summary>
    public class DreamforgeMod : ModBehaviour
    {
        public DreamforgeConfig config = new DreamforgeConfig();

        private ClientSession _session;
        private HostAuthority _host;
        private DreamforgeUi _ui;
        private EventSystem _pausedEventSystem;

        private void Awake()
        {
            try
            {
                instance.isAlteringGameplay = true;
                Loc.Japanese = config.japanese;
                string dir = Path.Combine(Application.persistentDataPath, "QuickSave", "Mods", "DreamforgeRPG");
                _ui = null;
                _session = new ClientSession(dir, e => _ui?.Notify(e));
                _ui = new DreamforgeUi(_session, () => config);
                _host = new HostAuthority(() => _session?.Profile.Run?.DailyId ?? DailyDream.Today.Id);
                _session.FirstLaunch();
                harmony.PatchAll(typeof(DreamforgeMod).Assembly);
                Log.Info($"Loaded {mod.metadata.id} {mod.metadata.modVer}. Profile: {_session.SavePath}");
            }
            catch (Exception ex)
            {
                Log.Error("Awake failed: " + ex);
            }
        }

        public override void OnConfigChanged()
        {
            Loc.Japanese = config.japanese;
        }

        private readonly PerfMeter _perf = new PerfMeter();

        private void Update()
        {
            if (_session == null) return;
            _perf.Frame(Time.unscaledDeltaTime);
            _perf.Begin();
            HandleKeys();
            bool block = _ui != null && (_ui.Open || _ui.MouseOverPanel);
            BlockInputWhileMenuOpen.MenuOpen = block;
            BlockGameUi(block);
            _session.Tick();
            try
            {
                _host.Tick();
            }
            catch (Exception ex)
            {
                Log.Error("Host tick: " + ex);
            }
            // GUILayout を使うパネルが無いときは、IMGUI のレイアウト処理（OnGUI の Layout イベント）自体を止める。
            if (_ui != null)
            {
                bool layout = _ui.NeedsLayout;
                _ui.LayoutEnabled = layout;
                useGUILayout = layout;
            }
            _perf.EndUpdate();
        }

        /// <summary>メニュー操作中はゲーム側のUI（uGUI）にクリックを渡さない。</summary>
        private void BlockGameUi(bool block)
        {
            if (block)
            {
                var es = EventSystem.current;
                if (es != null && es.enabled && _pausedEventSystem == null)
                {
                    es.enabled = false;
                    _pausedEventSystem = es;
                }
            }
            else if (_pausedEventSystem != null)
            {
                _pausedEventSystem.enabled = true;
                _pausedEventSystem = null;
            }
        }

        private void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (Pressed(kb, config.menuKey)) _ui.Toggle();
            if (_ui.Open && kb.escapeKey.wasPressedThisFrame) _ui.Close();
            var run = _session.Profile.Run;
            if (run != null && run.AwaitingChoice && _session.ActiveRunId != null)
            {
                if (Pressed(kb, config.secureKey)) _session.Secure();
                else if (Pressed(kb, config.delveKey)) _session.Delve();
            }
        }

        private static bool Pressed(Keyboard kb, Key key)
        {
            if (key == Key.None) return false;
            try
            {
                return kb[key].wasPressedThisFrame;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private void OnGUI()
        {
            if (_ui == null) return;
            _perf.Begin();
            _ui.Draw();
            _perf.EndGui();
        }

        [ConsoleCommand("Dreamforge: show time spent by this mod per frame (Update / OnGUI / save)", "dreamforge_perf")]
        private void PerfCommand()
        {
            Debug.Log("[DreamforgeRPG] " + _perf.Report() + " | save avg " + _session.SaveMsAverage.ToString("0.00") + "ms (main thread)");
        }

        private void OnApplicationQuit()
        {
            _session?.SaveNow();
            _session?.FlushSaves();
        }

        private void OnDestroy()
        {
            // ライブリロードに備え、付けた補正・登録・パッチをすべて外してから保存する。
            try { _host?.Detach(); } catch (Exception ex) { Log.Error("Detach: " + ex); }
            try { _session?.Unwire(); } catch (Exception ex) { Log.Error("Unwire: " + ex); }
            try
            {
                _session?.SaveNow();
                _session?.FlushSaves();
            }
            catch (Exception ex) { Log.Error("Save on destroy: " + ex); }
            BlockInputWhileMenuOpen.MenuOpen = false;
            BlockGameUi(false);
            try { _ui?.Dispose(); } catch (Exception ex) { Log.Error("UI dispose: " + ex); }
            try { harmony.UnpatchAll(harmony.Id); } catch (Exception ex) { Log.Error("Unpatch: " + ex); }
        }

        [ConsoleCommand("Dreamforge: give relics for testing (count, rarity 0-4)", "dreamforge_give")]
        private void GiveCommand(int count, int rarity)
        {
            var p = _session.Profile;
            var rng = p.TakeRng();
            for (int i = 0; i < Math.Max(1, Math.Min(count, 20)); i++)
            {
                var r = Loot.RollRelic(rng, (Rarity)Math.Max(0, Math.Min(4, rarity)), Math.Max(1, p.BestItemLevel));
                if (p.Run != null) p.Run.Satchel.Add(r);
                else p.Stash.Add(r);
                _ui.Notify(new GameEvent(EventKind.Drop, "[debug] " + r.DisplayName, r.Rarity));
            }
            p.StoreRng(rng);
            _session.MarkDirty(true);
            _session.SaveNow();
        }

        [ConsoleCommand("Dreamforge: log your hero's final stats and the bonus applied by this mod", "dreamforge_stats")]
        private void StatsCommand()
        {
            var hero = _session.LocalHero;
            if (hero == null)
            {
                Debug.Log("[DreamforgeRPG] no local hero");
                return;
            }
            var st = hero.Status;
            var b = st.bonusStats;
            Debug.Log($"[DreamforgeRPG] {ClientSession.HeroKeyOf(hero)} HP {hero.currentHealth:0}/{st.maxHealth:0} AD {st.attackDamage:0.0} AP {st.abilityPower:0.0} " +
                $"crit {st.critChance:0.00} armor {st.armor:0.0} | bonus AD% {b.attackDamagePercentage:0.#} AS% {b.attackSpeedPercentage:0.#} HP {b.maxHealthFlat:0.#}+{b.maxHealthPercentage:0.#}% armor {b.armorFlat:0.#} haste {b.abilityHasteFlat:0.#} move% {b.movementSpeedPercentage:0.#}");
            Debug.Log("[DreamforgeRPG] build: " + _session.CurrentBuild(ClientSession.HeroKeyOf(hero)).Encode() + " hostConfirmed=" + _session.HostConfirmed);
        }

        [ConsoleCommand("Dreamforge (host test): kill enemies within radius to test drops", "dreamforge_killnear")]
        private void KillNearCommand(float radius)
        {
            if (!MakeSureServer()) return;
            var hero = _session.LocalHero;
            if (hero == null) return;
            ListReturnHandle<Entity> handle;
            var list = DewPhysics.OverlapCircleAllEntities(out handle, hero.agentPosition, Mathf.Clamp(radius, 1f, 60f),
                (Func<Entity, bool>)(e => e is Monster && e.isActive && e.GetRelation(hero) == EntityRelation.Enemy));
            var targets = new System.Collections.Generic.List<Entity>(list);
            handle.Return();
            foreach (var e in targets) e.Kill();
            Debug.Log($"[DreamforgeRPG] killed {targets.Count} enemies");
        }

        [ConsoleCommand("Dreamforge (test): simulate kills of a tier (0 lesser,1 normal,2 miniboss,3 boss)", "dreamforge_simkill")]
        private void SimKillCommand(int tier, int count)
        {
            if (_session.Profile.Run == null) { Debug.Log("[DreamforgeRPG] no run"); return; }
            int lvl = Math.Max(1, NetworkedManagerBase<GameManager>.instance?.ambientLevel ?? 1);
            for (int i = 0; i < Math.Max(1, Math.Min(count, 500)); i++)
                foreach (var e in Rules.OnKill(_session.Profile, (MonsterTier)Math.Max(0, Math.Min(3, tier)), lvl)) _ui.Notify(e);
            _session.MarkDirty(false);
        }

        [ConsoleCommand("Dreamforge (test): end the current run in the mod (1 = victory, 0 = defeat)", "dreamforge_endrun")]
        private void EndRunCommand(int victory)
        {
            foreach (var e in Rules.EndRun(_session.Profile, victory != 0)) _ui.Notify(e);
            _session.MarkDirty(true);
            _session.SaveNow();
        }

        [ConsoleCommand("Dreamforge (test): open a secure point now", "dreamforge_securepoint")]
        private void SecurePointCommand()
        {
            Rules.ReachSecurePoint(_session.Profile);
        }

        [ConsoleCommand("Dreamforge: show profile summary", "dreamforge_status")]
        private void StatusCommand()
        {
            var p = _session.Profile;
            Debug.Log($"[DreamforgeRPG] Lv{p.DreamLevel} xp{p.DreamXp} stash{p.Stash.Count} shards{p.Material(Materials.Shard)} run={(p.Run != null ? p.Run.RunId + " heat" + p.Run.Heat + " satchel" + p.Run.Satchel.Count : "none")} active={_session.ActiveRunId} hostOk={_session.HostConfirmed} hostActive={_host.IsActive}");
        }
    }
}
