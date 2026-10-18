# Issue #457 バグ再現記録

## 修正前の再現

2026-10-16、修正前の本番 `Content.Uniques` と `Links.Satisfied` を使った既存の再現テスト2件が失敗した。固有品「傑作の連剣」`unique.link_mist_masterwork` は、合法なMistの8構成で一度も発動しなかった。全固有品・セット連携393件の検査では、同型の到達不能条件が4件あった。

| 固有品ID | 名前 | 同時に要求していたIdentity |
| --- | --- | --- |
| `unique.link_mist_masterwork` | 傑作の連剣 | `St_D_AstridsMasterpieceEnGarde` + `St_D_AstridsMasterpiecePriorite` |
| `unique.link_vesper_resolve_mercy` | 決意と慈悲 | `St_D_Resolve` + `St_D_MercyOfEl` |
| `unique.link_lacerta_powder_tap` | 双発の火薬庫 | `St_D_DoubleTap` + `St_D_SalamanderPowder` |
| `unique.link_husk_wind_scar` | 風傷の殺陣 | `St_D_TheKillingFlow` + `St_D_ScarOfTheWind` |

## 原因

`Links.Satisfied` は `Requires` の全条件を要求し、記憶条件には本人の装着記憶だけを使う。Identityは1枠なので、2種類を同時に要求する4件の条件は満たせなかった。Mistに不可能なIdentity2種類を診断入力として渡した場合だけ発動した。

枠の根拠は `Links.cs` の分類と `docs/specs/v1.27-traveler-kits.md` の記憶一覧、および本体 `HeroSkill` の実装である。読み取り専用の逆コンパイル資料では、`OnLateStartServer` が各枠に候補を1つ設定し、`GetSkill` がIdentity枠から1つだけ返す。`EquipSkill` は同じ枠の記憶を外してから装着し、`CanReplaceSkill` はIdentityとMovementの通常の差し替えを禁止する。`HostAuthority` も各枠の実型名を1つずつ集めて判定するため、味方のIdentityでは補えない。

さらに、Identityのみを使用対象とする迅化・鼓舞が10件あった。`HostAuthority.OnSkillUse` は、使用された記憶が `Requires` に含まれるときだけ処理する。受け身のIdentityは通常の使用イベントを出さないため、これらも発動しなかった。

## 修正

2026-10-16の修正で、到達不能だった4件の2つ目のIdentityを同じ旅人のMovementへ置き換えた。旅人と記憶2つの全条件一致、効果量、固有パワー、IDは維持した。

| 固有品ID | 修正後の記憶条件（加えて対応する旅人） | 同調の効果量 |
| --- | --- | ---: |
| `unique.link_mist_masterwork` | `St_D_AstridsMasterpieceEnGarde` + `St_M_FastFeet` | 46% |
| `unique.link_vesper_resolve_mercy` | `St_D_Resolve` + `St_M_Charge` | 46% |
| `unique.link_lacerta_powder_tap` | `St_D_DoubleTap` + `St_M_NimbleDodge` | 46% |
| `unique.link_husk_wind_scar` | `St_D_TheKillingFlow` + `St_M_FlashStep` | 48% |

Mistはアンガルド選択時に発動する。プリオリテ単独への拡張や連携全体のOR条件化は行っていない。

Identityのみを使用対象とする10件は、装着条件を保ち、使用イベントを必要としない同調へ変えた。数値は `旧値 × 同調の条件数別上限 ÷ 旧種類の条件数別上限` を四捨五入した（0.5は切り上げ）。迅化の上限は50/80/110、鼓舞は40/64/88、同調は25/40/55。上限に対する相対量を保つ調整であり、実戦で異なる効果が等価になることは保証しない。

| 固有品ID | 条件数 | 旧種類・旧値 | 新種類・新値 |
| --- | ---: | --- | --- |
| `unique.link_yubar_star_echo` | 1 | MemoryHaste 38 | Attune 19 |
| `unique.link3_dawnless_slash` | 3 | MemorySurge 62 | Attune 39 |
| `unique.c_menace_locket` | 2 | MemorySurge 36 | Attune 23 |
| `unique.c_flowkill_seal` | 1 | MemoryHaste 30 | Attune 15 |
| `unique.c_windscar_quill` | 2 | MemorySurge 34 | Attune 21 |
| `unique.c_twobeat_sight` | 2 | MemorySurge 34 | Attune 21 |
| `unique.c_rightofway_feather` | 1 | MemoryHaste 30 | Attune 15 |
| `unique.c_packheart` | 2 | MemorySurge 30 | Attune 19 |
| `unique.c_exotic_inkstone` | 1 | MemorySurge 26 | Attune 16 |
| `unique.c_convergence_comet` | 2 | MemoryHaste 60 | Attune 30 |

`St_Q_MoonlightPact` は使用できる星座構成があるため維持した。孤狼の星座による使用停止は任意の構成である。

日英の装備説明・図鑑・Wikiは共通の `Links.Describe` を使う。修正時には数値の原本 `tools/balance/equipment/uniques.json`、生成C#定数、v1.29の8行と種類別集計を同期した。種類・数値の変更で内容指紋も変わるため、協力プレイ参加者全員の同時更新を日英の更新履歴に記載した。既存の内容不一致警告は維持した。

## 検証

以下は2026-10-16の修正工程で確認した結果である。修正工程ではテストを追加・変更していない。

- 再現クラス2件は、修正前に終了コード1で失敗し、修正後は終了コード0で成功した。合法Mistは発動0/8から4/8、393連携の枠制約による到達不能は4件から0件になった。
- テストを変えずに4件の装備条件だけを旧条件へ一時的に戻すと、同じ2件が失敗した。修正を再適用すると2件とも成功した。
- 関連する既存7クラスは22件成功した。定義の走査では、Identity／Movementの重複条件とIdentityのみを使用対象とする迅化・鼓舞は0件だった。
- 数値生成と `python3 tools/balance/gen_cs.py --check`、ゲームDLL参照のMOD Releaseビルド、ローカルWiki生成、`git diff --check` は成功した。Wikiでは変更14件の日英の条件と効果量を確認した。公開Wikiは更新していない。

再現クラスのコマンドは次のとおり。修正済みの状態では2件とも成功する。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  -c Release --filter FullyQualifiedName~Issue457LinkReachabilityTests \
  --logger 'console;verbosity=detailed' \
  -p:PythonCommand=python3 -p:NuGetAudit=false \
  -p:GameDir='/mnt/ghauto-sbx/sod-gamedata/Shape of Dreams'
```

393件の検査はIdentity最大1・Movement最大1という必要制約の確認である。全入手経路、全イベント、全星座構成、全発動効果の成立を保証しない。

未実行はUnity実機の装備・表示・戦闘・協力通信・`dreamforge_simkill`（実機利用不可）、全テスト・slowテスト（関連クラスに限定）、NuGet脆弱性照会（接続制約）である。同調へ変えた10件の実戦バランスは未確認。

工程ごとの実行環境と、修正済みスナップショットで再検証した際の判定は[過去の作業記録](archive/issue-457-work-log.md)を参照。そこにある `NOT_REPRODUCED` は修正済み状態での再検証の判定であり、修正前の失敗や修正の実施を否定するものではない。
