# 軽量化ループの記録

軽量化ループ（1回に1対象）の記録。遊び方・強さ・判定・数値・見た目を変えずに軽くする。新しい記録はこのファイルの末尾に足す。

## perf-loop/20261018-1420

対象は native Dew.Core/AbilityInstance.cs（StatusEffect と Projectile の基底クラス）。改善なし。

毎フレームの経路（LogicUpdate、HandleSequence）は添字の for ループで早抜けがあり、シーケンスの実行状態は静的プールで再利用していた。被弾ごとの経路（CreateDamage、Heal、GetValue、effectiveLevel）は必要な1つの確保と数回の分岐だけだった。通信（SerializeSyncVars、DeserializeSyncVars）はdirty bitの判定だけだった。OnCreate の反射による上書き判定は型ごとに辞書へキャッシュされ、FxPlay は効果がないときすぐ返った。ActorManager.enableUsefulActorName は既定で無効なので、GetActorReadableName の文字列組み立ても毎フレームには走らなかった。

見送った候補は次の3つ。

- DestroyOnDeath のデリゲート確保（1回の登録で4〜5件）。状態効果の付与時にしか呼ばれず頻度が低い。調査中に本体の不具合も見つけた（キャッシュ用のフィールドへ代入し忘れたため、購読の解除が機能していない）。直すには購読の管理を変えることになり、結果が変わらないと保証できないため触らない。
- OnDestroyActor の StartCoroutine とクロージャ（破棄ごと）。開始効果を止めるために2フレーム待っており、別の待ち方に置き換えると停止のタイミングが変わるおそれがあるため見送り。
- HandleSequence の enumerator.Current の重複読み出し（1ステップで最大5回）。Transpiler が必要な割に効果は毎フレーム数ナノ秒の桁のため見送り。

## perf-loop/20261018-1433

対象: mod src/SodRpg.Mod/BountyReportBatching.cs

直した内容: クライアントの `OnBountyCounts`（`DreamforgeBountyCountsMsg` 受信ごと）で、`Rules.OnSupportApplied` を `msg.shields`・`msg.allyHeals` が 0 でも無条件に呼んでいた。0 件の呼び出しは Rules.cs の冒頭で空の `List<GameEvent>` を確保して返すだけで、`Emit(IEnumerable)` は空のリストを反復するだけなので、件数が 0 のときは呼び出し自体を省き、確保と反復をなくした。

結果が変わらない理由: `OnSupportApplied` は `count <= 0` を最初に検査してプロフィールに触れず空リストを返し、空リストの `Emit` は何も起きない（`_dirty` も立たない）。0 件のときの外部から見える作用は無く、1 件以上のときは従前どおり。元素撃破のみの報告（`ReportElementalDeath` 由来）では毎回 ShieldGranted・AllyHealed 両方の空リスト確保が発生していた。

効果の見込み（推定）: 撃取報告メッセージ1通あたり最大2件の `List<GameEvent>` 確保と2回の空反復を削減。元素ビルドで敵を倒し続ける場面でクライアント側の GC 負荷が減る見込み（推定。頻度は戦闘中のホストティックごと・英雄ごと最大1通）。
