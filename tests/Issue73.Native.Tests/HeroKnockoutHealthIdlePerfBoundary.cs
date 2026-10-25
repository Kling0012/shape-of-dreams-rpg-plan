// （#1204）NativeHeroKnockoutHealthIdlePerf（NativePerfPatches.cs を直接リンクして検証する。
// 撃倒中の英雄の体力固定で立つ _currentHealth の dirty ビットの同じ値の再送を省くパッチ）
// の受け口。本体 Dew.Core Se_HeroKnockedOut のうちパッチが触る範囲の模倣:
// ActiveLogicUpdate(float) の宣言と、被害者の体力を毎ロジックtickで固定値 0.01f へ書き戻す
// ピン。実機のピンは SetHealth(0.01f) で、EntityStatus の weaver 生成セッター
// Network_currentHealth を通るため、固定の書き戻しも（値が変わったぶん）体力の dirty ビットを
// 立てる。境界の共有 EntityStatus.SetHealth（NativeBoundary.cs）はビットの立たない素朴な
// 書き口を期待する他のリンクした検証が多いので、ここでは模倣の ActiveLogicUpdate から
// weaver セッター（StatsRatioIdlePerfBoundary.cs の partial 追加分）へ直接書き、回復チャンクも
// 同じセッター経由でテストから書く（ビットの立ち方まで本体と同じ意味にする）。
namespace SodRpg.Mod
{
    internal sealed class Se_HeroKnockedOut : StatusEffect
    {
        internal void ActiveLogicUpdate(float dt)
        {
            victim.Status.Network_currentHealth = 0.01f;
        }
    }
}
