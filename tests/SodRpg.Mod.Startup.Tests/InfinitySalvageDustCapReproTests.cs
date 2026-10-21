using System;
using System.Linq;
using HarmonyLib;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Mod.Startup.Tests
{
    // Issue #1030: インフィニティで潜行しても、世界ごとの分解ダスト上限が回復しない。
    // ホストの取引入り口 OnTrade（HostAuthority.Trades.cs）を通して、上限を使い切った後に
    // 本体の currentZoneIndex が進まないまま潜行（Regenerate("delve")）した世界で、
    // 分解がもう一度支払われるかを確かめる。
    public sealed partial class InfinityLobbyStartTests
    {
        /// <summary>
        /// この世界の上限を使い切ってから、台帳と runId を維持したまま次の世界へ潜行すると、
        /// 本体の currentZoneIndex が同じままであっても、別の遺物の分解が dustcap で断られずに
        /// 支払われること。現状は TradeZoneIndex が本体の番号をそのまま返すため前の世界の計数が残り、
        /// 潜行後も断られ続ける（再現）。
        /// </summary>
        [Fact]
        public void DiveToTheNextInfinityWorldRestoresTheSalvageDustCapDespiteTheUnchangedNativeZoneIndex()
        {
            var (session, zone) = StartZoneTransition("issue1030-dustcap");
            var player = DewPlayer.local;
            player.dreamDust = 1000000; // 支払いの検証に入らない十分な残高
            var authority = HostAuthority.NativeInstance;
            var state = session.Profile.Run.Infinity;

            // ホストと同じ経路で手動分解を繰り返し、この世界の上限を使い切る。
            long token = 1; ulong uid = 1;
            string exhausted = null;
            for (int i = 0; i < 100000; i++)
            {
                var d = SalvageViaHost(authority, player, token++, uid++);
                if (d.Ok) continue;
                exhausted = d.Code;
                break;
            }
            Assert.Equal("dustcap", exhausted);
            Assert.Equal(Economy.DustCap(zone.currentZoneIndex, Economy.SalvageScale),
                (long)player.dreamDust - 1000000);

            // 台帳と runId を維持したまま次の世界へ潜る。移動先は別のゾーンだが、本体の番号は進まない。
            string worldBefore = zone.currentZone.name;
            zone.currentZone = SmallPrimusZone();
            state.FixedZoneId = zone.currentZone.name;
            PrepareDelve(state);
            DewResources.Zones.Add(zone.currentZone);
            DewResources.Zones.RemoveAll(z => z.name == "Zone_Foo");
            DewResources.Zones.Add(NativeZone("Zone_Other"));
            Assert.True(InfinityMode.Regenerate("delve"));
            Assert.Equal("Zone_Other", zone.currentZone.name);
            Assert.NotEqual(worldBefore, zone.currentZone.name);
            Assert.Equal(0, zone.currentZoneIndex);
            Assert.True(zone.LastTravelNoAdvance);
            Assert.Equal(1, state.GraphEpoch);
            Assert.Equal(session.Profile.Run.RunId, NetworkedManagerBase<GameManager>.softInstance.runId);

            // 潜行後の世界では、別の遺物の分解がもう一度支払われること。
            var after = SalvageViaHost(authority, player, token, uid);
            Assert.True(after.Ok, after.Code ?? after.Reason);
            Assert.True(after.Paid > 0);
            DewResources.Zones.Clear();
        }

        /// <summary>
        /// 潜らない限り上限は取り直せない。同じ世界での別の遺物の分解も、同じ取引idの再送も
        /// （記録済みの拒否を返すだけで）計数を戻さないこと。
        /// </summary>
        [Fact]
        public void StayingInTheSameInfinityWorldDoesNotRestoreTheExhaustedSalvageDustCap()
        {
            var (session, zone) = StartZoneTransition("issue1030-same-world");
            var player = DewPlayer.local;
            player.dreamDust = 1000000;
            var authority = HostAuthority.NativeInstance;

            long token = 1; ulong uid = 1;
            for (int i = 0; i < 100000 && SalvageViaHost(authority, player, token, uid).Ok; i++)
            {
                token++; uid++;
            }
            var refused = SalvageViaHost(authority, player, token, uid);
            Assert.False(refused.Ok);
            Assert.Equal("dustcap", refused.Code);

            // 同じ取引idの再送は、同じ拒否を返すだけ。
            var resent = SalvageViaHost(authority, player, token, uid);
            Assert.False(resent.Ok);
            Assert.Equal("dustcap", resent.Code);
            DewResources.Zones.Clear();
        }

        /// <summary>
        /// ホストの OnTrade に手動分解（SalvageForDust）を送り、結果の受理と実際の支払い量を返す。
        /// 台帳の重複排除に効くように uid は呼び出しごとに変えること。
        /// </summary>
        private static (bool Ok, string Code, string Reason, int Paid) SalvageViaHost(
            HostAuthority authority, DewPlayer player, long token, ulong uid)
        {
            var pending = new PendingTrade
            {
                Token = token,
                Kind = TradeKind.SalvageForDust,
                Uid = TradeWire.UnpackUid(uid),
                Rarity = (int)Rarity.Epic,
                Enhance = 1,
            };
            TradeWire.Encode(pending, out int spendGold, out int spendDust, out int earnDust);
            int dustBefore = player.dreamDust;
            AccessTools.Method(typeof(HostAuthority), "OnTrade").Invoke(authority, new object[]
            {
                new DreamforgeTradeMsg
                {
                    token = token, spendGold = spendGold, spendDust = spendDust, earnDust = earnDust,
                    protocol = Protocol.Version, runId = NetworkedManagerBase<GameManager>.softInstance.runId,
                },
                player,
            });
            var result = Actor.SentToClients
                .Where(t => t.player == player && t.message is DreamforgeTradeResultMsg)
                .Select(t => (DreamforgeTradeResultMsg)t.message)
                .LastOrDefault(r => r.token == token);
            Assert.NotNull(result);
            TradeWire.SplitReason(result.reason, out string code, out _);
            return (result.ok, code, result.reason, player.dreamDust - dustBefore);
        }
    }
}
