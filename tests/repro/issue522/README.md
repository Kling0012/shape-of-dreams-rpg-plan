# Issue #522 修正前の再現

製品コードを変更せず、射程変換の表示・協力設定の参照・説明不足・命中時の余分な処理を再現する。

```sh
python3 tests/repro/issue522/run.py
```

.NET SDK と Python 3 が必要。ゲームの起動・DLL 配置は不要。`DOTNET_ROLL_FORWARD` が未設定の場合は `Major` を使う。NuGet キャッシュは呼び出し元の環境変数を引き継ぎ、worktree 内には設定しない。

`artifacts/<UTC時刻>/repro.log` に実行出力、`results.json` にコマンド・終了コード・各ケースの結果を保存する。ケース失敗は終了コード1、ビルド失敗などでケース結果を取得できない場合は2。成果物・bin・obj はコミットしない。

## 方法と限界

- Core の `Content.FormatStat`、`CoopDistance` と、Mod の `TravelerCombatNative`、`CoopDistanceNative`、`HostAuthority.MeleeCoop`、`DreamforgeConfig` を直接ビルドする。
- `extract.py` は本番の `OnMemoryDamage`、`MemorySource`、`LinkMarks`、hello 型・ホスト返信 initializer、および `Draw` の表示文脈設定までの冒頭をそのまま取り出す。アルゴリズムの複製で期待値を再計算するテストではない。抽出できない場合はビルド失敗にする。
- ゲームの Actor・位置・設定属性などだけを `NativeApi.cs` の最小限の代役に置き換える。`Draw` は描画前で切り出しており、実画面のレンダリング試験ではない。
- ホスト設定のテストは、同じ位置と逆の設定でホストの適用結果と UI の計算を比較し、hello 返信に設定差が含まれないことを別途確認する。実際の RPC の送受信、クライアントの `OnHello`、旧版との相互接続は実行しない。設定同期の修正後は、実際の受信処理を通すようハーネスを追随させる必要がある。
- `asset` 読み出し回数と `parentActor` アクセス回数を計測する。フレーム時間・GC・実機性能の測定ではない。

## 修正前の結果（2026-10-16）

実行環境: .NET SDK 10.0.111、net8.0 を Major roll-forward で実行、`NUGET_PACKAGES=/home/wang/.cache/gh-auto/nuget`。

**19件中15件失敗・4件成功、終了コード1。コンパイル成功後のアサーション失敗で再現した。**

| ケース | 修正前の観測 |
| --- | --- |
| 攻撃速度70%・射程30%、日英 | 適用後80%で増分10%。表示は +30% |
| 攻撃速度80%・射程30%、日英 | 適用後80%で増分0%。表示は +30% |
| 距離減衰OFFの説明、日英 | 14mで実効果0だが、説明文は有効時の10〜18mと2秒猶予のみ |
| 崩し・近接防御・変換の設定 | 崩し+12%/3秒、近接防御12%/3m、攻撃速度上限80%の説明がない |
| hello のホスト設定 | 4機能をすべてON/OFFにしても返信が同一 |
| ホスト減衰OFF・ローカルON、14m | ホスト係数0、UI係数0.5 |
| ホスト変換OFF・ローカルON | ホストは射程30%を保持、UIは攻撃速度へ変換した表示を選択 |
| 崩しOFFまたは遠距離旅人 | 必要な MemorySource の128段に加えて、IsNativeBasicDamage でも128段を走査（計256回） |
| 近接防御の命中処理 | スキャンで資格を確定した後も、命中ごとに攻撃アセットを1回読み直す |

対照4件は、4人の近接コンポーネント判定、変換・崩し・近接防御の基本発動、日英の上限未到達表示。既存の `MeleeCoopNativeTests` も別途17件成功（下記）。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  --filter FullyQualifiedName~MeleeCoopNativeTests \
  -p:PythonCommand=python3 -p:WarningLevel=0 --logger 'console;verbosity=minimal'
```

## 近接判定に関する資料確認

読み取り専用の本体資料では `Dew.Core/TriggerConfig.cs:43` の `spawnedInstanceRef` は `AssetRef<AbilityInstance>`。`Dew.Core/AssetRef.cs:29` の `asset` は `T` を返し、76〜102行で取得・変換する。`GameObject` を直接返すことはこの型契約からは確認できない。既存テストの `object asset` だけを根拠に、GameObject を返す代役を作って実機の不具合と断定していない。

資料の所在: `~/dev/sod-gamedata/decompiled/sod-decomp/Dew.Core/`。`~/dev/sod-gamedata/Shape of Dreams/Shape of Dreams_Data/Managed/Dew.Contents.dll` 内に4つの旅人名 `Hero_Vesper` / `Hero_Husk` / `Hero_Mist` / `Hero_Aurena` があることも `strings` で確認した。これらは稼働中の prefab・攻撃構成や、実際の発動を確認した証拠ではない。

## 判断したこと

- この工程はバグ再現に限定し、修正は次工程に残す。受け付け審査の懸念は、上記の再現可能な部分と下記の未実行部分を区別して引き継ぐ。
- 数値説明の確認先は triage の方針どおり既存の設定項目とした。距離減衰OFFの説明は既存の `CoopDistance.Description` を確認する。
- 近接判定の GameObject 仮説は本体の型契約と一致する根拠が得られなかったため、再現成功の対象に含めない。
- 上限表示は、Issue の具体例どおり「変換前70%＋射程30%から実効+10%」を正とする。上限未到達の対照も実行した。倍率は現行の1を使用し、定数の改変はしない。

## 未実行・未解決

- ゲーム内での4旅人のアセットと変換・崩し・近接防御の発動: ゲームを起動できる実機がないため未実行。近接判定の不発は未再現。
- 実マルチプレイ、設定の受信、旧版の任意欄・未受信フォールバック、切断・再接続: 未実行。同期追加後の検証が必要。
- 近接防御のキャッシュ化による最大0.25秒の切り替え遅延: 未実行。この工程ではキャッシュ化していない。
- 設定画面の1920x1080表示・スクリーンショット: 未実行。製品UIを変更していない。
- Mod全体のビルド・全件/低速テスト・ゲーム内 `dreamforge_simkill`: 再現工程の範囲外として未実行。

現在の再現ハーネスは次工程への証拠であり、設定同期や表示文脈のAPIが変わった際は、抽出箇所・代役を修正内容に合わせて追随させる。
