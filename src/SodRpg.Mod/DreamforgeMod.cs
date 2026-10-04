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
        private PerformanceTuner _performance;
        private MobModelSession _mobModels;
        private bool _hasFocus = true;
        private EventSystem _pausedEventSystem;

        private void Awake()
        {
            try
            {
                instance.isAlteringGameplay = true;
                Loc.Japanese = config.japanese;
                // Install the generated star maps and their migration rules before any profile is loaded or any build is computed.
                StarClusters.RegisterAllGenerated();
                _performance = new PerformanceTuner();
                _performance.Start(config, _hasFocus);
                _mobModels = new MobModelSession(mod?.path, config.customMobModels);
                string dir = Path.Combine(Application.persistentDataPath, "QuickSave", "Mods", "DreamforgeRPG");
                _ui = null;
                _session = new ClientSession(dir, e => _ui?.Notify(e));
                RelicIcons.Init(mod?.path);
                RelicIcons.Preload();
                _ui = new DreamforgeUi(_session, () => config);
                _host = new HostAuthority(() => _session?.Profile.Run?.DailyId ?? DailyDream.Today.Id);
                _session.FirstLaunch();
                _perfLogEnabled = File.Exists(Path.Combine(dir, "perf.flag"));
                _devCommands = File.Exists(Path.Combine(dir, "dev.flag"));
                if (_perfLogEnabled) Log.Info("perf logging enabled (perf.flag)");
                harmony.PatchAll(typeof(DreamforgeMod).Assembly);
                Log.Info($"Loaded {mod.metadata.id} {mod.metadata.modVer}. Profile: {_session.SavePath}");
                HostAuthority.ModVersion = mod.metadata.modVer ?? "?";
            }
            catch (Exception ex)
            {
                Log.Error("Awake failed: " + ex);
            }
        }

        public override void OnConfigChanged()
        {
            Loc.Japanese = config.japanese;
            _performance?.Configure(config);
            _mobModels?.Configure(config.customMobModels);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _hasFocus = hasFocus;
            _performance?.SetFocus(hasFocus);
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
            _mobModels?.Tick();
            try
            {
                _host?.Tick();
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
            if (_perfLogEnabled && Time.unscaledTime >= _nextPerfLog)
            {
                _nextPerfLog = Time.unscaledTime + 10f;
                Debug.Log("[DreamforgeRPG] perf " + _perf.Report() +
                    $" | lightweight={_performance?.Mode ?? config.lightweight} background={!_hasFocus}");
            }
        }

        // 計測用：保存先に perf.flag があるときだけ、10秒ごとに処理時間をログへ書く（通常は何もしない）。
        private bool _perfLogEnabled;
        private float _nextPerfLog;

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
            if (_ui == null) return;
            if (Pressed(kb, config.menuKey)) _ui.Toggle();
            if (_ui.Open && kb.escapeKey.wasPressedThisFrame) _ui.Close();
            var run = _session.Profile.Run;
            // 確保地点の画面は、隠して戦場を見たり、また出したりを何度でもできる（ゾーンを進むたびに出し直しになる）。
            if (run != null && run.AwaitingChoice && _session.ActiveRunId != null && Pressed(kb, config.securePanelKey)) _ui.ToggleSecurePanel();
            if (run != null && run.AwaitingChoice && _session.ActiveRunId != null && !_session.HasPendingTrades)
            {
                if (Pressed(kb, config.secureKey)) _ui.SetStatus(_session.Secure());
                else if (Pressed(kb, config.delveKey)) _ui.SetStatus(_session.Delve());
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

        private bool _devCommands;

        /// <summary>確認用コマンドが使えるか。使えないときは、黙らずにコンソールへ一言出す（何も起きない理由が分かるように）。</summary>
        private bool DevAllowed()
        {
            if (_devCommands) return true;
            Debug.Log("[DreamforgeRPG] This command is not available.");
            return false;
        }

        [ConsoleCommand("Dreamforge: show time spent by this mod per frame (Update / OnGUI / save)", "dreamforge_perf")]
        private void PerfCommand()
        {
            Debug.Log("[DreamforgeRPG] " + _perf.Report() + " | save avg " + _session.SaveMsAverage.ToString("0.00") +
                $"ms (main thread) | lightweight={_performance?.Mode ?? config.lightweight} background={!_hasFocus}");
        }

        [ConsoleCommand("Dreamforge (test): switch lightweight rendering 0=Off 1=Light 2=Strong 3=Max", "dreamforge_lightweight")]
        private void LightweightCommand(int mode)
        {
            config.lightweight = (LightweightMode)Math.Max(0, Math.Min(3, mode));
            _performance?.Configure(config);
            Debug.Log("[DreamforgeRPG] lightweight=" + config.lightweight);
        }

        [ConsoleCommand("Dreamforge (test): travel to the nearest connected combat node (prefers unvisited)", "dreamforge_travelnext")]
        private void TravelNextCommand()
        {
            if (!DevAllowed()) return;
            try
            {
                var zm = NetworkedManagerBase<ZoneManager>.softInstance;
                if (zm == null)
                {
                    Debug.Log("[DreamforgeRPG] travel: ZoneManager unavailable");
                    return;
                }

                int cur = zm.currentNodeIndex;
                int target = -1;
                for (int i = 0; i < zm.nodes.Count; i++)
                {
                    if (i == cur || zm.nodes[i].type == WorldNodeType.ExitBoss || !zm.IsNodeConnected(cur, i))
                        continue;

                    if (target < 0) target = i;
                    if (zm.nodes[i].type == WorldNodeType.Combat && zm.nodes[i].status != WorldNodeStatus.HasVisited)
                    {
                        target = i;
                        break;
                    }
                }

                if (target < 0)
                {
                    Debug.Log("[DreamforgeRPG] travel: no connected destination");
                    return;
                }

                zm.CmdTravelToNode(target);
                Debug.Log("[DreamforgeRPG] travel -> " + target + " " + zm.nodes[target].type);
            }
            catch (Exception ex)
            {
                Log.Error("[DreamforgeRPG] travel: " + ex);
            }
        }

        [ConsoleCommand("Dreamforge (test): set URP render scale (0.25-2.0) to emulate a weaker GPU", "dreamforge_renderscale")]
        private void RenderScaleCommand(float scale)
        {
            try
            {
                var asset = URPUnlocker.API.URPUnlockerAPI.CurrentUnlockedURPAsset;
                if (asset == null)
                {
                    Debug.Log("[DreamforgeRPG] renderScale: URP asset unavailable");
                    return;
                }
                asset.Quality.RenderScale = Math.Max(0.25f, Math.Min(2.0f, scale));
                Debug.Log("[DreamforgeRPG] renderScale=" + asset.Quality.RenderScale);
            }
            catch (Exception ex)
            {
                Log.Error("[DreamforgeRPG] renderScale: " + ex);
            }
        }

        private void OnApplicationQuit()
        {
            _session?.SaveNow();
            _session?.FlushSaves();
        }

        private void OnDestroy()
        {
            // 未精算の報酬と遠征の結果を保存してから、登録・補正・パッチを外す。
            try { _mobModels?.Dispose(); } catch (Exception ex) { Log.Error("Mob models dispose: " + ex); }
            try { RelicIcons.Dispose(); } catch (Exception ex) { Log.Error("Icons dispose: " + ex); }
            try { _performance?.Dispose(); } catch (Exception ex) { Log.Error("Performance dispose: " + ex); }
            try { _session?.Unwire(); } catch (Exception ex) { Log.Error("Unwire: " + ex); }
            try { _host?.Detach(); } catch (Exception ex) { Log.Error("Detach: " + ex); }
            BlockInputWhileMenuOpen.MenuOpen = false;
            BlockGameUi(false);
            try { _ui?.Dispose(); } catch (Exception ex) { Log.Error("UI dispose: " + ex); }
            try { harmony.UnpatchAll(harmony.Id); } catch (Exception ex) { Log.Error("Unpatch: " + ex); }
        }

        [ConsoleCommand("Dreamforge (test): add star map points for this session only (0-300, 0 = off)", "dreamforge_testpoints")]
        private void TestPointsCommand(int points)
        {
            if (!DevAllowed()) return;
            // 保存しない。ゲームを終えれば元に戻る。設定画面には置かない（誰でも触れる所に置かない）。
            Profile.TestBonusPoints = Math.Max(0, Math.Min(StarProgression.MaxPoints, points));
            _ui.Notify(new GameEvent(EventKind.LevelUp, "[debug] star map points +" + Profile.TestBonusPoints));
        }

        [ConsoleCommand("Dreamforge: give relics for testing (count, rarity 0-4)", "dreamforge_give")]
        private void GiveCommand(int count, int rarity)
        {
            if (!DevAllowed()) return;
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

        [ConsoleCommand("Dreamforge: give legendaries whose id starts with a prefix for testing (e.g. set.cinder)", "dreamforge_giveunique")]
        private void GiveUniqueCommand(string prefix)
        {
            if (!DevAllowed()) return;
            var p = _session.Profile;
            var rng = p.TakeRng();
            int given = 0;
            foreach (var u in Content.Uniques)
            {
                if (string.IsNullOrEmpty(prefix) || !u.Id.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var r = Loot.RollUnique(rng, u, Math.Max(1, p.BestItemLevel));
                given++;
                if (p.Run != null) p.Run.Satchel.Add(r);
                else p.Stash.Add(r);
                _ui.Notify(new GameEvent(EventKind.Drop, "[debug] " + r.DisplayName, r.Rarity));
            }
            p.StoreRng(rng);
            Debug.Log($"[DreamforgeRPG] giveunique '{prefix}': {given} item(s) added to the {(p.Run != null ? "satchel" : "stash")}.");
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
            if (!DevAllowed()) return;
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
            if (!DevAllowed()) return;
            if (_session.Profile.Run == null) { Debug.Log("[DreamforgeRPG] no run"); return; }
            int lvl = Math.Max(1, NetworkedManagerBase<GameManager>.softInstance?.ambientLevel ?? 1);
            for (int i = 0; i < Math.Max(1, Math.Min(count, 500)); i++)
                foreach (var e in Rules.OnKill(_session.Profile, (MonsterTier)Math.Max(0, Math.Min(3, tier)), lvl, heroKey: ClientSession.HeroKeyOf(_session.LocalHero), trades: _session.Trades)) _ui.Notify(e);
            _session.MarkDirty(true);
        }

        [ConsoleCommand("Dreamforge (test): equip the newest stash relic of every slot on your traveler", "dreamforge_equipnew")]
        private void EquipNewCommand()
        {
            if (!DevAllowed()) return;
            var p = _session.Profile;
            string hero = ClientSession.HeroKeyOf(_session.LocalHero);
            if (hero == null) { Debug.Log("[DreamforgeRPG] no local hero"); return; }
            foreach (var slot in Content.SlotOrder)
            {
                Relic newest = null;
                foreach (var r in p.Stash) if (r.Slot == slot) newest = r;
                if (newest != null) Rules.Equip(p, hero, newest.Uid, _session.Trades);
            }
            _session.MarkDirty(true);
            Debug.Log("[DreamforgeRPG] equipped newest relics on " + hero);
        }

        [ConsoleCommand("Dreamforge (test): end the current run in the mod (1 = victory, 0 = defeat)", "dreamforge_endrun")]
        private void EndRunCommand(int victory)
        {
            if (!DevAllowed()) return;
            foreach (var e in Rules.EndRun(_session.Profile, victory != 0, _session.Trades.ReservedSalvageUids())) _ui.Notify(e);
            _session.MarkDirty(true);
            _session.SaveNow();
        }

        [ConsoleCommand("Dreamforge (test): open a secure point now", "dreamforge_securepoint")]
        private void SecurePointCommand()
        {
            if (!DevAllowed()) return;
            Rules.ReachSecurePoint(_session.Profile);
        }

        [ConsoleCommand("Dreamforge: show custom mob pack status and exact build tokens", "dreamforge_mobstatus")]
        private void MobStatusCommand()
        {
            Debug.Log("[DreamforgeRPG] mobs=" + (_mobModels?.Status ?? "unavailable")
                + " game=" + Application.version + " unity=" + Application.unityVersion
                + " target=" + MobModelAssets.CurrentTarget());
        }

        [ConsoleCommand("Dreamforge: show profile summary", "dreamforge_status")]
        private void StatusCommand()
        {
            var p = _session.Profile;
            Debug.Log($"[DreamforgeRPG] Lv{p.DreamLevel} xp{p.DreamXp} stash{p.Stash.Count} shards{p.Material(Materials.Shard)} run={(p.Run != null ? p.Run.RunId + " heat" + p.Run.Heat + " satchel" + p.Run.Satchel.Count : "none")} active={_session.ActiveRunId} hostOk={_session.HostConfirmed} hostActive={_host.IsActive}");
        }
    }
}
