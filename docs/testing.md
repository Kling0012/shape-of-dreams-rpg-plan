# テストガイド（tools/test_changed.py）

## 目的

開発中やエージェントの作業中に「変更した部分に関係するテストだけ」を素早く回すための
ツールとして `tools/test_changed.py`（Python 3.12・標準ライブラリのみ）を用意している。
テストプロジェクトごとのテストは直列（共有状態のため）なので、3 プロジェクトを
ビルド後に並列実行して実時間を縮める。

## 使い方

リポジトリルートから実行する。

```
python tools/test_changed.py [--base <git ref>] [--list] [--all] [--slow]
```

| オプション | 動作 |
|---|---|
| なし | 変更に対応するテストクラスだけを `dotnet test` で実行する |
| `--base <git ref>` | 比較元を指定する（既定: `origin/main` との merge-base） |
| `--list` | 選択されたテストクラス（完全修飾名）だけを表示して終了する（テストは実行しない） |
| `--all` | フィルタなしでスイート全体を実行する |
| `--slow` | `Speed=Slow` の網羅テスト（`SODRPG_SLOW=1`）も実行する（既定では除外） |

終了コードは最初の非ゼロの `dotnet test` 終了コード。テストに関係する変更が
一件もない場合は「変更なし」と表示して 0 を返す。

## 対象の収集

既定の比較元は `git merge-base HEAD origin/main`。`origin/main` が取得できない
場合は `HEAD` にフォールバックする（＝コミット済み差分は無く、ステージ済み・
未ステージ・未追跡の変更だけが対象になる）。収集対象は次の 3 種。

1. `git diff --name-only <base>...HEAD`（ブランチ上のコミット済み変更）
2. `git diff --name-only HEAD`（ステージ済み＋未ステージの作業ツリー変更）
3. `git ls-files --others --exclude-standard` のうち `.cs` ファイル（未追跡）

## 変更ファイルからテストクラスへのマッピング

`tests/` 配下の全 `.cs` ファイル（`bin`/`obj` を除く）をテストファイルのコーパスとし、
変更ファイルを次の規則でテストクラス（完全修飾名）へ写す。

1. **テストファイル自体の変更** — `tests/` 配下の変更された `.cs` は、
   そのファイルが宣言するテストクラスを選択する。
2. **製品コードの変更** — 変更された `.cs` のユニファイド diff から、その変更行に
   出てくるトークン（PascalCase の型名・メンバー名、4 文字以上の文字列リテラル）を
   抜き出し、それを単語として含むテストファイルのテストクラスを選択する
   （データ行の追加なら ID や名前、メンバーの変更ならその名前で絞り込める）。
   diff が取れない場合は従来どおりファイル全体の宣言型を使う。コメント-only の
   変更は何も選択しない。変更行にトークンが無い場合はファイル全体の宣言型へ
   フォールバックする。
3. **テストが読む非コードファイルの変更** — `tools/star-manifest/*.json`、
   `tools/lowrarity/*.json`、生成済み `.cs`（`*.Generated.cs`、`Generated*.cs`）が
   変更された場合、そのパス（ディレクトリ名＋ファイル名、または相対パス）を
   参照しているテストファイル、および生成型を参照しているテストファイル（規則 2 と
   同じ方法）のテストクラスを選択する。
4. **全体実行へのエスカレーション** — 次の場合はスイート全体を実行する。
   - ビルドファイル（`*.csproj`、`Directory.Build.*`）が変更された
   - 変更ファイルの型名がテストファイルの 60% 超に登場する
     （＝どこでも使われている型への影響が広すぎるため）

どの規則にも当てはまらない変更（ドキュメント等）だけの場合は
「関係する変更なし」と表示して終了コード 0 で終わる。

## 実行方法

選択されたテストクラスから `dotnet test <project> --filter` 式を組み立てる。
フィルタは `FullyQualifiedName~名前空間.クラス` 条件を `|` で連結した 1 本の式で、
長すぎる場合（既定の上限 15000 文字）は複数回の `dotnet test` 実行に分割する。
テストプロジェクトは `tests/` 配下の `.csproj` を再帰収集する（`bin`/`obj` を除く）。

- まず `dotnet build -c Release` をプロジェクトごとに直列に実行し
  （`obj/` の共有を守るため。Release は CPU 主体のこのスイートで
  Debug の半分以下の実時間になる）、その後 `dotnet test -c Release --no-build` を
  プロジェクト並列で実行する（各アセンブリ内のテストは共有状態のため元々直列）。

