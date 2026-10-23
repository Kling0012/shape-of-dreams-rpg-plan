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
    /// <summary>Skoll 印表示（HostAuthority.BossSkoll.cs / HostAuthority.BossVisuals.cs の抽出実行）。
    /// 印の due が蓄積開始時刻に固定された結果、(1) 持ち主の位置も印の数も変わらないティックは
    /// 差分を出さないこと（対照として、持ち主が動いただけのティックは間引き窓の中では保留に入る）、
    /// (2) 印が増えると即座に差分が出て、due は最初の蓄積開始時刻のまま送信時刻以下で届くこと
    /// （クライアントの pending = effect.due > now は false のまま）、
    /// (3) 期限切れで印が消えたあと再び印が付くと、due が新しい蓄積開始時刻に更新されること。
    /// 満ちた印の解除（SkollSlash）や部位ごとの攻撃は印表示の対象外。境界は SkollMarkBoundary.cs、
    /// 抽出は NativePersistence.targets の CompileSkollMarks。本体の時計は戻らないため、
    /// 各テストの時刻は単調に進める。</summary>
    public sealed class SkollMarkTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly HostAuthority _host = new HostAuthority();
        private readonly Hero _hero = new Hero { netId = 7 };
        private readonly HostAuthority.HeroRuntime _rt;
        private readonly int _maxMarks;
        private readonly float _markLifetime;

        public SkollMarkTests()
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
            // stage2（印）だけを装備した相当。BossFind は Build.BossMoves のこの一覧から対象を探す。
            Assert.True(BossProfiles.TryGetMove("boss_skoll.stage2", out var profile), "実プロファイルから stage2 が引ける");
            _maxMarks = profile.Actions[0].Count;
            _markLifetime = profile.Actions[0].LifetimeMillis / 1000f;
            Assert.True(_maxMarks > 1, "印は複数個まで溜まる");
            _rt.Powers.Build.BossMoves.Add(new BossMoveEntry(profile.SetId, profile.Id, new BossChannelValue[0]));
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

        // 本体では通常攻撃の命中・記憶の使用ごとに BossDispatch から入る。
        private void Gain(BossEvent kind, float now)
        {
            Clock(now);
            Call(_host, "DispatchSkollBoss", _rt, kind, 1L, new Entity { creationTime = 1f }, Vector3.zero, now);
        }

        // 本体では ActiveLogicUpdate の Prefix から毎ティック入る。
        private void Tick(float now) { Clock(now); Call(_host, "TickSkollBoss", _rt, now); }

        private object Marks() => _host.SkollOwnerForTest(_rt);

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
        public void Unchanged_mark_ticks_produce_no_new_delta()
        {
            Gain(BossEvent.MainHit, 10f);
            var state = Marks();
            Assert.Equal(1, (int)Get(state, "Marks"));
            Assert.Equal(1, Sent.Count);
            var first = Sent[0].Single();
            Assert.Equal(9, first.kind);
            Assert.Equal(1, first.count);
            Assert.Equal(10d, first.due, 6); // due は蓄積開始時刻
            long revision = Revision();
            // 持ち主の位置も印の数も変わらないティックが間引き窓（0.1秒）をまたいで続いても、何も起きない。
            foreach (var now in new[] { 10.05f, 10.15f, 10.3f, 10.5f }) Tick(now);
            // SkollPublishMarks を直接呼んでも同じ（毎ティックの再発行と同じ引数になる）。
            Call(_host, "SkollPublishMarks", _rt, state);
            Call(_host, "SkollPublishMarks", _rt, state);
            Assert.Equal(1, Sent.Count);
            Assert.Equal(revision, Revision());
            Assert.False(HasPending()); // 保留（10Hz間引き）に入った形跡もない: 同一値の早期return
        }

        // ---- (1) の対照: 持ち主が動いただけなら、直前の送信から0.1秒の間引き窓の中では保留に入る ----
        // （見た目の追従は残る。窓の外での移動は従来どおり即座に1通出る。時刻は単調に進める。）

        [Fact]
        public void Owner_movement_within_throttle_window_defers_to_pending()
        {
            Gain(BossEvent.MainHit, 10f);
            Assert.Equal(1, Sent.Count);
            _hero.position = new Vector3(5f, 0f, 0f); // 印は持ち主に追従する
            Tick(10.02f); // 直前の送信（10.0）から0.1秒以内
            Assert.Equal(1, Sent.Count);
            Assert.True(HasPending());
        }

        // ---- (2) 印の増加は即時送信で、due は蓄積開始時刻のまま送信時刻以下 ----

        [Fact]
        public void Gained_mark_sends_due_of_accumulation_start_below_send_time()
        {
            Gain(BossEvent.MainHit, 10f);
            Gain(BossEvent.MemoryUse, 12.3f); // 数の増加は即時の差分になる
            var state = Marks();
            Assert.Equal(2, (int)Get(state, "Marks"));
            Assert.Equal(2, Sent.Count);
            var delta = Sent[1].Single();
            Assert.Equal(2, delta.count);
            Assert.Equal(10d, delta.due, 6); // 現在時刻 12.3 ではなく蓄積開始の 10 のまま
            Assert.False(delta.removed);
            Assert.False(HasPending());
            Tick(12.4f); // 数も位置も変わらなければ、その後のティックも出さない
            Assert.Equal(2, Sent.Count);
            // クライアント（ClientSession.BossVisuals.cs）は copy.due = effect.due + clockOffset、
            // pending = copy.due > now と読む（clockOffset = now - estimatedHostTime）。遅延がなければ
            // estimatedHostTime は送信時刻の hostTime と一致し、due はそれ以下のため pending は false。
            double clientNow = 12.35d;
            double clockOffset = clientNow - 12.3d;
            double clientDue = delta.due + clockOffset;
            Assert.False(clientDue > clientNow);
        }

        // ---- (3) 期限切れの後の再蓄積は due を新しい時刻に更新する ----

        [Fact]
        public void Marks_reaccumulated_after_expiry_get_new_due()
        {
            Gain(BossEvent.MainHit, 10f);
            var state = Marks();
            float until = (float)Get(state, "MarksUntil");
            Assert.Equal(10f + _markLifetime, until, 6);
            Tick(until + 0.5f); // 期限切れ: 印は消え、効果は expires の期限で host 側からも外れる
            Assert.Equal(0, (int)Get(state, "Marks"));
            Assert.Equal(1, Sent.Count); // 期限切れでの追加の差分はない
            Assert.Equal(0, (int)Get(Visual(), "Count"));
            Assert.False(HasPending());
            Gain(BossEvent.MainHit, 20f); // 再び印が付く
            Assert.Equal(1, (int)Get(state, "Marks"));
            Assert.Equal(2, Sent.Count);
            var delta = Sent[1].Single();
            Assert.Equal(20d, delta.due, 6); // 1回目の蓄積開始 10 ではなく 20 に更新
            Assert.Equal(1, delta.count);
            Assert.False(delta.removed);
        }
    }
}
