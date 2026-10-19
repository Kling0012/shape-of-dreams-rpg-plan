// （#876）NativeStatsRatioIdlePerf（ステータス再計算の体力・マナの比率再書き込みノイズの
// 再送を省くパッチ。NativePerfPatches.cs を直接リンクして検証する）が typeof と Prefix/Postfix
// で参照する本体 EntityStatus のマナ側の境界。NativeBoundary.cs の EntityStatus（partial）に
// 対する追加分で、実機 Dew.Core r1.4.0.13 の EntityStatus のうちパッチが読む範囲を転写する:
//
//   - currentMana / maxMana の同期字段と、再計算のたびに baseMaxMana から作り直される最大値。
//   - weaver 生成の Network_currentHealth / Network_currentMana セッター。実機は
//     GeneratedSyncVarSetter(value, ref field, <bit>, null) の形で、値の比較
//     （SyncVarEqual。float.Equals と同じく NaN==NaN・0==-0 を等値とみたす）を通ったときだけ
//     字段を書き、同期ダーティビットを OR で立てる。ここでは本体と同じく
//     NetworkBehaviour.GeneratedSyncVarSetter（NativeBoundary.cs の境界実装）を
//     第4引数（フック）に null を渡して呼ぶ。これにより Prepare のフック検査
//     （GeneratedSyncVarSetter 呼び出しの直前が ldnull であること）も実機と同じ IL 形で通る。
//
// 実機のビット値は体力 0x80・マナ 0x100 だが、C# コンパイラは int32 のビット模様に収まる
// 整数定数を ldc.i4 + conv.i8 で出すため、そのまま書くと IL に ldc.i8 が1つも残らない
// （実機の weaver は IL を直接書くため ldc.i8 になる）。そこで境界では int32 のビット模様の
// 範囲外のビット（0x200000000・0x100000000。C# コンパイラが ldc.i8 のまま出す）を使う。
// パッチは「セッターの IL 上の唯一の ldc.i8 と、セッターが実際に立てるビットの一致」だけを
// 検査するため、この差は検証の対象（書き戻しとビットの落とし方の論理）に影響しない。
//
// 体力側の backing field（_currentHealth）と currentHealth プロパティは NativeBoundary.cs 側の定義を使う。
namespace SodRpg.Mod
{
    internal partial class EntityStatus
    {
        private float _currentMana = 100f;
        public float currentMana { get => _currentMana; set => _currentMana = value; }
        // 本体と同じくプロパティにする: Prepare は maxMana を AccessTools.PropertyGetter で解決する。
        public float maxMana { get; set; } = 100;
        // 再計算のたびに maxMana が作り直される元の値（体力側の baseMaxHealth と同じ役割）。
        public float baseMaxMana = 100f;

        // 実機の weaver 生成セッターと同じ形: GeneratedSyncVarSetter(value, ref field, bit, null)。
        // 等値（SyncVarEqual 相当）なら何もせず、違う値なら字段を書いて体力のダーティビットを立てる。
        public float Network_currentHealth
        {
            get => _currentHealth;
            set => GeneratedSyncVarSetter(value, ref _currentHealth, 0x200000000UL, null);
        }

        // マナ側の対（体力と別のビット）。
        public float Network_currentMana
        {
            get => _currentMana;
            set => GeneratedSyncVarSetter(value, ref _currentMana, 0x100000000UL, null);
        }
    }
}