- `dotnet` コマンド: 既定では `%LOCALAPPDATA%\dotnet-sdk\dotnet.exe` を使う。
  環境変数 `DOTNET` に実行ファイルのパスがあればそれを優先する。
- `DOTNET_ROOT` は子プロセスに設定される（既定 `%LOCALAPPDATA%\dotnet-sdk`、
  `DOTNET` 指定時はそのディレクトリ）。
- `DOTNET_CLI_UI_LANGUAGE=en` を設定して実行する（結果サマリの解析のため）。
- 標準入力は閉じた状態（`/dev/null` 相当）で起動する。

実行後、選択クラス数と合計の passed / failed を表示する。

## 例

```
# 変更が影響するテストだけを実行
python tools/test_changed.py

# 選択されるテストクラスを確認するだけ
python tools/test_changed.py --list

# main 以外の分岐との比較
python tools/test_changed.py --base origin/release/v2.0.1

# 全テスト実行
python tools/test_changed.py --all
```

## #146 鞄あふれ準備保存後の再参加

`Issue73.Native.Tests.SatchelOverflowSaveTests` の1件は、参加者の満杯の鞄から
`GrantPendingKill → Emit → 換金準備保存` を実行し、実ディスクの保存を読み直して
同じ遠征・ゾーンへ再参加する。精算済みの撃破が保存の `PendingKills` に残らず、
熟練度・経験値・乱数・戦利品が再付与されないことを確認する。
複数のあふれの取引が台帳ID付きで保存されることも同じケースで確認する。
一括化前の `5ea38b5` の Mod 精算・換金・保存経路へ差し替えた一時環境では、
再参加後の撃破数が期待値1に対して2となり、同じテストが失敗することを確認した。
本体API・通信はダブルであり、ゲーム実機の異常終了・実通信は対象外。

## #62 圧の追加報酬の死亡順序

`AuthoredMechanismNativeTests` の `Pressure_dividend_*`（3メソッド、17ケース）は、
実装の `OnEntityAdd → OnMonsterDeath → RemoveMonster → NativeAttributedKill.Postfix
→ PublishAttributedKill → OnEntityRemove` を通す。死亡記録を直接作る旧ヘルパーは使わない。
`extract_issue62_native.py` が製品ソースの該当メソッドと通常撃破の
`CaptureAuthoritativeRunKill` をビルド時に抽出し、APIダブルとコンパイルする。
抽出結果は `obj/` のみで、製品ソースは変更しない。ビルドには `python` が必要。

- CoinExplosion／Shout × ローカル／協力プレイ所有者の4ケースで、乱数を0に固定し、
  抽選1回・所有者宛の未確保欠片+1・同一通知／別packet／親Actor通知の重複防止を確認。
  通常撃破の分類記録をledgerで解決し、通常報酬を先に比較してから追加欠片を適用する。
- 圧不足・報酬無効・召喚敵・生成ダメージ・未確認spawn・帰属packetなしを両記憶で確認。
- entity削除なしでも、非スケール時間の9.99秒では保持、10秒で回収。
  32死亡を繰り返し、当選済み／未受付のspawn・期限・出所・抽選記録が残らないことを確認。

検証境界: Unity／Harmony実機ではなくAPIダブルを使用する。ダメージpacketの帰属と
生成ダメージの不許可フラグは入力として与え、ネットワークは宛先とreceiptを記録する。
実機2台の配送、nativeダメージpacketの生成処理、通常敵以外のDeathBurst／elite goldは対象外。

## #47 刻印の代償撤廃の回帰確認

- `GeneratedHeroAcceptanceTests` は全82刻印の型付き効果、ダメージ・傷の非弱体化、
  日英説明に「代償／Drawback」がないこと、現在の通信仕様でのビルド往復を確認する。
- `StarCarryoverV131Tests` は従来の形式3/4互換に加え、v1.31移行済みの形式4で
  保存した刻印IDが現在の生成定義に解決され、保持するPowerが引き継がれることを確認する。
  v1.31未移行の保存には、別仕様の効果変更時返却が引き続き適用される。
