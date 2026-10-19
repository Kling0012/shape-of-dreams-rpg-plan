using System.Reflection;
using SodRpg.Core.Game;
using SodRpg.Mod;
using Xunit;

namespace Issue73.Native.Tests
{
    /// <summary>
    /// perf-loop/20261019-1346 の前提の検証。BeginAttributedDamagePacket から被弾ごとの
    /// 2度目の祖先走査（!IsPairReactionSource）を省いたのは、TryGetMemoryActivation が
    /// true を返す連鎖では IsPairReactionSource が必ず false になるから。このハーネスには
    /// 本体側の TryGetMemoryActivation（祖先走査）と IsPairReactionSourceNode（両走査が
    /// 共通で使う述語）がリンクされているため、Gem・要素状態効果・gem 付き AbilityInstance
    /// を祖先に持つ場合とどれも持たない場合について、承認されるのがどれも持たない場合だけ
    /// であることを実際に確かめる。IsPairReactionSource の走査本体は
    /// HostAuthority.PairCombos.cs にありこのアセンブリには入らないため、同じ述語で
    /// true になる連鎖を SodRpg.Core.Tests 側（PairComboNativeTests）のテストと合わせて覆う。
    /// </summary>
    public sealed class MemoryActivationPairReactionPremiseTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Theory]
        [InlineData("none")]
        [InlineData("gem")]
        [InlineData("element")]
        [InlineData("gemAbility")]
        public void MemoryActivationAdmitsOnlyChainsWithoutAnyPairReactionSourceNode(string kind)
        {
            var host = new HostAuthority();
            var hero = new Hero();
            var skill = new TestQ { owner = hero };
            hero.Skill.Slots[HeroSkillLocation.Q] = skill;
            host.RefreshMemoryAttributionEquipment(hero);
            var ledger = (MemoryActivationAttribution)typeof(HostAuthority)
                .GetField("_memoryAttribution", Hidden).GetValue(host);
            var instance = new AbilityInstance();
            var dirty = kind == "gem" ? (Actor)new Gem()
                : kind == "element" ? new ElementalStatusEffect()
                : kind == "gemAbility" ? new AbilityInstance { gem = new Gem() }
                : null;
            if (dirty == null) instance.parentActor = skill;
            else { instance.parentActor = dirty; dirty.parentActor = skill; }
            skill.parentActor = hero;
            var identity = ledger.BeginActivation(hero.GetInstanceID(), nameof(TestQ));
            ledger.BindInstance(instance.GetInstanceID(), identity);

            bool admitted = host.TryGetMemoryActivation(instance, out var found);

            if (kind == "none")
            {
                Assert.True(admitted, "どのペア反応元も持たない連鎖は記憶帰属として承認される");
                Assert.Equal(identity.ActivationId, found.ActivationId);
                for (var node = (Actor)instance; node != null; node = node.parentActor)
                    Assert.False(HostAuthority.IsPairReactionSourceNode(node),
                        "承認された連鎖の祖先にペア反応元が残っていてはならない（IsPairReactionSource が true になる）");
            }
            else
            {
                Assert.False(admitted, kind + " を祖先に持つ被弾は記憶帰属として承認されない");
                Assert.True(HostAuthority.IsPairReactionSourceNode(dirty), kind + " はペア反応元のノードである");
            }
        }
    }
}
