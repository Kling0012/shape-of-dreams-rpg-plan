using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Core.Internal;
using SodRpg.Mod;
using Xunit;

// #1094: Beam of Balance の本体 Hit 1回の中で MOD 側の検証を PrepareInkBeamHit の判定
// （InkBeamState.HitPrepared）にまとめた。敵命中（Flat+Damage）と味方命中（Heal）のそれぞれで、
// フラグを流用する経路（変更後）と、フラグを落として毎回全検証を走らせる経路（変更前と同じ式）
// の InkBeamNative の戻り値と副作用が一致すること、BossEnsure 失敗（ゾーン遷移中）ではフラグが
// 立たず処理が止まること、dispatch の間にビーム・親・術者が消えたときは処理が止まることを確認する。
namespace SodRpg.Core.Tests
{
    public class InkBeamHitVerdictReuseTests
    {
        private const string HeroKey = "Hero_A";
        private const string DarkMoon = BossProfiles.DarkMoonSetId;
        private const string WhiteNight = BossProfiles.WhiteNightSetId;

        private sealed class Rig
        {
            internal HostAuthority Host;
            internal HostAuthority.HeroRuntime Rt;
            internal Hero Hero;
            internal Se_U_BeamOfBalance Parent;
            internal Ai_U_BeamOfBalance_Beam Beam;
            internal object State;
            internal Entity Enemy;
            internal Hero Ally;
            internal Room Room;
        }

        private sealed class Snapshot
        {
            internal bool Gate;
            internal float DarkSpent, WhiteSpent, ShieldReward;
            internal int Adds, Spears, Illuminations, WhiteTargets, Shields;
            internal long LastAddedPacket;
            internal float NextNativeAdd;
            internal bool DwellHasEnemy, WhiteHeroIsAlly;
        }

        // 2ピース装備で WhiteNight/DarkMoon の報酬段位1（NativeShield/NativeDamage が有効）を作る。
        private static Build InkBuild(string setId)
        {
            var order = Content.SlotOrder;
            int Rank(Slot slot) { for (int i = 0; i < order.Count; i++) if (order[i] == slot) return i; return order.Count; }
            var ids = Content.Uniques.Where(u => u.SetId == setId)
                .OrderBy(u => Rank(Content.GetBase(u.BaseId).Slot)).ToList();
            var profile = Profile.CreateNew(48);
            ulong seed = 900;
            for (int i = 0; i < 2; i++)
            {
                Content.TryGetUnique(ids[i].Id, out var unique);
                var relic = Loot.RollUnique(new Rng(seed++), unique, 5);
                profile.Stash.Add(relic);
                Rules.Equip(profile, HeroKey, relic.Uid);
            }
            var build = Build.Compute(profile, HeroKey, 0);
            Assert.Equal(1, build.BossRewards.Single(r => r.SetId == setId).Stage);
            return build;
        }

