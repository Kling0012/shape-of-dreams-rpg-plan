using System;
using System.Collections.Generic;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>#69: 星図ポイント・熟練度の通知には旅人の内部名（Hero_*）ではなく表示名を出す。</summary>
    public sealed class NotificationHeroNameTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Star_point_and_mastery_notifications_use_the_display_name(bool japanese)
        {
            WithLanguage(japanese, () =>
            {
                var p = Profile.CreateNew(2);
                Rules.BeginRun(p, "n");
                var events = new List<GameEvent>();
                // 100撃破で星図ポイント1（56xp）と熟練度1（100撃破）の両方の通知が出る
                for (int i = 0; i < 100; i++)
                    events.AddRange(Rules.OnKill(p, MonsterTier.Lesser, 1, NightmareAffix.None, "Hero_Vesper"));
                Assert.Contains(events, e => e.Kind == EventKind.LevelUp && e.Text ==
                    (japanese ? "ヴェスパーの星図ポイントが1増えました。" : "Vesper earned 1 star map point(s)."));
                Assert.Contains(events, e => e.Kind == EventKind.LevelUp && e.Text ==
                    (japanese ? "ヴェスパーの熟練度が1「見習い」に上がりました。" : "Vesper mastery 1 \"Apprentice\""));
                Assert.DoesNotContain(events, e => e.Text.Contains("Hero_Vesper")); // 内部名はそのまま出さない
            });
        }

        private static void WithLanguage(bool japanese, Action action)
        {
            bool previous = Loc.Japanese;
            try { Loc.Japanese = japanese; action(); }
            finally { Loc.Japanese = previous; }
        }
    }
}
