# Issue #457 バグ再現記録

## 今回の範囲

PR #477 は連携の成立状態を調べる診断ログと案内を追加する。元の不具合は未修正で、Issue #457 は閉じない。条件・種類・効果量・保存・通信・内容指紋は main と同じにした。

前工程の記録には、14件の修正と再現テストを含む状態での成功結果があった。診断だけの差分という説明と一致しなかったため、今回の修正ラウンドでゲーム定義の変更、再現テスト、修正済みとする更新履歴を PR の差分から外した。過去の成功結果を、現在の差分で不具合が直った根拠には使わない。

## 今回確認した再現結果

2026-10-18、引き継いだ `Issue457LinkReachabilityTests` を変更せずに実行した。開始時の定義では2件成功し、Mist の合法な8構成中4構成で成立、全393連携で枠制約による到達不能は0件だった。

次に `Content.cs`、`uniques.json`、`EquipmentItems.Generated.cs`、`v1.29-uniques-table.md` を main と同じに戻し、同じテストを実行した。2件とも失敗した。

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

失敗した4件は次のとおり。

| 固有品ID | 同時に要求されるIdentity |
| --- | --- |
| `unique.link_mist_masterwork` | `St_D_AstridsMasterpieceEnGarde` + `St_D_AstridsMasterpiecePriorite` |
| `unique.link_vesper_resolve_mercy` | `St_D_Resolve` + `St_D_MercyOfEl` |
| `unique.link_lacerta_powder_tap` | `St_D_DoubleTap` + `St_D_SalamanderPowder` |
| `unique.link_husk_wind_scar` | `St_D_TheKillingFlow` + `St_D_ScarOfTheWind` |

`Links.Satisfied` は要求された記憶をすべて装着しているか判定する。通常のIdentity枠から得られる記憶は1つなので、この4件の条件は成立しない。Core の再現結果から条件矛盾は確認できたが、実際のゲームで観測した結果ではない。今回は診断だけに限定する明示指示に従い、条件の修正は含めない。

再現に使ったコマンドは次のとおり。最初の実行後は `--no-restore` も指定した。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  -c Release --filter 'FullyQualifiedName~Issue457LinkReachabilityTests' \
  --logger 'console;verbosity=detailed' \
  -p:PythonCommand=python3 -p:NuGetAudit=false
```

この再現テストは診断 PR の範囲外という指摘に従って差分から外した。上記は除外前に行った比較の記録であり、最終状態にはそのテストクラスは含まれない。失敗を成功として扱わず、元の不具合が残る証拠として記録する。

## 診断ログ

`HostAuthority.UpdateLinks` の既存判定後と早期returnの直前から `DiagnoseLinks` を呼ぶ。旅人の種類、生存状態、記憶とエッセンスの取得有無、連携数と成立数、Identityを要求する連携の条件と成立結果を `diag457` として記録する。

連携定義・記憶・エッセンス・生存状態・成立結果が同じなら再出力しない。Identity連携の詳細は状態ごとに最大6件、ログ全体はホストの遠征ごとに最大32行とし、上限の最後に `run log limit reached` と出す。診断専用のコレクションを再利用し、判定用の状態へは書き戻さない。診断内の例外は吸収し、ゲーム処理を続ける。

名前・アカウントID・セーブ全文は追加ログに含めない。`Player.log` の保存先、共有前に除く個人情報、添えてほしい構成と操作は README の #457 節に記載した。

## 最終状態の検証

main と同じゲーム定義に戻し、再現テストを除外したあと、既存の関連6クラスを実行した。終了0、20件成功だった。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj \
  -c Release --no-restore \
  --filter 'FullyQualifiedName~LinksV126Tests|FullyQualifiedName~LinkActivationV127Tests|FullyQualifiedName~ContentTableV129Tests|FullyQualifiedName~CodexTextConsistencyTests|FullyQualifiedName~ContentFingerprintTests|FullyQualifiedName~StarRoutesV127Tests' \
  --logger 'console;verbosity=normal' \
  -p:PythonCommand=python3 -p:NuGetAudit=false -v:q
```

`dotnet build src/SodRpg.Mod -c Release` は、ゲームDLLの参照先とゲーム外の一時配置先を指定して実行した。終了0、エラー0件・警告6件だった。警告は既存APIの非推奨3件と未代入フィールド3件。パッケージキャッシュは環境変数で指定された worktree 外の場所を使った。

関連テストの成功は元の不具合の解消やUnity上のログ出力を確認した結果ではない。新しいテストは追加していない。

## 未実行

- Unity実機での装備操作・画面・戦闘・協力プレイ・`dreamforge_simkill`、実際の `Player.log` への書き込みとログ障害時の動作：実機を利用できないため。
- 全テスト・slowテスト：本工程では関連箇所だけを実行する指示のため。
- Wiki生成・数値生成器の実行：生成元と生成物を main と同じに戻し、これらの機能を変更していないため。
- NuGet脆弱性照会：`NuGetAudit=false` を指定したため。