        private static Rig Create(string setId)
        {
            Mirror.NetworkServer.active = true;
            UnityEngine.Time.time = 10f;
            SodRpg.Mod.NetworkedManagerBase<SodRpg.Mod.ZoneManager>.softInstance.currentRoom = new SodRpg.Mod.Room();
            SodRpg.Mod.NetworkedManagerBase<SodRpg.Mod.ZoneManager>.softInstance.isInAnyTransition = false;
            SodRpg.Mod.NetworkedManagerBase<SodRpg.Mod.GameManager>.softInstance.runId = "beam-run";
            SodRpg.Mod.EntityStatus.LiveStatusEffects.Clear();
            SodRpg.Mod.DewPhysics.Entities.Clear();
            SodRpg.Mod.NativeAttributedDamagePacket.Current = null;

            var build = InkBuild(setId);
            var room = new SodRpg.Mod.Room();
            SodRpg.Mod.NetworkedManagerBase<SodRpg.Mod.ZoneManager>.softInstance.currentRoom = room;
            var hero = new SodRpg.Mod.Hero { creationTime = 1f };
            var skill = new SodRpg.Mod.St_U_BeamOfBalance { owner = hero, creationTime = 2f };
            hero.Skill.EquipSkill(HeroSkillLocation.Identity, skill);
            var host = new HostAuthority();
            var rt = new HostAuthority.HeroRuntime { Hero = hero };
            rt.Powers = new PowerRuntime(build, 0f);
            rt.Powers.SetBuild(build);
            rt.AppliedBuild = new HostAuthority.GemBuildForTest { Build = build };
            host.Track(rt);
            HostAuthority.NativeInstance = host;

            var parent = new SodRpg.Mod.Se_U_BeamOfBalance { creationTime = 3f };
            parent.info.caster = hero;
            parent.parentActor = skill;
            host.BeginInkBeam(parent);
            // CreateAbilityInstance は本体と同じく親の ActorEvent_OnAbilityInstanceCreated を
            // 挙げ、BindInkBeam が親子同一性を記録した実ビームを繋ぐ。
            var beam = (SodRpg.Mod.Ai_U_BeamOfBalance_Beam)parent.CreateAbilityInstance<SodRpg.Mod.Ai_U_BeamOfBalance_Beam>(new CastInfo { caster = hero });

            var beams = (System.Collections.IDictionary)typeof(HostAuthority)
                .GetField("_inkBeams", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
            Assert.NotNull(beams[parent]);
            return new Rig
            {
                Host = host, Rt = rt, Hero = hero, Parent = parent, Beam = beam, State = beams[parent],
                Enemy = new SodRpg.Mod.Entity { creationTime = 5f },
                Ally = new SodRpg.Mod.Hero { creationTime = 6f },
                Room = room,
            };
        }

        // リグを作るたびに静的な currentRoom が入れ替わるので、過去のリグを再び触る前には
        // そのリグの部屋・Run に世界を戻す（本体でも1つの Hit の中で部屋が変わることはない）。
        private static void Focus(Rig rig)
        {
            var zone = SodRpg.Mod.NetworkedManagerBase<SodRpg.Mod.ZoneManager>.softInstance;
            zone.currentRoom = rig.Room;
            zone.isInAnyTransition = false;
            SodRpg.Mod.NetworkedManagerBase<SodRpg.Mod.GameManager>.softInstance.runId = "beam-run";
            HostAuthority.NativeInstance = rig.Host;
        }

        private static object Invoke(HostAuthority host, string method, params object[] args)
        {
            var info = typeof(HostAuthority).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(info);
            return info.Invoke(host, args);
        }

        private static object Field(object instance, string name)
        {
            var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(field);
            return field.GetValue(instance);
        }

        private static void SetField(object instance, string name, object value)
        {
            var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(field);
            field.SetValue(instance, value);
        }

        private static bool HitPrepared(Rig rig) => (bool)Field(rig.State, "HitPrepared");

        // 変更前の挙動を同じ実行バイナリ内で再現する: PrepareInkBeamHit は本体 Hit と同じく呼んだ
        // 上で、判定フラグだけ反射で落とし、各コールバックが毎回全検証（InkBeamCurrent + BossEnsure
        // + InkReconcile）を走らせる変更前の InkBeamNative の式を通す。
        private static void PrepareWithoutVerdict(Rig rig)
        {
            rig.Host.PrepareInkBeamHit(rig.Beam);
            SetField(rig.State, "HitPrepared", false);
        }

        // コールバックが本体 Hit の中で検証を始めるのと同じ位置で、同じ Scope を置いて呼ぶ。
        private static bool Gate(Rig rig, bool enemy)
        {
            var target = enemy ? (Entity)rig.Enemy : rig.Ally;
            SodRpg.Mod.InkBeamNativeHit.Current = new SodRpg.Mod.InkBeamNativeHit.Scope { Beam = rig.Beam, Target = target, Enemy = enemy };
            return (bool)Invoke(rig.Host, "InkBeamNative", rig.State, rig.Beam, target, enemy);
        }

        private static void EnemyHit(Rig rig, long serial)
        {
            SodRpg.Mod.InkBeamNativeHit.Current = new SodRpg.Mod.InkBeamNativeHit.Scope { Beam = rig.Beam, Target = rig.Enemy, Enemy = true };
            SodRpg.Mod.NativeAttributedDamagePacket.Current = new SodRpg.Mod.NativeAttributedDamagePacket.Packet
            { Actor = rig.Beam, Victim = rig.Enemy, Serial = serial };
            var damage = new DamageData(50f) { actor = rig.Beam };
            Invoke(rig.Host, "InkBeamFlat", rig.State, damage, rig.Beam, rig.Enemy);
            Invoke(rig.Host, "InkBeamDamage", rig.State, new EventInfoDamage { actor = rig.Beam, victim = rig.Enemy, damage = damage });
        }

        private static void AllyHit(Rig rig)
        {
            SodRpg.Mod.InkBeamNativeHit.Current = new SodRpg.Mod.InkBeamNativeHit.Scope { Beam = rig.Beam, Target = rig.Ally, Enemy = false };
            SodRpg.Mod.NativeAttributedDamagePacket.Current = null;
            Invoke(rig.Host, "InkBeamHeal", rig.State, new EventInfoHeal
            { actor = rig.Beam, target = rig.Ally, victim = rig.Ally, amount = 10f, discardedAmount = 10f });
        }

        private static bool DwellHas(Rig rig, Entity target)
        {
            foreach (var row in (System.Collections.IEnumerable)Field(rig.State, "Dwell"))
            {
                var found = Field(row, "Target");
                if (found != null && ReferenceEquals(found, target)) return true;
            }
            return false;
        }

        // 命中の後に取るスナップショット。Gate は命中の前（コールバックが最初に検証を通る位置）で
        // 別途呼んだ InkBeamNative の戻り値を代入して使う。
        private static Snapshot Snap(Rig rig, bool enemy, bool gate)
        {
            var ink = Invoke(rig.Host, "InkGet", rig.Rt);
            float shieldReward = 0; int shields = 0;
            foreach (var shield in (System.Collections.IEnumerable)Field(ink, "Shields"))
            { shieldReward += (float)Field(shield, "Reward"); shields++; }
            var whiteHeroes = (Array)Field(rig.State, "WhiteHeroes");
            return new Snapshot
            {
                Gate = gate,
                DarkSpent = (float)Field(rig.State, "DarkSpent"),
                WhiteSpent = (float)Field(rig.State, "WhiteSpent"),
                Adds = (int)Field(rig.State, "Adds"),
                Spears = (int)Field(rig.State, "Spears"),
                Illuminations = (int)Field(rig.State, "Illuminations"),
                WhiteTargets = (int)Field(rig.State, "WhiteTargets"),
                LastAddedPacket = (long)Field(rig.State, "LastAddedPacket"),
                NextNativeAdd = (float)Field(ink, "NextNativeAdd"),
                DwellHasEnemy = DwellHas(rig, rig.Enemy),
                WhiteHeroIsAlly = whiteHeroes.Length > 0 && ReferenceEquals(whiteHeroes.GetValue(0), rig.Ally),
                ShieldReward = shieldReward,
                Shields = shields,
            };
        }

        private static void AssertSame(Snapshot reused, Snapshot perCallback, string context)
        {
            Assert.True(reused.Gate, context + ": reused-verdict path must pass the gate");
            Assert.Equal(perCallback.Gate, reused.Gate);
            Assert.Equal(perCallback.DarkSpent, reused.DarkSpent);
            Assert.Equal(perCallback.WhiteSpent, reused.WhiteSpent);
            Assert.Equal(perCallback.Adds, reused.Adds);
            Assert.Equal(perCallback.Spears, reused.Spears);
            Assert.Equal(perCallback.Illuminations, reused.Illuminations);
            Assert.Equal(perCallback.WhiteTargets, reused.WhiteTargets);
            Assert.Equal(perCallback.LastAddedPacket, reused.LastAddedPacket);
            Assert.Equal(perCallback.NextNativeAdd, reused.NextNativeAdd);
            Assert.Equal(perCallback.DwellHasEnemy, reused.DwellHasEnemy);
            Assert.Equal(perCallback.WhiteHeroIsAlly, reused.WhiteHeroIsAlly);
            Assert.Equal(perCallback.ShieldReward, reused.ShieldReward);
            Assert.Equal(perCallback.Shields, reused.Shields);
        }

        private static void AssertUntouched(Snapshot snap, string context)
        {
            Assert.False(snap.Gate, context + ": the gate must reject the hit");
            Assert.Equal(0f, snap.DarkSpent);
            Assert.Equal(0f, snap.WhiteSpent);
            Assert.Equal(0, snap.Adds);
            Assert.Equal(0, snap.Spears);
            Assert.Equal(0, snap.Illuminations);
            Assert.Equal(0, snap.WhiteTargets);
            Assert.Equal(0L, snap.LastAddedPacket);
            Assert.Equal(0f, snap.NextNativeAdd);
            Assert.False(snap.DwellHasEnemy);
            Assert.False(snap.WhiteHeroIsAlly);
            Assert.Equal(0f, snap.ShieldReward);
            Assert.Equal(0, snap.Shields);
        }

        [Fact]
        public void Enemy_hit_with_the_reused_verdict_matches_the_per_callback_checks()
        {
            var reused = Create(DarkMoon);
            reused.Host.PrepareInkBeamHit(reused.Beam);
            Assert.True(HitPrepared(reused), "a fully valid hit must set the reused verdict");
            var perCallback = Create(DarkMoon);
            PrepareWithoutVerdict(perCallback);

            UnityEngine.Time.time = 10f;
            Focus(reused);
            bool firstGate = Gate(reused, true); EnemyHit(reused, 777);
            var firstReused = Snap(reused, true, firstGate);
            Focus(perCallback);
            bool firstOldGate = Gate(perCallback, true); EnemyHit(perCallback, 777);
            var firstOld = Snap(perCallback, true, firstOldGate);
            AssertSame(firstReused, firstOld, "first enemy hit");
            Assert.True(firstReused.DwellHasEnemy, "a gated-in enemy hit must record the dwell row");

            UnityEngine.Time.time = 25f;
            Focus(reused);
            bool secondGate = Gate(reused, true); EnemyHit(reused, 778);
            var secondReused = Snap(reused, true, secondGate);
            Focus(perCallback);
            bool secondOldGate = Gate(perCallback, true); EnemyHit(perCallback, 778);
            var secondOld = Snap(perCallback, true, secondOldGate);
            AssertSame(secondReused, secondOld, "second enemy hit");
        }

        [Fact]
        public void Ally_hit_with_the_reused_verdict_matches_the_per_callback_checks()
        {
            var reused = Create(WhiteNight);
            reused.Host.PrepareInkBeamHit(reused.Beam);
            Assert.True(HitPrepared(reused), "a fully valid hit must set the reused verdict");
            var perCallback = Create(WhiteNight);
            PrepareWithoutVerdict(perCallback);

            UnityEngine.Time.time = 10f;
            Focus(reused);
            bool firstGate = Gate(reused, false); AllyHit(reused);
            var firstReused = Snap(reused, false, firstGate);
            Focus(perCallback);
            bool firstOldGate = Gate(perCallback, false); AllyHit(perCallback);
            var firstOld = Snap(perCallback, false, firstOldGate);
            AssertSame(firstReused, firstOld, "first ally hit");
            Assert.True(firstReused.WhiteHeroIsAlly, "a gated-in ally hit must claim the heal target");

            UnityEngine.Time.time = 12f;
            Focus(reused);
            bool secondGate = Gate(reused, false); AllyHit(reused);
            var secondReused = Snap(reused, false, secondGate);
            Focus(perCallback);
            bool secondOldGate = Gate(perCallback, false); AllyHit(perCallback);
            var secondOld = Snap(perCallback, false, secondOldGate);
            AssertSame(secondReused, secondOld, "second ally hit");
            Assert.True(secondReused.WhiteSpent > 0f, "the ally hit must have spent the white budget");
        }

        // ゾーン遷移中の BossEnsure は false を返すと同時に ClearBossEffects でビーム状態を
        // 破棄する（本体の設計。遷移後は新しいビームで作り直す）。よってこの失敗例では、
        // フラグが立たないこと・ビームが外されること・破棄後の命中が両経路とも無処理で
        // 潜ることを確認する（破棄された状態からの「回復」は存在しない）。
        [Fact]
        public void BossEnsure_failure_leaves_the_verdict_unset_and_stops_the_hit()
        {
            var zone = SodRpg.Mod.NetworkedManagerBase<SodRpg.Mod.ZoneManager>.softInstance;
            var enemy = Create(DarkMoon);
            zone.isInAnyTransition = true;
            enemy.Host.PrepareInkBeamHit(enemy.Beam);
            Assert.False(HitPrepared(enemy), "a failed BossEnsure must not arm the reused verdict");
            Assert.True(BeamEnded(enemy), "a failed BossEnsure discards the beam state (ClearBossEffects)");
            AssertUntouched(Snap(enemy, true, Gate(enemy, true)), "enemy hit after a failed ensure");
            EnemyHit(enemy, 777);
            AssertUntouched(Snap(enemy, true, Gate(enemy, true)), "enemy hit after a failed ensure (after dispatch)");

            var enemyOld = Create(DarkMoon);
            zone.isInAnyTransition = true;
            PrepareWithoutVerdict(enemyOld);
            Assert.True(BeamEnded(enemyOld), "the per-callback path sees the same teardown");
            AssertUntouched(Snap(enemyOld, true, Gate(enemyOld, true)), "enemy hit after a failed ensure (per-callback checks)");
            EnemyHit(enemyOld, 777);
            AssertUntouched(Snap(enemyOld, true, Gate(enemyOld, true)), "enemy hit after a failed ensure (per-callback, after dispatch)");
        }

        private static bool BeamEnded(Rig rig)
        {
            var beams = (System.Collections.IDictionary)typeof(HostAuthority)
                .GetField("_inkBeams", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig.Host);
            return !beams.Contains(rig.Parent);
        }

        [Theory]
        [InlineData("beam")]
        [InlineData("parent")]
        [InlineData("knockedOut")]
        [InlineData("dead")]
        public void Mid_dispatch_loss_stops_the_hit_on_both_paths(string loss)
        {
            void Apply(Rig rig)
            {
                switch (loss)
                {
                    case "beam": rig.Beam.isActive = false; break;
                    case "parent": rig.Parent.isActive = false; break;
                    case "knockedOut": rig.Hero.isKnockedOut = true; break;
                    case "dead": rig.Hero.isActive = false; break;
                }
            }

            var enemyReused = Create(DarkMoon);
            enemyReused.Host.PrepareInkBeamHit(enemyReused.Beam);
            Assert.True(HitPrepared(enemyReused));
            Apply(enemyReused);
            var enemyOld = Create(DarkMoon);
            PrepareWithoutVerdict(enemyOld);
            Apply(enemyOld);
            AssertUntouched(Snap(enemyReused, true, Gate(enemyReused, true)), "enemy hit after losing " + loss);
            AssertUntouched(Snap(enemyOld, true, Gate(enemyOld, true)), "enemy hit after losing " + loss + " (per-callback checks)");
            EnemyHit(enemyReused, 777); EnemyHit(enemyOld, 777);
            AssertUntouched(Snap(enemyReused, true, Gate(enemyReused, true)), "enemy hit after losing " + loss + " (after dispatch)");
            AssertUntouched(Snap(enemyOld, true, Gate(enemyOld, true)), "enemy hit after losing " + loss + " (per-callback, after dispatch)");

            var allyReused = Create(WhiteNight);
            allyReused.Host.PrepareInkBeamHit(allyReused.Beam);
            Assert.True(HitPrepared(allyReused));
            Apply(allyReused);
            var allyOld = Create(WhiteNight);
            PrepareWithoutVerdict(allyOld);
            Apply(allyOld);
            AssertUntouched(Snap(allyReused, false, Gate(allyReused, false)), "ally hit after losing " + loss);
            AssertUntouched(Snap(allyOld, false, Gate(allyOld, false)), "ally hit after losing " + loss + " (per-callback checks)");
            AllyHit(allyReused); AllyHit(allyOld);
            AssertUntouched(Snap(allyReused, false, Gate(allyReused, false)), "ally hit after losing " + loss + " (after dispatch)");
            AssertUntouched(Snap(allyOld, false, Gate(allyOld, false)), "ally hit after losing " + loss + " (per-callback, after dispatch)");
        }

    }
}
