using System;
using System.Reflection;
using SodRpg.Core.Game;
using Xunit;
using Xunit.Sdk;

namespace SodRpg.Core.Tests
{
    /// <summary>
    /// すべてのテストを「どのクラスも StarClusters に何も登録していない基準の状態」から始める（アセンブリ属性）。
    /// RegisterAllGenerated / RegisterGeneratedHero / RegisterAuthored を後始末せず終えるテストが
    /// 生成済みツリーをプロセスに残し、クラス分割実行（#404）でクラス順が変わると後発クラスの
    /// ベースライン前提（HeroSigils.TreeFor / AllocationValidationForHero）が壊れるのが原因。
    /// xUnit の BeforeAfterTestAttribute はテストごとに（コンストラクタの後に）走るので、
    /// ここで Installed・移行規則・検証キャッシュを基準へ戻す。基準状態は「最初のテストの前」
    /// （= 登録なし。プロセス起動時はどのテストも登録していないため、空登録への戻しがそれに等しい）。
    /// 登録が要るテストはテスト本体の中で自分で登録するので影響を受けない。
    /// IClassFixture を使うクラスはフィクスチャがクラス単位で登録と後始末を行うため対象外
    /// （ここで戻すとフィクスチャの登録を消してしまう）。
    /// </summary>
    public sealed class SharedStarRegistryResetAttribute : BeforeAfterTestAttribute
    {
        public override void Before(MethodInfo methodUnderTest)
        {
            for (Type type = methodUnderTest.DeclaringType; type != null; type = type.DeclaringType)
                foreach (var face in type.GetInterfaces())
                    if (face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IClassFixture<>))
                        return;
            try
            {
                foreach (string hero in StarClusters.GeneratedHeroes)
                    if (StarClusters.TryGetRegisteredTree(hero, out _))
                        // 空の登録は Installed と移行規則を外し、検証キャッシュをベースラインの内容で上書きする。
                        StarClusters.RegisterAuthored(hero, Array.Empty<AuthoredStarDef>());
                Rules.RegisterAllocationValidation(null, null); // "default" キーの検証キャッシュ
            }
            catch (Exception)
            {
                // fail-soft: 戻しに失敗してもテストの実行は続ける（この仕組みがない頃と同じ順序依存に戻るだけ）。
            }
        }
    }
}
