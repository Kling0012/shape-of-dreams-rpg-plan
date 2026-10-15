# Issue #457 バグ再現記録

## 結果

修正前の本番 `Content.Uniques` と `Links.Satisfied` を使用した再現テスト2件が、期待する到達可能性を満たさず失敗した（終了コード1、2件実行／2件失敗）。製品コード・装備条件・数値・説明文は変更していない。

- 対象はセット品ではなく固有品「傑作の連剣」`unique.link_mist_masterwork`。
- 条件は `Hero_Mist`、`St_D_AstridsMasterpieceEnGarde`、`St_D_AstridsMasterpiecePriorite` のすべて。
- Mistの出発時の合法な8構成（Identity 2択 × Q 2択 × R 2択、Movementは固定）で発動数0。
- 診断として不可能なIdentity2種類の同時入力を渡した場合だけ発動した。
- 全固有品・セット連携393件にIdentity／Movement各1枠の必要制約を適用した検査で、同型の到達不能条件を計4件確認した。

| 固有品ID | 名前 | 同時に要求されるIdentity |
| --- | --- | --- |
| `unique.link_mist_masterwork` | 傑作の連剣 | `St_D_AstridsMasterpieceEnGarde` + `St_D_AstridsMasterpiecePriorite` |
| `unique.link_vesper_resolve_mercy` | 決意と慈悲 | `St_D_Resolve` + `St_D_MercyOfEl` |
| `unique.link_lacerta_powder_tap` | 双発の火薬庫 | `St_D_DoubleTap` + `St_D_SalamanderPowder` |
| `unique.link_husk_wind_scar` | 風傷の殺陣 | `St_D_TheKillingFlow` + `St_D_ScarOfTheWind` |

## 再現方法

リポジトリルートで次を実行する。対象は追加した再現クラスだけで、全スイートは実行しない。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  -c Release --filter FullyQualifiedName~Issue457LinkReachabilityTests \
  --logger 'console;verbosity=detailed' \
  -p:PythonCommand=python3 -p:NuGetAudit=false \
  -p:GameDir='/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams'
```

2026-10-16のサンドボックスで上記コマンドを実行し、次の結果を確認した。

```text
Masterwork_flurry_can_activate_in_a_legal_Mist_loadout [FAIL]
Legal Mist loadouts: 8; active: 0
Diagnostic only, impossible dual-Identity input: active=True
Every_relic_link_can_be_satisfied_with_at_most_one_Identity_and_one_Movement [FAIL]
Checked relic/set links: 393; unreachable: 4
Test Run Failed.
Total tests: 2
     Failed: 2
```

先行するビルド済みアセンブリの実行でも同じ失敗を確認し、`--logger 'trx;LogFileName=issue457-repro.trx' --results-directory /tmp/issue457-repro-results` でTRXを保存した。TRXは環境内の補助資料であり、追試は上記コマンドとコミットした再現テストから行える。

初回はNuGet脆弱性データの外部照会失敗 `NU1900` が警告のエラー扱いとなり復元を停止した。コマンド引数の `NuGetAudit=false` で当該照会を省いた。続く実行は.NET 8ランタイム不在でテストホストを起動できなかったため、`DOTNET_ROLL_FORWARD=Major` で利用可能な.NET 10.0.11を使用した。プロジェクトやCIの設定は変更していない。これらの環境エラーはバグ再現の根拠に含めず、その後のテストアサーション失敗を根拠とする。

## 根拠と検査の範囲

- 本番定義：`src/SodRpg.Core/Game/Content.cs` の上記4固有品。
- 本番判定：`src/SodRpg.Core/Game/Links.cs` の `Satisfied` は `Requires` 全要素に `RequirementSatisfied` を要求する。記憶条件は本人の装着記憶だけを参照するため、味方が別のIdentityを持っていても補えない。
- ゲームへの反映：`src/SodRpg.Mod/HostAuthority.cs` の連携走査は、`LinkSkills` の各枠から `hero.Skill.GetSkill(loc)` の実型名を1つずつ収集し、`Links.Satisfied` を呼ぶ。
- 記憶の分類・選択肢：`Links.cs` のIdentity／Movement分類と、`docs/specs/v1.27-traveler-kits.md` の「遠征の前に選べる装備」。Mistの記憶2種はどちらもIdentityとして明記されている。
- 本体の読み取り専用逆コンパイル資料：`/mnt/ghauto-sbx/sod-gamedata/decompiled/sod-decomp/Dew.Core/HeroSkill.cs`。`OnLateStartServer`（433〜458行）は各枠にロードアウト候補を1つ設定する。`GetSkill`（471〜483行）はIdentity枠から1つだけ返す。`EquipSkill`（560〜589行）は既存の同枠記憶を外してから設定し、`CanReplaceSkill`（682〜693行）はIdentityとMovementの通常の差し替えを禁止する。

393件の検査は `Content.Uniques` のすべての非null連携と、`Content.Sets` のすべての `LinkStages` を列挙する。装着記憶をIdentity最大1・Movement最大1に制限して、本番の判定が成立する構成を探す。通常記憶・エッセンス・近くの味方旅人は条件どおりに与え、今回のスロット矛盾を独立に検査する。残り389件がこの必要制約を通ることは、全入手経路・全イベント・全発動効果の成立を保証しない。

## 判断したこと・次工程への引き継ぎ

添付画像やIssue原文を取得せず、報告された記憶の実型名と本番定義から対象を特定した。本体スロット制約と既存の記憶一覧が一致するため、再現には追加の利用者情報を必要としない。

この工程は再現までとし、条件緩和方法・効果量・バランスの変更は決めていない。4件の修正方法、日英の説明・図鑑・Wikiの整合、内容指紋が変わる場合の協力プレイ参加者全員への更新告知は修正工程に残す。今回はテストと再現記録のみなので、ゲーム内容の指紋は変更していない。

未実行：Unity実機での装備操作・表示・協力通信・`dreamforge_simkill`（実機利用不可）、全テストスイート（工程の指示により対象クラスだけ実行）、MODのゲームDLL参照ビルド（製品コード変更なし）、NuGet脆弱性照会の完了（接続失敗のため実行引数で省略）。
