using System.Collections.Generic;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #439：宿敵化したボス（報告では「禁忌化したボス」）を討伐しても禁忌の核がドロップしない、という報告の再現。
    /// 仕様（CHANGELOG v1.31 系・Workshop の説明）：宿敵化したボスの撃破で禁忌の核を獲得できる
    /// （エリートの宿敵は因縁と追加報酬のみ）。因縁3段以上では核が1つ増える。
    /// </summary>
    public sealed class NemesisCoreDropIssue439Tests
    {
        private static Profile PlayerWithRun()
        {
            var p = Profile.CreateNew(17);
            p.Run = new RunState { RunId = "run-439", HeroKey = "default" };
            return p;
        }

        private static void AssertKilledNemesisBossDropsCore(Profile p, List<GameEvent> ev)
        {
            Assert.True(p.Material(Materials.NemesisCore) > 0,
                $"宿敵ボス撃破後の禁忌の核が 0（期待: {Forbidden.BossCores}）");
            Assert.Contains(ev, e => e.Text != null && e.Text.Contains("禁忌の核"));
        }

        [Fact]
        public void Killing_nemesis_boss_grants_forbidden_cores()
        {
            var p = PlayerWithRun();
            var ev = Rules.OnKill(p, MonsterTier.Boss, 10, nightmare: NightmareAffix.Nemesis, heroKey: "default");
            AssertKilledNemesisBossDropsCore(p, ev);
            Assert.Equal(Forbidden.BossCores, p.Material(Materials.NemesisCore));
        }

        [Fact]
        public void Killing_nemesis_boss_at_high_grudge_grants_bonus_core()
        {
            var p = PlayerWithRun();
            p.NemesisGrudge = Forbidden.GrudgeBonusCoreAt;
            Rules.OnKill(p, MonsterTier.Boss, 10, nightmare: NightmareAffix.Nemesis, heroKey: "default");
            Assert.Equal(Forbidden.BossCores + 1, p.Material(Materials.NemesisCore));
        }

        [Fact]
        public void Killing_nemesis_elite_grants_no_core_but_deepens_grudge()
        {
            var p = PlayerWithRun();
            Rules.OnKill(p, MonsterTier.MiniBoss, 10, nightmare: NightmareAffix.Nemesis, heroKey: "default");
            Assert.Equal(0, p.Material(Materials.NemesisCore));
            Assert.Equal(1, p.NemesisGrudge);
        }

        [Fact]
        public void Host_nemesis_flag_survives_fact_roundtrip_into_kill_rewards()
        {
            // ホストが記録した撃破情報（AuthoritativeRunKill）の宿敵の印が、精算時の撃破報酬へそのまま渡ること。
            // クライアントは自前の観測（nightmare=None）とホストの事実を突き合わせて精算する。
            // 伝送は DreamforgeMonsterKillMsg（FromFact/ToFact）を通るので、その往復も含めて確認する。
            var fact = new AuthoritativeRunKill("run-439", "evt-1", 77, 0, NightmareAffix.Nemesis, null, 1, "1",
                "Mon_Forest_BossDemon", false, 0);
            var msg = DreamforgeMonsterKillMsg.FromFact(fact, 1UL);
            var received = msg.ToFact();
            Assert.Equal(NightmareAffix.Nemesis, received.Nightmare);
            Assert.Equal("Mon_Forest_BossDemon", received.BossTypeName);

            var ledger = new KillClassificationLedger();
            var native = new PendingRunKill("run-439", 0, 0, MonsterTier.Boss, 10, NightmareAffix.None, null, "default");
            Assert.True(ledger.ObserveDeath(new PendingMonsterDeath(77, native, "1"), 0.0));
            Assert.True(ledger.ReceiveFact(received, 0.0));
            Assert.True(ledger.TryResolve(out var kill, 0.0));
            Assert.Equal(NightmareAffix.Nemesis, kill.Nightmare);
            Assert.Equal(MonsterTier.Boss, kill.Tier);

            var p = PlayerWithRun();
            var ev = Rules.OnKill(p, kill.Tier, kill.Level, kill.Nightmare, kill.HeroKey,
                bossTypeName: kill.BossTypeName);
            AssertKilledNemesisBossDropsCore(p, ev);
        }
    }
}
