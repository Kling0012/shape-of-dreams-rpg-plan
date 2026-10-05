using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #97: 本体の中断保存に合わせて、取引の台帳（判定と墓標）も同じ時点へ保存・復元する。
    /// 復元した権威は保存時の世代と台帳識別子を引き継ぐため、巻き戻り前に実行した取引idの再送は
    /// 「記録済みの結果の再送」として扱われ、通貨は二重に動かない。無効なデータは一部も復元しない。
    /// </summary>
    public class TradeAuthorityCheckpointTests
    {
        private static TradeRequest Salvage(long token, ulong uid) =>
            new TradeRequest { Token = token, Kind = TradeKind.SalvageForDust, Rarity = (int)Rarity.Epic, Enhance = 1, SalvageUid = uid };

        [Fact]
        public void Checkpoint_restores_the_saved_generation_so_resends_replay_instead_of_reexecuting()
        {
            var saved = new TradeAuthority();
            var first = saved.Evaluate("player-guid", "run", Salvage(7, 42), 0, 0);
            Assert.True(first.Ok);
            Assert.False(first.Replayed);
            long ledger = first.LedgerId;
            string checkpoint = saved.CaptureCheckpoint();

            // 巻き戻り再開: 新しく作った権威が、保存時点の世代・台帳識別子へ戻る。
            var resumed = new TradeAuthority();
            resumed.RestoreCheckpoint(checkpoint);
            Assert.Equal(ledger, resumed.LedgerIdOf("player-guid", "run"));

            // 巻き戻り前に実行済みの取引idの再送は記録済みの結果を返すだけ（通貨は動かない）。
            var replay = resumed.Evaluate("player-guid", "run", Salvage(7, 42), 0, 0);
            Assert.True(replay.Ok);
            Assert.True(replay.Replayed);
            Assert.Equal(ledger, replay.LedgerId);
            Assert.Equal(first.EarnDust, replay.EarnDust);

            // 同じ遠征で同じ遺物の再分解も、保存時点で実行済みなら受け付けない。
            Assert.False(resumed.Evaluate("player-guid", "run", Salvage(9, 42), 0, 0).Ok);

            // 保存以降の新しい取引idは通常どおり裁かれ、保存し直せる。
            var next = resumed.Evaluate("player-guid", "run", Salvage(8, 43), 0, 0);
            Assert.True(next.Ok);
            Assert.False(next.Replayed);
            resumed.RestoreCheckpoint(resumed.CaptureCheckpoint());
            Assert.True(resumed.Evaluate("player-guid", "run", Salvage(8, 43), 0, 0).Replayed);
        }

        [Fact]
        public void Invalid_checkpoints_throw_and_never_leave_a_partially_restored_ledger()
        {
            var authority = new TradeAuthority();
            var before = authority.Evaluate("player-guid", "run", Salvage(1, 1), 0, 0);
            Assert.True(before.Ok);

            Assert.Throws<LedgerFormatException>(() => authority.RestoreCheckpoint("not-an-object"));
            Assert.Throws<LedgerFormatException>(() => authority.RestoreCheckpoint("{\"version\":2}"));
            Assert.Throws<LedgerFormatException>(() => authority.RestoreCheckpoint(
                "{\"version\":1,\"generation\":1,\"serial\":1,\"players\":[" +
                "{\"key\":\"k\",\"id\":1,\"floors\":[],\"floorOverflow\":false,\"executed\":[" +
                "{\"token\":5,\"fingerprint\":\"f\",\"ok\":true,\"replayed\":false,\"gold\":0,\"dust\":0,\"earn\":0,\"ledger\":2}]," +
                "\"cancelled\":[],\"salvage\":[]}]}")); // 台帳識別子が一致しない実行記録

            // 失敗のあとも権威は壊れておらず、元の台帳で裁き続ける。
            var still = authority.Evaluate("player-guid", "run", Salvage(1, 1), 0, 0);
            Assert.True(still.Ok);
            Assert.True(still.Replayed);
        }
    }
}
