# Issue #771 修正前の再現記録

MOD の画面（Dreamforge のメニュー）の検索欄で、IME の変換を含む日本語入力ができない状態を再現した。
対象は装備・鍛冶タブ共通の名前検索・星図の星検索・交換タブの名前検索。本工程では製品コードを変更していない。

## 実行

```sh
DOTNET_ROLL_FORWARD=Major dotnet test tests/Issue73.Native.Tests/Issue73.Native.Tests.csproj -c Release --filter 'FullyQualifiedName~Issue771Ime' --verbosity quiet
```

再現テストは `tests/Issue73.Native.Tests/Issue771ImeReproTests.cs`（境界は `Issue771ImeUiBoundary.cs`、
抽出は `NativePersistence.targets` の `CompileIssue771ImeSearchUi` / `CompileIssue771ImeTradeUi`）。
修正前の実行は終了コード1、**5件中3件が失敗**（装備・鍛冶・星図・交換の IME 有効化）、2件が成功
（フォーカス喪失時にモードが戻ること・変換中の Enter で「次の星へ」が進まないことの、既存挙動の境界）。
失敗はすべて「検索欄にフォーカスしている間は Input.imeCompositionMode == On であること」の表明で、
ハーネスの例外ではない。環境は SDK 10.0.111 / ランタイム 10.0.11（`DOTNET_ROLL_FORWARD=Major`）、
`NUGET_PACKAGES` は worktree 外の既定のキャッシュを使った。

## 再現の方法

1. 本体の `ControlManager.FrameUpdate`（逆コンパイル資料 `decompiled/sod-decomp/Dew.Core/ControlManager.cs:448`）は、
   キャラ操作中の毎フレーム `Input.imeCompositionMode = IMECompositionMode.Off` を書き続ける。
   テストの setup でこれと同じ効果（モード Off・フォーカスなし）を与える。
2. 実ソースから抽出した検索欄の描画（`DrawRelicSearch` / `StarDrawSearch` / `DrawCoopTradeFilters`）を
   1フレーム描いてコントロールを確定させ、検索欄をクリックした想定でキーボードフォーカスを与え、
   次のフレームを描く。
3. 描画後の `Input.imeCompositionMode` が On であることを確かめる。Unity の IMGUI のテキスト欄は
   このモードが On の間しか OS の IME 変換入力を受け付けない（Off では IME が無効になり、かな漢字に
   変換する前の鍵盤入力しか届かない）。実 Unity プロセスは動かさないため、モードの値で判定する。

## 現行コードで確認した事実

- MOD の全ソースに `imeCompositionMode` への書き込みはなく、IME を On にする経路が存在しない。
  読み出しは `DreamforgeUi.cs:3113`（星検索の Enter 判定で `Input.compositionString` と一緒に使う）だけ。
- 本体が毎フレーム Off にするのに対し、MOD は `Patches.cs` の `BlockInputWhileMenuOpen` で
  `GetShouldProcessCharacterInputAllowKnockedOut` を偽って Off の書き込み自体は止まるが、
  直前までゲームプレイだった値（Off）が残ったままになる。ロビー等で一度も Off が書かれていない
  状態もあり得るため、復元は「無条件で Off」ではなく「フォーカス前の値に戻す」のが正しい。
- MOD のテキスト入力欄はすべて IMGUI（`GUI.TextField` / `GUILayout.TextField`）で、次の7か所。
  - `DreamforgeUi.cs:1708` 装備・鍛冶タブ共通の名前検索（#420）
  - `DreamforgeUi.cs:3115` 星図の星検索（名前 `DreamforgeStarSearch`、Enter 判定のみ IME 考慮済み）
  - `DreamforgeUi.CoopTrade.cs:332` 交換タブの名前検索（#407）
  - `DreamforgeUi.CoopTrade.cs:278,280` 交換の素材数（数字）
  - `DreamforgeUi.StarLoadouts.cs:65` 星図のロードアウト名
  - `CodexView.cs:105` 図鑑の検索
  図鑑・ロードアウト名・素材数は今回の抽出対象外だが、同じ原因で日本語が入らない。修正では
  これらを含む全欄で IME が有効になることが必要（受け付け審査の指摘どおり横展開する）。
- 検索側の一致処理は日本語を前提に作られている。`RelicSearch.FoldChar`（`SodRpg.Core/Game/RelicSearch.cs:34`）は
  カタカナ→ひらがな・全角→半角をそろえ、`StarMapMath.Matches` も同じ方針。確定文字列が欄に
  入れば検索はそのまま効く。
- 二次的な懸念: `DreamforgeMod.cs:244-245` の `HandleKeys` は F6 と Esc を新しい Input System の
  生の `wasPressedThisFrame` で見ており、IME 変換中かどうかを確認しない。変換の確定 Enter・取消 Esc が
  UI の開閉や検索確定として誤動作しないよう、修正では `Input.compositionString` の空確認を
  検索欄と同じ条件で入れることを検討する（境界の例「確定 Enter・取消 Esc で閉じない」）。
  旧 Input API が製品で動いている（`DreamforgeUi.cs:3113`）ため、`Input.compositionString` は
  このゲームでも読める（Active Input Handling は Both 相当）。

## 判断したこと

- 「日本語を入力できない」の再現の基準は、OS の IME 変換入力の受け付け条件（`Input.imeCompositionMode`）
  が検索欄フォーカス中に On になっていることとした。文字の到達・確定・検索実行はその結果として
  決まるため、モードの値で判定する。
- フォーカスの表現は IMGUI のキーボードコントロール（`GUIUtility.keyboardControl`）と名前付き
  コントロール（`GUI.SetNextControlName` / `GUI.GetNameOfFocusedControl`）の両方を境界スタブに用意した。
  修正がどちらの方法でフォーカスを見ても同じ検証が通る。
- 3つの検索欄（装備・鍛冶共通・星図・交換）を実ソースから抽出して再現し、残りの欄は上記の
  事実（書き込み経路が存在しない）で同じ原因と判断した。図鑑 `Draw` は抽出コストに対し
  情報が増えないため対象外とし、修正工程での横展開に任せた。
- フォーカス喪失時にモードが戻ること・変換中の Enter で「次の星へ」が進まないことの2件は
  修正前から通る境界テストとして残す（修正で壊していないことの確認用）。

## 実行した確認

- `--filter 'FullyQualifiedName~Issue771Ime'`: 終了コード1。失敗3（装備・鍛冶／星図／交換の IME 有効化）、成功2。
- 共用スタブを拡張したため、既存の UI 境界テストの絞り込み実行
  （`ProfileSlot|RelicRowCache|StarCluster|Satchel|SecurePanel|ContinueSave|Hud`）: 終了0、45件成功。
- `git diff --check`: 終了0。
- ビルド警告は既存のもののみ（今回の追加分に新しい警告は出ていない）。

## 未実行と未解決

- 実ゲームでの IME 入力・変換候補の表示位置・全画面/ウィンドウ差・フォント: この環境ではゲームを
  起動できないため未実行。PR 本文の「未実行」に記載する。
- 修正は未実施（工程指定）。修正では7か所すべての欄で IME 有効化と復元、F6/Esc の変換中ガード、
  必要なら `GUI.SetNextControlName` の追加（名前でのフォーカス判定のため）を検討すること。
- 図鑑・ロードアウト名・素材数欄への抽出テスト追加、MOD 全体のビルド、全テスト: 未実行。
  修正工程またはパイプラインに任せる。
