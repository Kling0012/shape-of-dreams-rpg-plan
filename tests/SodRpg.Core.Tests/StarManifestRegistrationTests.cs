using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SodRpg.Core.Game;
using Xunit;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// starManifest の実行時登録（#1222、PR #1229 レビュー F1）。本体9人のツリーを登録1回につき1度だけ
    /// 索引化し、失敗した星をまとめて外す変更に伴い、受理される星・範囲外の除外・valueRef の解決・
    /// 同じ宣言の再読み込み（§9 の照合の前提）が前と同じ結果になることを確かめる。
    /// </summary>
    public sealed class StarManifestRegistrationTests
    {
        const string Hero = "Hero_Nova";

        static string Star(string id, string cluster, int order, string value) =>
            "{\"id\":\"" + id + "\",\"region\":\"memory\",\"cluster\":\"" + cluster + "\",\"shape\":\"chain\","
            + "\"anchor\":\"route.St_Q_NovaBlink." + order + "\",\"edges\":[],\"requires\":[],\"requiresAny\":[],"
            + "\"kind\":\"MemoryDamage\",\"memory\":\"St_Q_NovaBlink\",\"value\":" + value + ","
            + "\"nameJa\":\"星\",\"nameEn\":\"Star\"}";

        [Fact]
        public void RegistrationKeepsValidStarsDropsOutOfBoundAndResolvesValueRef()
        {
            foreach (string hero in StarClusters.GeneratedHeroes) StarClusters.RegisterGeneratedHero(hero);
            var linkStars = StarClusters.GeneratedHeroes.SelectMany(HeroSigils.TreeFor)
                .Where(n => n.LinkPerRank != null && n.LinkPerRank.Kind == LinkKind.MemoryDamage).ToArray();
            var reference = linkStars.First(n => n.LinkPerRank.Value > 0);
            decimal max = linkStars.Max(n => n.LinkPerRank.Value);
            string manifest = "{\"hero\":\"" + Hero + "\",\"stars\":["
                + Star("focus", "focus", 1, "1")
                + "," + Star("echo", "echo", 2, "{\"valueRef\":\"" + reference.Id + "/value\"}")
                + "," + Star("greed", "greed", 3, (max + 1m).ToString("0.###", CultureInfo.InvariantCulture))
                + "]}";
            string declaration = "{\"dreamforgeApi\":1,\"heroes\":[{\"key\":\"" + Hero + "\","
                + "\"name\":{\"ja\":\"ノヴァ\",\"en\":\"Nova\",\"zh\":\"诺瓦\"},\"melee\":false,"
                + "\"memories\":[{\"key\":\"St_Q_NovaBlink\",\"name\":{\"ja\":\"瞬き\",\"en\":\"Blink\",\"zh\":\"闪烁\"}}],"
                + "\"starManifest\":\"stars.json\"}]}";
            var warnings = new List<string>();
            var result = ExtendedHeroes.Register(declaration, "star-manifest-test", warnings.Add, relative => manifest);

            var heroResult = Assert.Single(result.Heroes);
            Assert.True(heroResult.Accepted, string.Join("\n", warnings));
            var tree = HeroSigils.TreeFor(Hero);
            Assert.Contains(tree, n => n.Id == "x." + Hero + ".focus");
            Assert.Contains(tree, n => n.Id == "x." + Hero + ".echo");
            Assert.DoesNotContain(tree, n => n.Id == "x." + Hero + ".greed");
            Assert.Equal(reference.LinkPerRank.Value, tree.First(n => n.Id == "x." + Hero + ".echo").LinkPerRank.Value);
            Assert.Equal(7, tree.Count(n => n.RouteId == "x." + Hero + ".route.St_Q_NovaBlink"));
            Assert.Contains(warnings, w => w.Contains("x." + Hero + ".greed") && w.Contains("star omitted"));

            // 同じ宣言の再読み込みは同一の受理ハッシュを返す（§9 の協力プレイ照合の前提）。
            var again = ExtendedHeroes.Register(declaration, "star-manifest-test", _ => { }, relative => manifest);
            Assert.True(Assert.Single(again.Heroes).Accepted);
        }
    }
}
