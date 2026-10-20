using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using SodRpg.Core.Game;
using SodRpg.Mod;
using UnityEngine;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>Skoll バースト表示（HostAuthority.BossGlacialCore.cs / HostAuthority.BossVisuals.cs の抽出実行）。
    /// バーストの due が開始時刻に固定された結果、(1) 残数も位置も変わらないティックが差分を出さないこと
    /// （対照として、持ち主が動いただけのティックは間引き窓の中では保留に入る）、
    /// (2) 発射で残数が減ると即座に差分が出て due がバースト開始時刻のまま届くこと、
    /// (3) 残数が0になると（発射で尽きる・解放）削除差分が出ることと、BurstUntil の到達では
    /// 残数0・効果は開始時に届いた expires の期限で取り除かれること、
    /// (4) 同じ BurstVisual の id で次のバーストが始まると due が更新されて新たに送られること。
    /// 境界は SkollBurstBoundary.cs、抽出は NativePersistence.targets の CompileSkollBurst。
    /// 本体の時計は戻らないため、各テストの時刻は単調に進める（発射の時刻は期限から逆算する）。</summary>
    public sealed class SkollBurstTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly HostAuthority _host = new HostAuthority();
        private readonly Hero _hero = new Hero { netId = 7 };
        private readonly HostAuthority.HeroRuntime _rt;
        private readonly Gem_U_GlacialCore _core = new Gem_U_GlacialCore { creationTime = 1f };

        public SkollBurstTests()
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            DewPlayer.gamePlayers.Clear();
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "skoll" };
            NetworkedManagerBase<ZoneManager>.softInstance = new ZoneManager { currentZoneIndex = 0 };
            Time.time = 10f; Time.unscaledTime = 10f; Time.frameCount = 1;
            HostAuthority.NativeInstance = _host;
            NetworkTrafficOptions.Configure(true, true, true, true);
            var player = new DewPlayer { netId = 2, guid = "guid-2", isHumanPlayer = true, hero = _hero, connectionToClient = new NetworkConnectionToClient() };
            _hero.owner = player;
            DewPlayer.gamePlayers.Add(player);
            _rt = new HostAuthority.HeroRuntime { Hero = _hero };
            ((Dictionary<Hero, HostAuthority.HeroRuntime>)Get(_host, "_runtimes"))[_hero] = _rt;
            Set(_host, "_registeredOn", new Actor());
        }

        public void Dispose()
        {
            NetworkServer.active = false;
            DewPlayer.gamePlayers.Clear();
            HostAuthority.NativeInstance = null;
        }

        // ---- 準備 ----

        private static Type ObjectType(object target) => target is Type type ? type : target.GetType();
        private static object Instance(object target) => target is Type ? null : target;
        private static object Get(object target, string name) => ObjectType(target).GetField(name, Hidden).GetValue(Instance(target));
        private static void Set(object target, string name, object value) => ObjectType(target).GetField(name, Hidden).SetValue(Instance(target), value);
        private static object Call(object target, string name, params object[] args)
        {
            try { return ObjectType(target).GetMethod(name, Hidden).Invoke(Instance(target), args); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        private static void Clock(float now) { Time.time = now; Time.unscaledTime = now; }

        private HostAuthority.SkollCoreState CreateCore() => _host.SkollCreateCoreForTest(_rt, _core);

        private object Owner() => _host.SkollOwnerForTest(_rt);

        private HostAuthority.SkollCoreState CoreState() => (HostAuthority.SkollCoreState)((IDictionary)Get(Owner(), "Cores"))[_core];

        // 本体では SkollCoreColdHealRoutine の完了（EntityEventOnTakeHeal）から入る。
        private void StartBurst(float now)
        {
            Clock(now);
            var routine = new object();
            _host.SkollApprovedRoutines.Add(routine);
            Call(_host, "CompleteSkollCoreHeal", routine, _core,
                new EventInfoDamage { victim = new Entity { creationTime = 1f } });
        }

        // 本体では ActiveLogicUpdate の Prefix（BeginSkollCoreUpdate）から毎ティック入る。
        private void Tick(float now) { Clock(now); Call(_host, "TickSkollCores", _rt, Owner(), now); }

        // 本体では Actor.InvokeOnAbilityInstanceCreated の Postfix（弾の生成ごと）から入る。
        private void Fire(float now)
        {
            Clock(now);
            CoreState().ApprovedThisUpdate = true;
            Call(_host, "CountSkollCoreShot",
                new Ai_Gem_U_GlacialCore_Projectile { parentActor = _core, info = new CastInfo { caster = _hero } });
        }

        private object Visual()
        {
            var states = (Array)Get(_host, "_bossVisuals");
            return states.Cast<object>().First(state => ReferenceEquals(Get(state, "Owner"), _rt));
        }
        private long Revision() => (long)Get(Visual(), "Revision");
        private bool HasPending() => (bool)Get(Visual(), "HasPending");

        private List<DreamforgeBossEffect[]> Sent => _host.SentBossVisualDeltas;

        // ---- (1) 無変化のティックは差分を出さない ----

        [Fact]
        public void Unchanged_burst_ticks_produce_no_new_delta()
        {
            CreateCore();
            StartBurst(10f);
            var state = CoreState();
            Assert.True(state.Remaining > 0, "報酬段階3の実プロファイルでバーストが始まる");
            Assert.Equal(1, Sent.Count);
            var first = Sent[0].Single();
            Assert.Equal(9, first.kind);
            Assert.Equal(10d, first.due, 6);
            long revision = Revision();
            // 残数も位置も変わらないティックが間引き窓（0.1秒）をまたいで続いても、何も起きない。
            foreach (var now in new[] { 10.05f, 10.15f, 10.3f, 10.5f }) Tick(now);
            Assert.Equal(1, Sent.Count);
            Assert.Equal(revision, Revision());
            Assert.False(HasPending()); // 保留（10Hz間引き）に入った形跡もない: 同一値の早期return
        }

        // ---- (1) の対照: 持ち主が動いただけなら、直前の送信から0.1秒の間引き窓の中では即送ではなく保留に入る ----
        // （見た目の追従は残る。窓の外での移動は従来どおり即座に1通出る。時刻は単調に進める。）

        [Fact]
        public void Owner_movement_within_throttle_window_defers_to_pending()
        {
            CreateCore();
            StartBurst(10f);
            Assert.Equal(1, Sent.Count);
            _hero.position = new Vector3(5f, 0f, 0f);
            Tick(10.02f); // 直前の送信（10.0）から0.1秒以内
            Assert.Equal(1, Sent.Count);
            Assert.True(HasPending());
        }

        // ---- (2) 発射は即時送信で、due はバースト開始時刻のまま ----

        [Fact]
        public void Shot_sends_delta_immediately_and_due_stays_at_burst_start()
        {
            CreateCore();
            StartBurst(10f);
            int remaining = CoreState().Remaining;
            Assert.True(remaining > 0);
            Fire(10.7f);
            Assert.Equal(remaining - 1, CoreState().Remaining);
            Assert.Equal(2, Sent.Count);
            var delta = Sent[1].Single();
            Assert.Equal(remaining - 1, delta.count);
            Assert.Equal(10d, delta.due, 6); // 現在時刻 10.7 ではなくバースト開始の 10 のまま
            Assert.False(delta.removed);
            Assert.False(HasPending());
        }

        // ---- (3) 発射で残数が尽きると（バースト中の Remaining==0）、削除差分が即座に出る ----

        [Fact]
        public void Shot_depleting_remaining_sends_removal_delta()
        {
            CreateCore();
            StartBurst(10f);
            var state = CoreState();
            int remaining = state.Remaining;
            Assert.True(remaining > 1);
            // 発射の時刻は単調に増やし、すべてバースト期限（BurstUntil）より前に収める。
            // 残数や期限が実プロファイルで変わっても時計が戻らないよう、期限から逆算する。
            Assert.True(state.BurstUntil > 10.05f, "最初の発射から期限までの間に全発を収める");
            float step = (state.BurstUntil - 10.05f) / remaining;
            for (int shot = 1; shot < remaining; shot++) Fire(10.05f + step * shot); // 残数を1まで減らす
            long visual = state.BurstVisual;
            Assert.Equal(1 + (remaining - 1), Sent.Count); // 開始 + 減算のたびに1通
            Fire(10.05f + step * (remaining - 0.5f)); // 最後の1発で残数0（直前の発射より後・期限より前）
            Assert.Equal(0, CoreState().Remaining);
            var removal = Sent[Sent.Count - 1].Single();
            Assert.True(removal.removed);
            Assert.Equal(visual, removal.id);
            Assert.Equal(0, removal.count);
        }

        // ---- (3) BurstUntil の到達でも残数は0になり、効果は expires 期限で取り除かれる ----
        // （削除の伝達はこの経路では電文によらず、開始時に届いた expires の期限で両側が同じく扱う。
        //   本体側は PruneBossVisualState が先に効果を外すため、削除電文の発行は上の2経路だけで起きる。）

        [Fact]
        public void Burst_until_zeroes_remaining_and_expires_by_deadline()
        {
            CreateCore();
            StartBurst(10f);
            var state = CoreState();
            Assert.True(state.Remaining > 0);
            Assert.True(state.BurstUntil > 10f);
            Tick(state.BurstUntil + 0.5f);
            Assert.Equal(0, CoreState().Remaining);
            Assert.Equal(1, Sent.Count);              // 期限切れでの追加の差分はない
            Assert.Equal(0, (int)Get(Visual(), "Count")); // 効果は expires で host 側も取り除かれる
            Assert.False(HasPending());
        }

        // ---- (3) 解放でも残数0の削除が出て、state は返却される ----

        [Fact]
        public void Core_release_mid_burst_sends_removal_delta()
        {
            CreateCore();
            StartBurst(10f);
            var state = CoreState();
            Assert.True(state.Remaining > 0);
            long visual = state.BurstVisual;
            _core.isValid = false; // SkollCoreRuntime が成立しなくなる（装備が外れた相当）
            Tick(10.4f);
            Assert.Equal(2, Sent.Count);
            var removal = Sent[1].Single();
            Assert.True(removal.removed);
            Assert.Equal(visual, removal.id);
            Assert.Equal(0, removal.count);
            Assert.Null(state.Core); // SkollReleaseCore まで実ソースどおり
            Assert.Equal(0, ((IDictionary)Get(Owner(), "Cores")).Count);
        }

        // ---- (4) 同じ id の次のバーストは due を更新して送られる ----

        [Fact]
        public void Next_burst_reuses_visual_id_with_updated_due()
        {
            CreateCore();
            StartBurst(10f);
            var state = CoreState();
            long visual = state.BurstVisual;
            Tick(state.BurstUntil + 0.5f); // 1回目のバースト終了（残数0・効果は期限で削除済み）
            Assert.Equal(1, Sent.Count);
            StartBurst(20f);
            var again = CoreState();
            Assert.Equal(visual, again.BurstVisual); // state は解放されず id を使い回す
            Assert.True(again.Remaining > 0);
            Assert.Equal(2, Sent.Count);
            var delta = Sent[1].Single();
            Assert.Equal(visual, delta.id);
            Assert.Equal(20d, delta.due, 6); // 1回目の開始時刻 10 ではなく 20 に更新
            Assert.Equal(again.Remaining, delta.count);
            Assert.False(delta.removed);
        }
    }
}
