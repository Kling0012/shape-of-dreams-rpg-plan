# Issue #506: ボス実体の寿命番号が枯渇してマークが読めなくなる

マーク対象の生成・寿命終了を4096回繰り返し、次の対象でもマークを読めることを確かめる。常時アクティブな対象は1体だけで、同時に4096体を生存させる必要はない。テストは実際のMODソースをリンクし、本体のライフサイクルをAPI doubleで再現する。

## 実行方法

リポジトリのルートで実行する。

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/SodRpg.Core.Tests/SodRpg.Core.Tests.csproj -c Release --filter FullyQualifiedName~BossNativeActorLifetimeReproTests --logger 'console;verbosity=detailed' -p:PythonCommand=python3
```

`DOTNET_ROLL_FORWARD=Major` は、.NET 8のtesthostを起動できない環境で指定する。.NET 8がある環境では省略できる。パッケージキャッシュは環境の `NUGET_PACKAGES`、未設定なら既定のグローバルパッケージ場所を使う。

## 修正前の結果

2026-10-16と2026-10-17の記録では、終了コード1、3ケース中2成功・1失敗。ビルドやtesthostの起動は成功し、4097体目のマーク件数を確かめるアサーションが失敗した。修正前のアダプタとテストの参照元はコミット `475d3657`。

| usePooling | reuseInRoom | 4096回終了後の残件数 | 次の寿命番号 | 次のマーク件数 |
| --- | --- | ---: | ---: | ---: |
| true | false | 0 | 4097 | 1 |
| false | true | 0 | 4097 | 1 |
| false | false | 4096 | 0（未登録） | 0 |

非プール条件では終了済みの記録が1024、2048、3072、4096件と増え、最後に次の出力となった。

```text
usePooling=False, reuseInRoom=False, retained=4096, nextLife=0, nextMarks=0
```

## 本体コードとMODの根拠

調査元は逆コンパイルした本体の `Dew.Core/`。本体ソースとDLLはリポジトリに含めていない。

- `Actor.Destroy()` は `Dew.Destroy(gameObject)` へ進む。`Dew.StepDestroy` は `ICleanup.OnCleanup()` の後、RPC待ちと `canDestroy` のポーリングを経て、カスタム破棄処理か `SpawnManager.Destroy` を呼ぶ。
- `Actor.ICleanup.OnCleanup()` は `Network_isActive=false` を設定する。`OnIsActiveChanged` が `InvokeOnDestroyActorIfDidnt` を呼ぶ。MODの `CompleteBossNativeActor` はこの時点で寿命番号を消さない。
- `Actor.OnDisable()` は `DewPool.ClearEventsAndProcessors(this)` を呼ぶが、`ClearPooledEventsAndProcessors()` まで進むのは `usePooling || actor.reuseInRoom` が真の場合だけ。修正前はこのprefixだけが辞書を解放していた。
- `SpawnManager.Destroy` は非プール条件でUnityの破棄へ進む。部屋片付け・ゾーン移動の `Actor.Destroy()` も、この解放条件を変えない。
- `_bossNativeActorLives` は4096件で新規割り当てを0として拒否する。マークは正の寿命番号と辞書の一致を要求するため、未登録の対象では読めなくなる。

## 現行の解放処理と既存テスト

`BossNativeDisabledLifetime` はActor本体とコンテンツの派生型が宣言する `OnDisable()` を列挙し、基底処理を呼ばないoverrideも対象にする。`isActive=false` のActorを後処理で解放する。一時的な `SetActive(false)` で `isActive=true` のActorは番号を保持する。既存のプール解放prefixも残し、重複呼び出しでは実際に削除したときだけ解放数を増やす。

すでに隠されたActorは、後から破棄しても二度目の `OnDisable` を受けないため、`BossNativeCleanedLifetime` が `ICleanup.OnCleanup` の後処理で非アクティブなActorを解放する。現行の条件は、隠されていたActorに限定されていない。

既存テストは通常破棄と隠されたままの破棄を交互に実行する。全3条件で4096回終了後の残件数0、次の寿命番号4097・マーク1件を要求する。一時無効化中は同じ番号とマークを保持し、その後の破棄で記録を解放することも確かめる。API doubleはHarmonyを自動適用しないため、実装のprefix／postfixを明示的に呼ぶ。

2026-10-17の記録では3件成功。cleanup後処理の解放だけを一時的に外した比較では、全3条件が2回目の隠された破棄で残件数1となり失敗した。2026-10-19の文書整理でも現行の既存3件は成功した。新しいテストは追加していない。

開発コマンド `dreamforge_bosslives 1` は現在件数・最大件数・累計解放数を直後と30秒ごとに出力する。`0` は現在値を1回出して停止する。使い方は [MODの診断案内](../../../src/SodRpg.Mod/README.md#ボス実体の寿命診断issue-506) を参照。

## 確認できていない経路

既存テストの成功は、次の経路の安全性を示すものではない。

- 直接のUnity破棄やシーン解放では `isActive=true` のまま `OnDisable` を通る可能性があり、fake-nullとなったキーの回収は既存テストに含まれない。
- cleanup後も本体の破棄処理は待機・ポーリングを続ける。`AzurakBurrowCurrent` は親が非アクティブでも寿命番号と祖先を照合するため、cleanup直後に親の番号を消してよいとは言えない。`OnDestroyActor` 呼び出しの完了と、そこから生じる遅延攻撃の完了は区別する。今回参照できた本体ソースには `Se_U_Burrow` の実装本文がなく、その生成順序は未確認。
- 解放後に `BossNativeActorLife` が呼ばれると再登録できる。非アクティブなActorの新規登録を拒否する案は、終了攻撃が既存の親番号を読む経路と、生成直後の有効化前に登録する経路を区別する必要がある。

これらはIssue #623のL10・L13・L14として調査した。Unityのfake-nullや遅延する終了攻撃を既存のAPI doubleは再現しておらず、追加のテスト設計が必要なため、今回の文書整理では動作を変更していない。

実ゲーム起動、Unityの破棄コールバック・Harmony適用・fake-null、Mirror実通信、既定フラグの実行時値、長時間Infinity・高密度部屋、診断コマンドの実ログ、実投射物の重複判定は未実行。実機確認だけを目的とした別Issueは作成しない。全体・低速・性能スイートは変更範囲外のため未実行。
