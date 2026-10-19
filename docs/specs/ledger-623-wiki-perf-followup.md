# #623 Wiki と軽量化のレビュー提案への対応

2026-10-19、PR #792・#793・#805・#813・#816・#822・#823 と Issue #818 の提案10件を個別に確認した。

## 更新履歴の分類

L2 の PR #793 に含まれる2変更は、不具合の修正として扱う。共有クールダウン表が満杯で入力間隔のキーを登録できなくてもポラリスのモード切替を通す変更と、固有品ID・土台IDの文字列が衝突してもアイコンを取り違えない変更である。既存の実装を読み、日英の断片 `changelog.d/623-ledger-edge-cases.md` を追加した。この作業で両処理を再実装したわけではない。

## Wiki の公開と再調律表

L5 は [Wiki 原稿の README](../wiki/README.md) に公開手順を追加した。GitHub API の `full_name` は `Kling0012/shape-of-dreams-rpg-plan` で、原稿内のリンクは現行名と一致したため変更していない。GitHub Wiki の最初のページ作成、既存本文を残した8ファイルの反映、DokuWiki の `basics.txt` の再生成結果を `wiki-pages` へ反映する作業は、利用者確認後に維持者が行う。原稿のマージだけで #818 を閉じない。

L6 は `Loot.RollRelic` と `Loot.RollUnique`、`Content.RetuneCost`、`Forbidden.IsOrange` を確認した。通常の固有品は `RollUnique` が `Rarity.Legendary` で生成する。一方、`IsOrange` の判定条件は固有ID・セットなし・ボスの技なしで、レア度を検査していない。追加費用表は伝説のオレンジ枠固有品向けと日英の見出しに明記した。伝説以外のデータへ適用できる表とは扱わず、ゲームの判定や保存形式は変更していない。

生成前後を比較し、94ファイルのうち差分は `pages/dreamforge/basics.txt` の表の説明1行だけだった。追加3回分の費用8・10・12は変わっていない。生成器は終了コード0だが、既存の `sets.txt` が280,000バイトの目安を超える警告は前後とも残った。

## 位置同期とシェーダーの比較

L7 は位置同期のコードコメントと [仕様](perf-loop/20261019-0400.md) を訂正した。省いた更新の速度が1cm/s以下でも、受信側が保持する直前の速度にはさらに1cm/sの差があり得る。最大約2cmのずれが次の意味のある更新まで残り得るため、1cm以内という説明と、停止時の速度0を必ず送るという説明を改めた。送信判定は変更していない。

L10 は `GuardFloat`・`GuardVector`・`SameArray` のコメントと [仕様](perf-loop/20261019-0558.md) を実装に合わせた。比較は float の値に対する `==` であり、+0 と -0 は同一視し、NaN は送り直す。ビット比較への変更は行っていない。

## 協力効果と警告の単位

L9 は `CoopDistanceNative.Coefficient` の製品コードの呼び出し元を確認した。拡大半径を使う Heal・Siphon は効果ごとの Tracker を明示する。ほかの呼び出し元の SharedWard・TriumphSong・Apothecary・Resonance は `FullRange` と同じ10で、`LivingAllies` の0指定は距離判定を通らない。現状の呼び出し元と、新しい拡大半径の効果には別の Tracker が必要なことをコメントに残した。料理の警告コメントは記録する単位に合わせて `Warn once per ingredient stack` に直した。

## 過去の検証記録のパス

L8 は PR #805 の本文と #623 の本文を確認した。#623 の本文にはパスを書き換える個別要件がなく、PR #805 はパスを環境に依存しない表記へ改めたと説明している。この書き換えは Infinity の処理順と検証記録の明確化に付随した変更として扱う。環境に依存しない表記には、元の環境固有の絶対パスを別環境で使わずに済む利点がある。ただし、当時の意図は確認できておらず、参照先の実値もその表記からは復元できない。過去の記録への追加の書き換えは行っていない。

## 保留した提案

- L1：PR 本文を組み立てる処理はこのリポジトリに含まれず、他リポジトリの操作は禁止されているため保留。本文に見出しや理論節がある場合に挿入を省く修正は、その処理の担当側で行う必要がある。
- L3：過去の PR #816 の本文を編集する操作は、コミットと結果ファイルまでという担当範囲を超えるため保留。今回の本文案では検証の節を重複させない。
- L4：依頼が今回の修正不要としているため、Requires のコメントと逐語的文字列の対応は保留。

## 今回の検証

- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Issue73.Native.Tests -c Release --filter 'FullyQualifiedName~PositionSyncIdlePerfTests|FullyQualifiedName~OcclusionShaderIdlePerfTests' --logger 'console;verbosity=minimal'`：20件成功、失敗・スキップ0件。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests -c Release --filter 'FullyQualifiedName~MeleeCoopNativeTests' --logger 'console;verbosity=minimal'`：25件成功、失敗・スキップ0件。
- `DOTNET_ROLL_FORWARD=Major dotnet run -c Release --project tools/WikiGen -- <worktree 外の出力先>`：変更前後とも終了コード0。生成結果の差分を比較した。

未実行：ゲーム内の表示・協力通信の確認。この環境ではゲームを起動できない。MOD 全体のビルド、全件・低速テスト、バランス生成コードの検査は、実行処理や数値の原本を変えていないため実行しなかった。GitHub Wiki 本文の取得・公開と `wiki-pages` への反映は公開手順の記載までとし、実行していない。新規テストは作成していない。
