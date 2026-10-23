using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>Azurak のイベント絞り込み（HostAuthority.BossAzurak.cs の抽出実行）。
    /// DispatchAzurakBoss の冒頭は、Azurak の区分が購読しないイベント種では BossMoves の
    /// 全走査をまとめて省く。その絞り込みが手書きの列挙ではなく実レジストリから起動時に
    /// 求まっていることを、生成の実ソース（BuildAzurakEventGate と AzurakEventGate）を
    /// NativePersistence.targets の CompileAzurakEventGate で抽出して確かめる。
    /// (1) フラグは BossProfiles.Moves の Azurak 区分（CreateAzurakMoves() の出力）の
    /// 全 Action（Actions[1] 以降も含む）の Event と MainHit に過不足なく一致すること。
    /// MainHit は Actions とは別に DispatchAzurakBoss 内の stage2 の命中カウンター分岐が
    /// 直に鍵として使うため常に含む。(2) CreateAzurakMoves() の全プロファイルについても
    /// 同じこと。Azurak の区分を CreateAzurakMoves() 以外から登録すると両者が食い違って
    /// 失敗する。(3) Azurak が購読しない種（被害ごとに飛ぶ NativeDamageDealt・
    /// ModeTransition・RewardInstance）はフラグが通さず、走査ごと省かれたままであること。</summary>
    public sealed class AzurakEventGateTests
    {
        // ---- 準備 ----

        // 抽出した実ソースの静的なフラグ（private のため反射で読む）。
        private static bool[] Gate()
        {
            var field = typeof(HostAuthority).GetField("AzurakEventGate", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (bool[])field.GetValue(null);
        }

        private static HashSet<BossEvent> SubscribedEvents(IEnumerable<BossMoveProfile> profiles)
        {
            var events = new HashSet<BossEvent>();
            foreach (var profile in profiles)
                for (int i = 0; i < profile.Actions.Count; i++)
                    events.Add(profile.Actions[i].Event);
            return events;
        }

        private static List<BossMoveProfile> RegistryAzurakProfiles()
            => BossProfiles.Moves.Where(p => p.SetId == BossProfiles.AzurakSetId).ToList();

        // フラグの全種について、期待する購読の有無と一致することを確かめる。
        private static void AssertGateMatches(HashSet<BossEvent> subscribed)
        {
            subscribed.Add(BossEvent.MainHit);
            var gate = Gate();
            foreach (BossEvent kind in Enum.GetValues(typeof(BossEvent)))
                Assert.True(gate[(int)kind] == subscribed.Contains(kind),
                    $"{kind}: フラグ={gate[(int)kind]} 期待={subscribed.Contains(kind)}");
        }

        // ---- 絞り込みの元 ----

        [Fact]
        public void Gate_admits_exactly_the_registry_Azurak_subscription()
        {
            var azurak = RegistryAzurakProfiles();
            Assert.NotEmpty(azurak);
            AssertGateMatches(SubscribedEvents(azurak));
        }

        [Fact]
        public void Gate_admits_exactly_the_CreateAzurakMoves_subscription()
        {
            var authored = BossProfiles.CreateAzurakMoves();
            Assert.NotEmpty(authored);
            // Azurak の区分は CreateAzurakMoves() だけが登録している。ほかの登録元が現れると
            // フラグ（Moves の Azurak 区分から作る）が取りこぼすため、名前の集合で一致を確かめる。
            Assert.Equal(authored.Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal),
                RegistryAzurakProfiles().Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal));
            AssertGateMatches(SubscribedEvents(authored));
        }

        // ---- 省く対象 ----

        [Fact]
        public void Unsubscribed_kinds_stay_skipped()
        {
            var gate = Gate();
            // 被害ごとに飛ぶ種（NativeDamageDealt）ほど走査の無駄が大きい。Azurak が購読しない
            // 種はフラグを通らないため、BossMoves の走査ごと省かれたままである。
            Assert.False(gate[(int)BossEvent.NativeDamageDealt], "NativeDamageDealt は Azurak の区分は購読しない");
            Assert.False(gate[(int)BossEvent.ModeTransition], "ModeTransition は Azurak の区分は購読しない");
            Assert.False(gate[(int)BossEvent.RewardInstance], "RewardInstance は Azurak の区分は購読しない");
        }
    }
}
