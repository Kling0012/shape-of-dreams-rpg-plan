using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using System.IO;
using SodRpg.Core.Game;
using UnityEngine;
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
        private bool _hasFocus = true;
        private bool _running;
        private bool _stopped;
        private bool _iconsStarted;
        private bool _gameplayFlagChanged;
        private bool _originalGameplayFlag;

        private void Awake()
        {
            string stage = "native preflight";
            useGUILayout = false;
            try
            {
                if (harmony == null || string.IsNullOrEmpty(harmony.Id) || harmony.Id == "*")
                    throw new InvalidOperationException("Startup requires a non-wildcard Harmony owner.");
                // Native prerequisites gate only their dependent feature groups. The composed-IL
                // diagnostic is advisory; unrelated classes can still install if its copier fails.
                try { NativePatchPreflight.Validate(harmony, typeof(DreamforgeMod).Assembly); }
                catch (Exception ex) { Log.Warn("Native preflight failed; patching class by class instead: " + ex); }
                stage = "patch installation";
                PatchEachClass();
                stage = "resource initialization";
                Loc.Japanese = config.japanese;
                // Install the generated star maps and their migration rules before any profile is loaded or any build is computed.
                StarClusters.RegisterAllGenerated((hero, error) =>
                    Log.Warn("Generated star map disabled for " + hero + "; other features remain available: " + error));
                _performance = new PerformanceTuner();
                _performance.Start(config, _hasFocus);
                string dir = Path.Combine(Application.persistentDataPath, "QuickSave", "Mods", "DreamforgeRPG");
                _ui = null;
                _session = new ClientSession(dir, e => _ui?.Notify(e));
                _iconsStarted = true;
                RelicIcons.Init(mod?.path);
                RelicIcons.Preload();
                _ui = new DreamforgeUi(_session, () => config);
                _host = new HostAuthority(() => _session?.Profile.Run?.DailyId ?? DailyDream.Today.Id);
                _session.FirstLaunch();
                _perfLogEnabled = File.Exists(Path.Combine(dir, "perf.flag"));
                _devCommands = File.Exists(Path.Combine(dir, "dev.flag"));
                if (_perfLogEnabled) Log.Info("perf logging enabled (perf.flag)");
                Log.Info($"Loaded {mod.metadata.id} {mod.metadata.modVer}. Profile: {_session.SavePath}");
                HostAuthority.ModVersion = mod.metadata.modVer ?? "?";
                _originalGameplayFlag = instance.isAlteringGameplay;
                _gameplayFlagChanged = true;
                instance.isAlteringGameplay = true;
                _running = true;
            }
            catch (Exception ex)
            {
                Log.Error("Startup stopped during " + stage + ": " + ex);
                Stop();
            }
        }

        public override void OnConfigChanged()
        {
            if (!_running) return;
            Loc.Japanese = config.japanese;
            _performance?.Configure(config);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _hasFocus = hasFocus;
            if (!_running) return;
            _performance?.SetFocus(hasFocus);
        }

        private readonly PerfMeter _perf = new PerfMeter();

        private void Update()
        {
            if (!_running) return;
            _perf.Frame(Time.unscaledDeltaTime);
            _perf.Begin();
            HandleKeys();
            bool block = _ui != null && (_ui.Open || _ui.MouseOverPanel);
            BlockInputWhileMenuOpen.MenuOpen = block;
            _session.Tick();
            try
            {
                _host?.Tick();
            }
            catch (Exception ex)
            {
                // HostAuthority.Tick の内部は工程単位で守られている。ここは登録までの保険で、
                // 毎フレーム例外が出てもログがあふれないように間引く（#74）。
                if (Time.unscaledTime >= _nextHostTickErrorLog)
                {
                    _nextHostTickErrorLog = Time.unscaledTime + 10f;
                    Log.Error("Host tick: " + ex);
                }
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

        private float _nextHostTickErrorLog;

        // 計測用：保存先に perf.flag があるときだけ、10秒ごとに処理時間をログへ書く（通常は何もしない）。
        private bool _perfLogEnabled;
        private float _nextPerfLog;

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
            if (!_running) return;
            _perf.Begin();
            _session?.DrawBossEffects();
            _ui.Draw();
            _perf.EndGui();
        }

        private bool _devCommands;

        /// <summary>確認用コマンドが使えるか。使えないときは、黙らずにコンソールへ一言出す（何も起きない理由が分かるように）。</summary>
        private bool DevAllowed()
        {
            if (!CommandAllowed()) return false;
            if (_devCommands) return true;
            Debug.Log("[DreamforgeRPG] This command is not available.");
            return false;
        }

        [ConsoleCommand("Dreamforge: show time spent by this mod per frame (Update / OnGUI / save)", "dreamforge_perf")]
        private void PerfCommand()
        {
            if (!CommandAllowed()) return;
            Debug.Log("[DreamforgeRPG] " + _perf.Report() + " | save avg " + _session.SaveMsAverage.ToString("0.00") +
                $"ms (main thread) | lightweight={_performance?.Mode ?? config.lightweight} background={!_hasFocus}");
        }

        [ConsoleCommand("Dreamforge: give up trades whose result the host could not confirm (no reward is granted; reserved relics return)", "dreamforge_trades_giveup")]
        private void TradesGiveUpCommand()
        {
            if (!CommandAllowed()) return;
            int n = _session.GiveUpLostTrades();
            Debug.Log("[DreamforgeRPG] gave up " + n + " unconfirmed trade(s)");
        }

        [ConsoleCommand("Dreamforge (test): switch lightweight rendering 0=Off 1=Light 2=Strong 3=Max", "dreamforge_lightweight")]
        private void LightweightCommand(int mode)
        {
            if (!CommandAllowed()) return;
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
                bool infinity = InfinityMode.Enabled;
                int target = infinity ? InfinityMode.RevealedNext(zm) : -1;
                for (int i = 0; !infinity && i < zm.nodes.Count; i++)
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
            if (!CommandAllowed()) return;
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
            if (!_running) return;
            _session?.SaveNow();
            _session?.FlushSaves();
        }

        private void OnDestroy()
        {
            Stop();
        }

        private bool CommandAllowed()
        {
            if (_running) return true;
            Debug.Log("[DreamforgeRPG] Startup is not active; command unavailable. See the startup error log.");
            return false;
        }

        private void Stop()
        {
            _running = false;
            if (_stopped) return;
            _stopped = true;
            _devCommands = false;
            _perfLogEnabled = false;
            useGUILayout = false;
            // Clear references first so callbacks cannot observe a partially stopped entry point.
            var session = _session; _session = null;
            var host = _host; _host = null;
            var ui = _ui; _ui = null;
            var performance = _performance; _performance = null;
            try { host?.Detach(); } catch (Exception ex) { Log.Error("Startup cleanup: host detach: " + ex); }
            try { session?.Unwire(); } catch (Exception ex) { Log.Error("Startup cleanup: session unwire: " + ex); }
            try { ui?.Dispose(); } catch (Exception ex) { Log.Error("Startup cleanup: UI dispose: " + ex); }
            try { performance?.Dispose(); } catch (Exception ex) { Log.Error("Startup cleanup: performance restore: " + ex); }
            if (_iconsStarted)
            {
                _iconsStarted = false;
                try { RelicIcons.Dispose(); } catch (Exception ex) { Log.Error("Startup cleanup: icons dispose: " + ex); }
            }
            BlockInputWhileMenuOpen.MenuOpen = false;
            if (_gameplayFlagChanged)
            {
                _gameplayFlagChanged = false;
                try { instance.isAlteringGameplay = _originalGameplayFlag; }
                catch (Exception ex) { Log.Error("Startup cleanup: gameplay flag restore: " + ex); }
            }
            // Continue after an individual rollback error; never remove another owner's patches.
            try
            {
                if (harmony != null && !string.IsNullOrEmpty(harmony.Id) && harmony.Id != "*")
                    foreach (var target in new List<MethodBase>(harmony.GetPatchedMethods()))
                        try { harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id); }
                        catch (Exception ex) { Log.Error("Startup cleanup: unpatch " + target.FullDescription() + ": " + ex); }
            }
            catch (Exception ex) { Log.Error("Startup cleanup: patch enumeration: " + ex); }
        }

        /// <summary>Patch classes one at a time. A class that fails is rolled back (only the targets it added) and skipped.</summary>
        private void PatchEachClass()
        {
            int installed = 0;
            int installedInfinity = 0;
            var skipped = new List<string>();
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(DreamforgeMod).Assembly))
            {
                bool infinity = InfinityMode.IsNativePatch(type);
                // #229: the hunter adjustment degrades alone when its own patch cannot install.
                bool hunter = type == typeof(InfinityHunterAdvance);
                if (NativePatchPreflight.TryGetDisabledFeature(type, out var featureName))
                {
                    if (infinity) InfinityMode.DisableFeature("Infinity patch skipped: native feature " + featureName + " is unavailable or unconfirmed: " + type.FullName);
                    skipped.Add(type.FullName);
                    Log.Warn("Patch class skipped: " + type.FullName + ": native feature " + featureName + " is unavailable or unconfirmed.");
                    continue;
                }
                try
                {
                    if (HarmonyMethodExtensions.GetFromType(type).Count == 0)
                    {
                        if (infinity) InfinityMode.DisableFeature("Infinity patch has no native target: " + type.FullName);
                        continue;
                    }
                    var targets = harmony.CreateClassProcessor(type).Patch();
                    if (infinity)
                    {
                        if (targets == null || targets.Count == 0 || !HasInstalledClass(type))
                        {
                            InfinityMode.DisableFeature("Infinity patch was not installed: " + type.FullName);
                            skipped.Add(type.FullName);
                            continue;
                        }
                        installedInfinity++;
                    }
                    if (hunter && (targets == null || targets.Count == 0 || !HasInstalledClass(type)))
                        InfinityMode.DisableHunterAdjustment("Hunter patch was not installed: " + type.FullName);
                    installed++;
                }
                catch (Exception ex)
                {
                    skipped.Add(type.FullName);
                    Log.Warn("Patch class skipped: " + type.FullName + ": " + ex.Message);
                    if (hunter)
                        InfinityMode.DisableHunterAdjustment("Hunter patch installation failed: " + type.FullName + ": " + ex.Message);
                    if (infinity)
                    {
                        InfinityMode.DisableFeature("Infinity patch installation failed: " + type.FullName + ": " + ex.Message);
                        try { RollBackClass(type); }
                        catch (Exception rollback) { Log.Warn("Infinity patch rollback failed; its hooks remain disabled: " + rollback); }
                    }
                    else RollBackClass(type);
                }
            }
            InfinityMode.CompletePatchInstallation(installedInfinity);
            Log.Info($"Patches installed: {installed} classes" + (skipped.Count > 0 ? $", skipped {skipped.Count}: {string.Join(", ", skipped)}" : ""));
        }

        private bool HasInstalledClass(Type type)
        {
            foreach (var target in harmony.GetPatchedMethods())
            {
                var info = Harmony.GetPatchInfo(target);
                if (info == null) continue;
                foreach (var list in new[] { info.Prefixes, info.Postfixes, info.Transpilers, info.Finalizers })
                    foreach (var patch in list)
                        if (patch.owner == harmony.Id && IsSameOrNested(patch.PatchMethod.DeclaringType, type)) return true;
            }
            return false;
        }

        /// <summary>Remove only the patch methods declared by this class (and its nested types), leaving every other patch in place.</summary>
        private void RollBackClass(Type type)
        {
            foreach (var target in new List<MethodBase>(harmony.GetPatchedMethods()))
            {
                var info = Harmony.GetPatchInfo(target);
                if (info == null) continue;
                var mine = new List<MethodInfo>();
                foreach (var list in new[] { info.Prefixes, info.Postfixes, info.Transpilers, info.Finalizers })
                    foreach (var patch in list)
                        if (patch.owner == harmony.Id && IsSameOrNested(patch.PatchMethod.DeclaringType, type)) mine.Add(patch.PatchMethod);
                foreach (var method in mine)
                    try { harmony.Unpatch(target, method); }
                    catch (Exception ex) { Log.Error("Rollback unpatch " + target.FullDescription() + ": " + ex); }
            }
        }

        private static bool IsSameOrNested(Type candidate, Type root)
        {
            for (var t = candidate; t != null; t = t.DeclaringType)
                if (t == root) return true;
            return false;
        }

        [ConsoleCommand("Dreamforge (test): add star map points for this session only (0-500, 0 = off)", "dreamforge_testpoints")]
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
            int excluded = 0;
            foreach (var u in Content.Uniques)
            {
                if (string.IsNullOrEmpty(prefix) || !u.Id.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (BossSets.IsExclusive(u)) { excluded++; continue; }
                var r = Loot.RollUnique(rng, u, Math.Max(1, p.BestItemLevel));
                given++;
                if (p.Run != null) p.Run.Satchel.Add(r);
                else p.Stash.Add(r);
                _ui.Notify(new GameEvent(EventKind.Drop, "[debug] " + r.DisplayName, r.Rarity));
            }
            p.StoreRng(rng);
            Debug.Log($"[DreamforgeRPG] giveunique '{prefix}': {given} item(s) added to the {(p.Run != null ? "satchel" : "stash")}.");
            if (excluded > 0)
            {
                string message = Loc.T(
                    $"ボス限定装備{excluded}点を除外しました。確認用の付与には dreamforge_givebossset を使用してください。",
                    $"Excluded {excluded} boss-exclusive item(s). Use dreamforge_givebossset for development grants.");
                Debug.Log("[DreamforgeRPG] " + message);
                _ui.Notify(new GameEvent(EventKind.Drop, "[debug] " + message));
            }
            _session.MarkDirty(true);
            _session.SaveNow();
        }

        [ConsoleCommand("Dreamforge (test): grant all six boss-set parts by set id or boss type (e.g. boss_demon)", "dreamforge_givebossset")]
        private void GiveBossSetCommand(string setIdOrBossType)
        {
            if (!DevAllowed()) return;
            if (!BossSets.TryGetSet(setIdOrBossType, out var set))
                set = Content.GetSet(setIdOrBossType != null && setIdOrBossType.StartsWith("set.", StringComparison.Ordinal)
                    ? setIdOrBossType : "set." + setIdOrBossType);
            if (set?.BossTypeName == null)
            {
                string message = Loc.T("登録済みのボス限定セットIDまたはボス型名を指定してください。",
                    "Specify a registered boss-exclusive set id or boss type name.");
                Debug.Log("[DreamforgeRPG] " + message);
                _ui.Notify(new GameEvent(EventKind.Info, "[debug] " + message));
                return;
            }
            var p = _session.Profile;
            var rng = p.TakeRng();
            foreach (var unique in Content.Uniques)
                if (unique.SetId == set.Id)
                    GrantDeveloperBossRelic(p, Loot.RollUnique(rng, unique, Math.Max(1, p.BestItemLevel)));
            p.StoreRng(rng);
            _session.MarkDirty(true);
            _session.SaveNow();
        }

        [ConsoleCommand("Dreamforge (test): roll one exclusive boss drop by boss type during a run", "dreamforge_simbosskill")]
        private void SimBossKillCommand(string bossTypeName)
        {
            if (!DevAllowed()) return;
            var p = _session.Profile;
            if (p.Run == null || !BossSets.TryGetSet(bossTypeName, out _))
            {
                string message = Loc.T("遠征中に登録済みのボス型名を指定してください。",
                    "Specify a registered boss type name during an active run.");
                Debug.Log("[DreamforgeRPG] " + message);
                _ui.Notify(new GameEvent(EventKind.Info, "[debug] " + message));
                return;
            }
            var game = NetworkedManagerBase<GameManager>.softInstance;
            string difficulty = game?.difficulty?.name;
            bool nightmare = difficulty == "diffNightmare" || difficulty == "diffLimbo";
            int depth = ClientSession.HostRun?.DreamDepth ?? p.Run.DreamDepth;
            var rng = p.TakeRng();
            var relic = BossSets.RollDrop(rng, bossTypeName, nightmare, depth, Math.Max(1, game?.ambientLevel ?? 1));
            p.StoreRng(rng);
            string result = Loc.T(
                $"開発用ボス抽選：{bossTypeName}、確率{BossSets.DropChance(nightmare, depth) * 100:0}%、{(relic == null ? "当選なし" : relic.DisplayName)}。",
                $"Developer boss roll: {bossTypeName}, chance {BossSets.DropChance(nightmare, depth) * 100:0}%, {(relic == null ? "no drop" : relic.DisplayName)}.");
            Debug.Log("[DreamforgeRPG] " + result);
            if (relic != null) GrantDeveloperBossRelic(p, relic);
            _ui.Notify(new GameEvent(EventKind.Info, "[debug] " + result));
            _session.MarkDirty(true);
            _session.SaveNow();
        }

        private void GrantDeveloperBossRelic(Profile profile, Relic relic)
        {
            relic.DeveloperGranted = true;
            if (profile.Run != null) profile.Run.Satchel.Add(relic);
            else profile.Stash.Add(relic);
            string message = Loc.T("開発付与：", "Developer grant: ") + relic.DisplayName;
            Debug.Log("[DreamforgeRPG] " + message + " (" + relic.UniqueId + ", " + relic.Uid + ")");
            _ui.Notify(new GameEvent(EventKind.Drop, "[debug] " + message, relic.Rarity));
        }

        [ConsoleCommand("Dreamforge: log your hero's final stats and the bonus applied by this mod", "dreamforge_stats")]
        private void StatsCommand()
        {
            if (!CommandAllowed()) return;
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

        [ConsoleCommand("Dreamforge: show profile summary", "dreamforge_status")]
        private void StatusCommand()
        {
            if (!CommandAllowed()) return;
            var p = _session.Profile;
            Debug.Log($"[DreamforgeRPG] Lv{p.DreamLevel} xp{p.DreamXp} stash{p.Stash.Count} shards{p.Material(Materials.Shard)} run={(p.Run != null ? p.Run.RunId + " heat" + p.Run.Heat + " satchel" + p.Run.Satchel.Count : "none")} active={_session.ActiveRunId} hostOk={_session.HostConfirmed} hostActive={_host.IsActive}");
        }
    }
}
