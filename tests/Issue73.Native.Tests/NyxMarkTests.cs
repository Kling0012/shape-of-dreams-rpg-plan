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
    /// <summary>Nyx 印表示（HostAuthority.BossNyx.cs / HostAuthority.BossVisuals.cs の抽出実行）。
    /// 印の due が蓄積開始時刻に固定された結果、(1) 持ち主の位置も印の数も変わらない0.25秒ごとの
    /// 再発行は差分を出さないこと（対照として、持ち主が動いただけの再発行は間引き窓の中では
    /// 保留に入り、窓の外では従来どおり即座に1通出る）、(2) 印が増えると即座に差分が出て、
    /// due は最初の蓄積開始時刻のまま送信時刻以下で届くこと（クライアントの
    /// pending = effect.due > now は false のまま）。満ちた印（3つ）の再取得は数が変わらなくても
    /// 期限の延長を送ること、(3) 期限切れで印が消えたあと再び印が付くと、due が新しい蓄積開始
    /// 時刻に更新されること。種（kind 3）と領域（kind 4）の表示、ダッシュ（表示なし）、
    /// 星界の運命（stage6）の印の消費は印表示の対象外。境界は NyxMarkBoundary.cs、抽出は
    /// NativePersistence.targets の
    /// CompileNyxMarks。印は通常記憶で星点を置き（stage2）、通常攻撃の命中でそれを消すことで
    /// 1つ得られる（stage3）。本体の時計は戻らないため、各テストの時刻は単調に進める。</summary>
    public sealed class NyxMarkTests : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly HostAuthority _host = new HostAuthority();
        private readonly Hero _hero = new Hero { netId = 7 };
        private readonly HostAuthority.HeroRuntime _rt;

        public NyxMarkTests()
        {
            NetworkServer.active = true;
            NetworkClient.active = false;
            DewPlayer.gamePlayers.Clear();
            NetworkedManagerBase<GameManager>.softInstance = new GameManager { runId = "nyx" };
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
            // stage2（星点と柱）と stage3（印）だけを装備した相当。BossFind は Build.BossMoves の
            // この一覧から対象を探す（印の付与は両方が揃ったときに起きる）。
            Assert.True(BossProfiles.TryGetMove("boss_nyx.stage2", out var stage2), "実プロファイルから stage2 が引ける");
            Assert.True(BossProfiles.TryGetMove("boss_nyx.stage3", out var stage3), "実プロファイルから stage3 が引ける");
            _rt.Powers.Build.BossMoves.Add(new BossMoveEntry(stage2.SetId, stage2.Id, new BossChannelValue[0]));
            _rt.Powers.Build.BossMoves.Add(new BossMoveEntry(stage3.SetId, stage3.Id, new BossChannelValue[0]));
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

        // 本体では通常記憶の使用と通常攻撃の命中ごとに BossDispatch から入る。
        private void Dispatch(BossEvent kind, float now)
        {
            Clock(now);
            Call(_host, "DispatchNyxBoss", _rt, kind, 1L, new Entity { creationTime = 1f }, Vector3.zero, now);
        }

        // 印を1つ得る: 通常記憶で星点を置き（kind 4 の差分1通）、通常攻撃の命中でそれを消して
        // （同1通）印を出版する（kind 9 の1通）。
        private void Gain(float now) { Dispatch(BossEvent.MemoryUse, now); Dispatch(BossEvent.MainHit, now); }

        // 本体では ActiveLogicUpdate の Prefix から毎ティック入る。
        private void Tick(float now) { Clock(now); Call(_host, "TickNyxBoss", _rt, now); }

        private object Marks() => _host.NyxOwnerForTest(_rt);

        private object Visual()
        {
            var states = (Array)Get(_host, "_bossVisuals");
            return states.Cast<object>().First(state => ReferenceEquals(Get(state, "Owner"), _rt));
        }
        private long Revision() => (long)Get(Visual(), "Revision");
        private bool HasPending() => (bool)Get(Visual(), "HasPending");

        private List<DreamforgeBossEffect[]> Sent => _host.SentBossVisualDeltas;

        private static DreamforgeBossEffect MarkDelta(DreamforgeBossEffect[] batch) => batch.Single(e => e.kind == 9);

        // ---- (1) 無変化の再発行は差分を出さない ----

        [Fact]
        public void Unchanged_mark_republishes_produce_no_new_delta()
        {
            Gain(10f);
            var state = Marks();
            Assert.Equal(1, (int)Get(state, "Marks"));
            Assert.Equal(3, Sent.Count); // 星点の設置・消費と印の3通
            var first = MarkDelta(Sent[2]);
            Assert.Equal(9, first.kind);
            Assert.Equal(1, first.count);
            Assert.Equal(10d, first.due, 6); // due は蓄積開始時刻
            long revision = Revision();
            // 持ち主の位置も印の数も変わらない0.25秒ごとの再発行が間引き窓（0.1秒）をまたいで
            // 続いても、何も起きない。
            foreach (var now in new[] { 10.26f, 10.51f, 10.76f, 11.01f }) Tick(now);
            // NyxPublishMarks を直接呼んでも同じ（毎0.25秒の再発行と同じ引数になる）。
            Call(_host, "NyxPublishMarks", _rt, state, 11.02f);
            Call(_host, "NyxPublishMarks", _rt, state, 11.5f);
            Assert.Equal(3, Sent.Count);
            Assert.Equal(revision, Revision());
            Assert.False(HasPending()); // 保留（10Hz間引き）に入った形跡もない: 同一値の早期return
        }

        // ---- (1) の対照: 持ち主が動いた再発行は、間引き窓（直前の送信から0.1秒）の外では
        // 従来どおり即座に1通出る。窓の中では保留（10Hz間引き）に入る（見た目の追従は残る）。

        [Fact]
        public void Owner_movement_still_produces_deltas()
        {
            Gain(10f);
            Assert.Equal(3, Sent.Count);
            _hero.position = new Vector3(5f, 0f, 0f); // 印は持ち主に追従する
            Tick(10.26f); // 0.25秒ごとの再発行（直前の送信 10.0 の窓の外）
            Assert.Equal(4, Sent.Count);
            var moved = MarkDelta(Sent[3]);
            Assert.Equal(5f, moved.center.x, 6);
            Assert.Equal(5f, moved.end.x, 6);
            Assert.Equal(1, moved.count);
            Assert.Equal(10d, moved.due, 6); // due は蓄積開始時刻のまま
            _hero.position = new Vector3(5.5f, 0f, 0f);
            var state = Marks();
            Call(_host, "NyxPublishMarks", _rt, state, 10.31f); // 直前の送信（10.26）から0.1秒以内
            Assert.Equal(4, Sent.Count);
            Assert.True(HasPending());
        }

        // ---- (2) 印の増加は即時送信で、due は蓄積開始時刻のまま送信時刻以下 ----

        [Fact]
        public void Gained_mark_sends_due_of_accumulation_start_below_send_time()
        {
            Gain(10f);
            Gain(12.3f); // 数の増加と MarksUntil の延長は即時の差分になる
            var state = Marks();
            Assert.Equal(2, (int)Get(state, "Marks"));
            Assert.Equal(6, Sent.Count); // 2回の星点の設置・消費と印
            var delta = MarkDelta(Sent[5]);
            Assert.Equal(2, delta.count);
            Assert.Equal(10d, delta.due, 6); // 現在時刻 12.3 ではなく蓄積開始の 10 のまま
            Assert.Equal(12.3d + 12, delta.expires, 4); // 期限は延びて届く
            Assert.False(delta.removed);
            Assert.False(HasPending());
            Tick(12.56f); // 数も位置も変わらなければ、その後の再発行も出さない
            Assert.Equal(6, Sent.Count);
            // クライアント（ClientSession.BossVisuals.cs）は copy.due = effect.due + clockOffset、
            // pending = copy.due > now と読む（clockOffset = now - estimatedHostTime）。遅延がなければ
            // estimatedHostTime は送信時刻の hostTime と一致し、due はそれ以下のため pending は false。
            double clientNow = 12.35d;
            double clockOffset = clientNow - 12.3d;
            double clientDue = delta.due + clockOffset;
            Assert.False(clientDue > clientNow);
        }

        // ---- (2) の延長: 満ちた印（3つ）の再取得は数が変わらなくても期限の延長を送る ----

        [Fact]
        public void MarksUntil_refresh_at_cap_still_sends_delta()
        {
            Gain(10f); Gain(12.3f); Gain(14f);
            var state = Marks();
            Assert.Equal(3, (int)Get(state, "Marks"));
            Assert.Equal(9, Sent.Count);
            Gain(16f); // 4回目: 数は3のままで MarksUntil だけが延びる
            Assert.Equal(12, Sent.Count);
            var delta = MarkDelta(Sent[11]);
            Assert.Equal(3, delta.count);
            Assert.Equal(10d, delta.due, 6); // due も蓄積開始時刻のまま
            Assert.Equal(16d + 12, delta.expires, 4); // 期限だけが延びる
            Assert.False(delta.removed);
        }

        // ---- (3) 期限切れの後の再蓄積は due を新しい時刻に更新する ----

        [Fact]
        public void Marks_reaccumulated_after_expiry_get_new_due()
        {
            Gain(10f);
            var state = Marks();
            float until = (float)Get(state, "MarksUntil");
            Assert.Equal(22f, until); // 印の寿命は12秒
            Tick(until + 0.5f); // 期限切れ: 印は消え、効果は expires の期限で host 側からも外れる
            Assert.Equal(0, (int)Get(state, "Marks"));
            Assert.Equal(3, Sent.Count); // 期限切れでの追加の差分はない
            Assert.Equal(0, (int)Get(Visual(), "Count"));
            Assert.False(HasPending());
            Gain(23f); // 再び印が付く
            Assert.Equal(1, (int)Get(state, "Marks"));
            Assert.Equal(6, Sent.Count);
            var delta = MarkDelta(Sent[5]);
            Assert.Equal(23d, delta.due, 6); // 1回目の蓄積開始 10 ではなく 23 に更新
            Assert.Equal(1, delta.count);
            Assert.False(delta.removed);
        }
    }
}
