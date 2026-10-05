using System;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #128：装備タブで「鍵をかける」が押せなくなっていた直し。
    /// 右の欄は高さに余りがなくなると末尾からはみ出るので、操作のボタン（装着・外す・鍵）は
    /// 詳細のスクロールより上に描かれる。これが下だと、見出しが増えてウィンドウの高さが
    /// 足りなくなったとき（v2.2.0 のインフィニティの行など）ボタンが枠の外へ出て見えなくなる。
    /// </summary>
    public class GearLockIssue128Tests
    {
        private static Relic StashedRelic(out Profile profile)
        {
            profile = Profile.CreateNew(128);
            var relic = Loot.RollRelic(new Rng(128), Rarity.Rare, 3, slot: Slot.Weapon);
            profile.Stash.Add(relic);
            return relic;
        }

        [Fact]
        public void Action_buttons_render_above_the_flexible_detail_scroll()
        {
            var relic = StashedRelic(out var profile);
            var ui = new DreamforgeUi(new RefundUiSession { Profile = profile }, "Hero_A");
            ui.DrawGear(-1);
            var ops = UnityEngine.GUILayout.Ops;
            int lockButton = ops.FindIndex(op => op.EndsWith("Lock") || op.EndsWith("鍵をかける"));
            int scroll = ops.IndexOf("scroll");
            Assert.True(lockButton >= 0, "lock button rendered: " + string.Join("|", ops));
            Assert.True(scroll >= 0, "detail scroll rendered: " + string.Join("|", ops));
            Assert.True(lockButton < scroll,
                "the lock button must render before the detail scroll; after a flexible scroll a short window clips it out of view");
        }

        [Fact]
        public void Lock_button_toggles_protection_and_unlock_button_releases_it()
        {
            var relic = StashedRelic(out var profile);
            var session = new RefundUiSession { Profile = profile };
            var ui = new DreamforgeUi(session, "Hero_A");

            // 装備していない遺物：[0]=装着する、[1]=鍵。
            ui.DrawGear(1);
            Assert.True(relic.Locked, "pressed lock button locks the relic");
            Assert.Throws<InvalidOperationException>(() => Rules.Salvage(profile, relic.Uid, session.Trades));
            Assert.DoesNotContain(relic.Uid, Rules.BulkSalvageCandidates(profile, session.Trades).Select(r => r.Uid));
            Assert.DoesNotContain(relic.Uid, Rules.TransmuteCandidates(profile, relic.Rarity, session.Trades).Select(r => r.Uid));

            ui.DrawGear(1);
            Assert.False(relic.Locked, "pressed lock button again unlocks the relic");
            Assert.NotNull(Rules.Salvage(profile, relic.Uid, session.Trades));
            Assert.Empty(profile.Stash);
        }

        [Fact]
        public void Lock_button_still_works_for_the_equipped_relic_and_survives_save_load()
        {
            var relic = StashedRelic(out var profile);
            foreach (var e in Rules.Equip(profile, "Hero_A", relic.Uid)) { }
            var session = new RefundUiSession { Profile = profile };
            var ui = new DreamforgeUi(session, "Hero_A");

            // 装備中の遺物：[0]=外す、[1]=鍵。
            ui.DrawGear(1);
            Assert.True(relic.Locked);

            // 「続きから」（保存・読み込み）でも鍵の状態は保たれ、保護も続く。
            var reloaded = ProfileCodec.Read(ProfileCodec.Write(profile), new List<string>());
            var restored = reloaded.FindStash(relic.Uid);
            Assert.True(restored.Locked);
            Assert.True(reloaded.IsEquippedAnywhere(relic.Uid));
            Assert.Throws<InvalidOperationException>(() => Rules.Salvage(reloaded, relic.Uid, session.Trades));
        }
    }
}
