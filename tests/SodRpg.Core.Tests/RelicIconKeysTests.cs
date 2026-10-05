using System.Collections.Generic;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>固有品アイコンの読み分け（RelicIconKeys）。Mod 層はこの順で最初に見つかった画像を使う。</summary>
    public class RelicIconKeysTests
    {
        [Fact]
        public void Preference_PutsUniqueIconBeforeBase()
        {
            Assert.Equal(new[] { "uniques/set.boss_demon.weapon", "weapon.shield_maul" },
                RelicIconKeys.Preference("set.boss_demon.weapon", "weapon.shield_maul").ToArray());
        }

        [Fact]
        public void Preference_NonUniqueUsesBaseOnly()
        {
            Assert.Equal(new[] { "weapon.shield_maul" }, RelicIconKeys.Preference(null, "weapon.shield_maul").ToArray());
            Assert.Equal(new[] { "weapon.shield_maul" }, RelicIconKeys.Preference("", "weapon.shield_maul").ToArray());
        }

        [Fact]
        public void Preference_MissingUniqueFallsBackToBase()
        {
            // Mod 層と同じ選び方（先頭から最初に存在するキー）: 固有品画像が無ければ土台へ戻る。
            HashSet<string> files = new HashSet<string> { "weapon.shield_maul" };
            string picked = RelicIconKeys.Preference("set.boss_demon.weapon", "weapon.shield_maul")
                .FirstOrDefault(k => files.Contains(k));
            Assert.Equal("weapon.shield_maul", picked);
        }

        [Fact]
        public void Preference_PresentUniqueWins()
        {
            HashSet<string> files = new HashSet<string> { "uniques/set.boss_demon.weapon", "weapon.shield_maul" };
            string picked = RelicIconKeys.Preference("set.boss_demon.weapon", "weapon.shield_maul")
                .FirstOrDefault(k => files.Contains(k));
            Assert.Equal("uniques/set.boss_demon.weapon", picked);
        }

        [Fact]
        public void Preference_EmptyYieldsNothing()
        {
            Assert.Empty(RelicIconKeys.Preference(null, null).ToArray());
        }
    }
}
