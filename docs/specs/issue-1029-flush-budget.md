# #1029 キューの処理予算と空の待機時間

追加効果と飛び道具生成のキューで、持ち越し中に古い空の時刻が残り、捌き切った直後に拡大した予算が縮む問題を修正した。持ち越しを観測したら空の時刻を未設定に戻し、次に捌き切った時刻から3秒を数え直す。

## 原因と修正

`PowerFlushBudget.Observe` は `leftover > 0` の分岐で `BacklogSince` を更新する。`NativeProcSpawnSpread.Tick` も処理後の `Queue.Count > 0` の分岐で `_backlogSince` を更新する。修正前はどちらも空の時刻を解除していなかった。

たとえば t=0 で空を観測し、t=4.0 から持ち越しが続いて t=4.7 で一段拡大すると、捌き切った後も t=0 が縮小の基準になる。追加効果キューは t=4.8 の空の観測で、飛び道具キューは捌き切った次の空の Tick で、すでに3秒待ったと判断して縮小する。

`HostAuthority.PowerCommandQueue.cs` の持ち越し分岐に `queue.DrainedSince = -1f` を、`NativeProcSpawnSpread.cs` の処理後の持ち越し分岐に `_drainedSince = -1f` を追加した。既存の空の分岐が次の観測時刻を記録するので、持ち越し中の時間や、その前の空の期間は縮小までの待機に含まれない。待機中の再持ち越しでも同じように数え直す。

## 要件と対応ファイル

| 要件 | 対応ファイルと確認内容 |
| --- | --- |
| FR-1 | `src/SodRpg.Mod/HostAuthority.PowerCommandQueue.cs`: `Observe` の `leftover > 0` で `DrainedSince` を解除する。 |
| FR-2 | `src/SodRpg.Mod/NativeProcSpawnSpread.cs`: 処理後の `Queue.Count > 0` で `_drainedSince` を解除する。 |
| FR-3 | 上記2ファイルの空の分岐が未設定の時刻を現在時刻で初期化する。3秒の判定値は変更しない。 |
| FR-4 | `tests/SodRpg.Mod.FxBudget.Tests/PowerFlushBudgetTests.cs` と `NativeProcSpawnSpreadTests.cs`: 空→長い持ち越し→拡大→空の時系列で、直後と2.99秒後は予算を保ち、3秒経過後に一段縮む。追加効果のテストは t=0、4.0、4.7、4.8 の指定例を使う。 |
| FR-5 | 同じ2つのテストファイルの `RestartsIdleWindowWhenCarryReturnsDuringTheWait` で、待機中の再持ち越しから数え直すことを確認する。 |
| FR-6 | `src/SodRpg.Mod/HostAuthority.NewPowers.cs` の `ClearNewPowerZone` / `ResetBudget` と処理後の観測順序は差分なし。`NativeProcSpawnSpread.cs` の `TickRoom`、先頭からの処理、処理済み範囲の除去、0.07秒の強制生成も変更しない。既存テストで投入した飛び道具の生成数と最大待ち時間の保護を確認する。 |
| FR-7 | `HostAuthority.PowerCommandQueue.cs` と `NativeProcSpawnSpread.cs` の0.6秒の拡大判定と一段ずつの変更を維持する。既存テストで追加効果は0.5秒で拡大せず0.6秒で拡大し、飛び道具は連続した持ち越しで一段拡大する。 |
| FR-8 | FR-4 と同じ既存テストで両方のキューを確認する。実ソースをテスト用アセンブリへ組み込むため、予算の判定を複製しない。 |

## 判断したこと

- 「持ち越し」は処理後に残りがある観測とした。追加効果の呼び出し元も処理後の件数を渡すため、解除は既存の持ち越し分岐に置く。同じ呼び出しで全件処理できた新規投入は持ち越しとして扱わない。
- 「捌き切った時点」は持ち越しから空へ変わった最初の観測とした。時刻の無効値は既存の初期値と同じ `-1f` を使う。
- 追加効果の `FlushNewPowers` は処理した呼び出しでだけ `Observe` を呼ぶ。空のまま呼び出されなくても、次の空の観測で記録した時刻からの経過を判定する。今回、呼び出し条件や処理順は変えていない。
- 部屋変更時の処理は既存のものを維持する。飛び道具側の `TickRoom` は診断状態と合体処理の状態を更新する処理であり、予算全体を新たにリセットする変更は加えない。

## 検証

```sh
dotnet test tests/SodRpg.Mod.FxBudget.Tests/SodRpg.Mod.FxBudget.Tests.csproj -c Release --filter 'FullyQualifiedName~PowerFlushBudgetTests|FullyQualifiedName~NativeProcSpawnSpreadTests'
dotnet test tests/SodRpg.Mod.FxBudget.Tests/SodRpg.Mod.FxBudget.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~NativeFxBudgetTests'
```
