using System;
using System.Linq;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// #147：1080p で「変更と払い戻しを一括承認」パネルの承認／取消ボタンが画面の外に出ていた直し。
    /// タブの中身（一覧や左欄）は見出しとの積算でウィンドウより高くなることがあり、IMGUI は
    /// はみ出しを下端から順に切る。だから承認・取消ボタンとステータス行は、タブの中身より先
    /// （見出しの直後）に描く。末尾に描くと一番下のボタンから消える（#128 の「鍵が押せない」と同じ型）。
    /// 併せて工房タブの「解放」ボタンはスクロールの中に入れ、強化の行が増えても枠の外に出ないようにする。
    /// </summary>
    public class RefundPanelIssue147Tests : IDisposable
    {
        private const string Hero = "Hero_Cetus", EffectId = "test.147.echo", DependentId = "test.147.leaf";

        private readonly DreamforgeUi _ui;

        public RefundPanelIssue147Tests()
        {
            var profile = Profile.CreateNew(147);
            profile.Hero(Hero).StarXp = StarProgression.TotalXpForPoints(StarProgression.MaxPoints);
            var effect = new TalentDef(EffectId, Line.Offense, new Txt("試験", "Test"), Stat.Armor, 0, 2) {
                HeroKey = Hero, RankCost = 3, RouteMemory = "St_D_IcyVeins",
                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Echo, Value = 5m }
            };
            var dependent = new TalentDef(DependentId, Line.Offense, new Txt("試験", "Test"), Stat.Armor, 1, 1) {
                HeroKey = Hero, RankCost = 2,
                AuthoredStar = new AuthoredStarDef { RequiredStarIds = new[] { EffectId } }
            };
            var relic = Loot.RollRelic(new Rng(147), Rarity.Common, 1, slot: Slot.Weapon);
            profile.Stash.Add(relic);
            var tree = HeroSigils.TreeFor(Hero).Where(t => t.Cluster == null).Concat(new[] { effect, dependent }).ToArray();
            Rules.RegisterAllocationValidation(Hero, new EffectiveAllocationValidation(tree, new EffectiveAllocationPolicy {
                PermanentDisables = new[] { new AllocationDisableRule { EquippedUid = relic.Uid, StarIds = new[] { EffectId } } }
            }));
            Rules.AddTalentRank(profile, Hero, EffectId);
            Rules.AddTalentRank(profile, Hero, EffectId);
            Rules.AddTalentRank(profile, Hero, DependentId);
            var proposal = Assert.Throws<AllocationValidationException>(() => Rules.Equip(profile, Hero, relic.Uid));
            var session = new RefundUiSession { Profile = profile };
            _ui = new DreamforgeUi(session, Hero);
            _ui.OfferRefund(proposal, true, relic.Uid);
        }

        public void Dispose() => Rules.RegisterAllocationValidation(Hero, null);

        [Fact]
        public void Refund_buttons_render_above_the_tab_content()
        {
            _ui.DrawMenu(0, -1); // 装備タブ・ロビー。中身が最も高くなる組合せ。
            var ops = UnityEngine.GUILayout.Ops;
            int content = ops.IndexOf("tab-content:0");
            int approve = ops.FindIndex(op => op.EndsWith("変更と全払い戻しを承認") || op.EndsWith("Approve change and all refunds"));
            int cancel = ops.FindIndex(op => op.EndsWith("取り消す") || op.EndsWith("Cancel"));
            Assert.True(content >= 0, "gear tab content rendered: " + string.Join("|", ops));
            Assert.True(approve >= 0, "approve button rendered: " + string.Join("|", ops));
            Assert.True(cancel >= 0, "cancel button rendered: " + string.Join("|", ops));
            Assert.True(approve < content && cancel < content,
                "the refund buttons must render above the tab content; below it the overflow clips them out of the window first");
        }

        [Fact]
        public void Status_line_renders_above_the_tab_content()
        {
            _ui.DrawMenu(0, -1);
            var ops = UnityEngine.GUILayout.Ops;
            int content = ops.IndexOf("tab-content:0");
            // OfferRefund が状態文（拒否された理由）をセットしている。それが中身より先に描かれること。
            Assert.True(_ui.Status != null, "an offered refund sets a status message");
            int status = ops.IndexOf("label:" + _ui.Status);
            Assert.True(content >= 0, "gear tab content rendered: " + string.Join("|", ops));
            Assert.True(status >= 0, "status line rendered: " + string.Join("|", ops));
            Assert.True(status < content,
                "the status line must render above the tab content; below it a tall tab clips the feedback away");
        }

        [Fact]
        public void Workshop_unlock_buttons_render_inside_a_scroll()
        {
            var menu = new DreamforgeUi(new RefundUiSession { Profile = Profile.CreateNew(148) }, "Hero_A");
            menu.DrawMenu(3, -1);
            var ops = UnityEngine.GUILayout.Ops;
            int scroll = ops.IndexOf("scroll");
            int endScroll = ops.IndexOf("endscroll");
            Assert.True(scroll >= 0, "workshop rows render inside a scroll so more upgrades cannot push the unlock buttons out: " + string.Join("|", ops));
            int unlock = ops.FindIndex(op => op.Contains("解放（欠片") || op.Contains("Unlock ("));
            Assert.True(unlock >= 0, "unlock button rendered: " + string.Join("|", ops));
            Assert.True(scroll < unlock && unlock < endScroll, "the unlock buttons must stay inside the workshop scroll");
        }
    }
}