- C11のHP支払い→障壁変換、C15の装備・選択変更の明示承認は既存テストを維持する。
- テストクラス間の並列実行は `AssemblyInfo.cs` の設定で無効（#151: 言語設定は
  スレッドローカル化したが、星のレジストリは実在の旅人 ID をキーにした
  プロセス全体の共有状態のため、並列化すると登録の干渉が起きる）。

## #48 ボスセットのnative回帰確認

- `SodRpg.Core.Tests` は製品の `HostAuthority.Boss*.cs` を直接リンクし、
  Unity／Mirror／ゲームAPIのみをスタブで置き換える。報酬の差分再適用、
  Primusの強化係数、Azurakの実HP被ダメージ条件、Big Chomp／Soul Prisonの
  追加量上限、Light表示の100ms集約、ボス装備なしの早期終了を確認する。
- `SodRpg.Mod.Startup.Tests` は実際のHarmonyで製品のIL事前検証と起動処理を実行し、
  契約不一致による適用拒否と、自MODのpatchだけを取り消す失敗時処理を確認する。
  ゲームと同じHarmony 2.3.3 APIを保つ `Lib.Harmony.Thin` と、
  .NET 10へのroll-forwardに対応する `MonoMod.Core` 1.3.6を固定している。
- Linuxでの全体実行：
  `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=Major python tools/test_changed.py --all`
- このハーネスは実ゲームの戦闘、Unity描画、Mirror協力同期、GC／frame時間を検証しない。

## #73 台帳・戦闘バッファ・差分同期の回帰確認

- `Issue73LedgerTests` は形式4→5の保存互換、連番の欠落・順序逆転・保存再読込を
  含む報酬の過不足防止、30秒期限、曖昧な復旧、旧 `expiredMonsters` の移行を確認する。
- `tests/Issue73.Native.Tests` は本番 Mod ソースをリンクする独立ハーネス。
  途中参加の再送と差分の32 RPC/フレーム枠、FIFO合流・種類間公平性、保存済みACK後の圧縮、
  空の仮peerの除外、配当の5秒遅延保存と書込済みリビジョン、入れ子のバッファ再利用、
  Harmony経由の直接 `RemoveAbility` と装備epochを確認する。
- ネイティブの通信・時計・エンティティだけをスタブ化する。配当の保存は実際の
  `ProfileStore` / `AsyncProfileWriter` で一時ディレクトリへ書き、試験後に除去する。
  `NativePersistence.targets` は SDK の Roslyn AST で保存・Emit・取引／照会と
  `ClientSession.Infinity.cs` のチェックポイント準備同期を選び、そのままコンパイルする。
  製品側の処理は複製・変更しない。
  `ContinueSaveTests.Overflow_at_continue_checkpoint_survives_restore_and_settles_exactly_once`
  は通常の既存キュー／Infinity準備中の回収 × 未実行／支払い済みの4ケースで、
  台帳ID付き義務の保存、別セッションへの復元、欠片の一度だけの回復とダストの二重払い防止を確認する。
- Core既存ハーネスの `ExposeAuthoredPendingMetadata` は、テスト公開の `PendingGimmick` と
  アクセス範囲を揃えるため、生成したコンパイル単位だけで `AuthoredPendingGimmick` を
  `internal` にする。製品ソースと値型メタデータの処理内容は変更しない。
- `DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=Major python tools/test_changed.py --all` で
  両プロジェクトを実行する。単独確認は `dotnet test <project> --filter FullyQualifiedName~Issue73LedgerTests`
  または `FullyQualifiedName~NativeAcceptanceTests`。
  実機のフレーム時間・確保量・Unity本体のRPC転送はこのハーネスの検証範囲外。

## マッピングロジックの単体テスト

`tools/tests/test_test_changed.py`（unittest）がマッピングロジックの単体テストを
持つ。実際の git / dotnet は使わず、一時ディレクトリに小さなダミーリポジトリを
作って検証する。

```
python -m unittest discover -s tools/tests -p "test_test_changed.py"
```

## 制限

- 規則 2 は `src/` 配下の `.cs` のみが対象。`tools/BalanceSim` 等の他の製品コードの
  変更は、テスト側で対応するテストファイルを同時に変更しない限り選択されない。
- 型名・パスの抽出は正規表現ベースなので、コメント内の文字列は無視されるが、
  文字列リテラル内の `class Xxx` という形は誤検出される可能性がある
  （誤検出はテストの過剰選択としてのみ現れ、欠落にはならない）。
