# 同行の刻印（2026-10-16）

9旅人に1段・1ポイントの刻印を各1個追加する。既存の刻印選択・Build同期・AuthoredKeystoneCodecを使い、保存形式・Protocolは変更しない。名前・説明は日英。数値は `tools/balance/stars.json` の27項目から生成する。Coop設定は既存Set変換の数値と、末尾に追加した `KeystonePayloadKind.Coop` の名前付きscopeで運ぶ。

| ID | ホスト側の仕掛け |
|---|---|
| `lacerta.key.shared-sights` | 最終命中で敵に4秒の印。別の味方の受信ダメージ+20%。味方の撃破で次の記憶1命中+50%を予約する。 |
| `vesper.key.shield-wall` | 0.25秒走査で6m以内の味方へ本人の防御50%をStatBonusで加算。本体の太陽の洗礼の生成時、味方を狙う敵へAI.Aggroを行う。 |
| `cetus.key.frost-resonance` | 本体ApplyElementalの冷気付与を記録し、既存冷気スタックまたはFrostbite由来のStunを持つ敵への味方の非冷気ダメージ+25%。味方の命中で本体スタック減衰タイマー／状態タイマーを0.5秒延長。 |
| `husk.key.shadow-cue` | 既存KillAssistsのHasHit/HitAtで3秒の不在を判定。味方の命中で対象別の次のIdentity命中を会心化し、既存SetCharge/RpcSetChargeで移動を1回復。CD2秒。The Killing Flowの本体通常攻撃もIdentityとして判定。 |
| `mist.key.parry-signal` | 既存遠征成長と同じSe_R_Parry_EndのblockedDamageで成功検出。8m以内の味方へ2秒・次の1命中+40%。 |
| `nachia.key.pack-guard` | 0.25秒走査で最低HP割合の味方を狙う敵へ召喚AIを向ける。そのような敵から召喚体が被弾すると5秒間魔力+10%。実際に守った味方を距離係数の相手として保持。 |
| `aurena.key.golden-trust` | 本体の正の回復イベントで味方へ3秒間与ダメージ+15%。味方の最終与ダメージの10%を本体Healで本人へ還元。 |
| `yubar.key.resonant-stardust` | 既存HitAtの2秒以内の味方命中を確認し、通常攻撃本体パケットへ+30%。同パケットの最終命中で魔力40%・2mの追加魔法ダメージ。CD0.5秒、EnterGenerated/ExitGeneratedと再入防止を使用。 |
| `bismuth.key.shared-crystal-field` | 業火物語Ai_QR_InfernalTalesの実DewCollider内を判定。味方の加速+10・与ダメージ+10%。領域内の味方の命中で3秒間、本人から対象へ+10%。Build適用前から存在する領域もActorManagerから取得。 |

距離・生存・味方判定にはCoopDistanceNativeを使う（距離減衰有効時は10mまで全量、18mで0、範囲外2秒猶予）。6m/8m・領域内という追加条件は個別に満たす必要がある。ソロは他の味方がいないため発動しない。味方へ渡す防御・加速・魔力は既存のStatBonus同期、冷気は既存SyncVar、回復・ダメージは本体経路を使い、FXは追加しない。

一回だけの増幅・会心はProcessReceivedDamageの受信処理後に消費する。これにより、後続の属性処理や命中通知の途中で起きる入れ子ダメージへの二重適用を避ける。星屑の予約は既存ダメージパケットに結び付け、終了時に残った予約も解除する。

必要なHarmony接続を入場時に確認し、欠けた接続・例外は当該刻印のみ停止する。死亡・装備無効・Build交換・部屋変更・Zone変更・ActorManager交換・離脱・プール返却で一時状態とStatBonusを解除する。走査では再利用リストを使い、定常的な毎フレーム割り当てを追加しない。印や予約はイベント時に、味方の補正状態は初めて対象範囲に入った走査時に作る。防御共有の元値から、他の盾壁でもらった防御（本体の防御割合補正込み）を除き、相互の再共有を防ぐ。無敵で無効になる攻撃では一回効果を消費しない。

## 代替と未確認点

- ヴェスパーは、本体の挑発対象のコレクションを増やす方式に代えて、依頼で認められたホストAI標的変更を使用する。`Ai_R_BaptismOfSun.OnHit` が敵のAI.Aggroを呼ぶことを確認した。本体の味方を狙う敵へのダメージ・被命中数由来の防御バフは追加しない。
- ビスマスの領域は本体の業火物語の円／輪とする。固定半径への近似はせず、本体の形状・大きさ変更をそのまま使う。
- 冷気の本体ApplyElementalは、既存スタックへ他の使用者が重ねても常にcasterを書き換えるわけではないため、ケトゥスの成功した付与を別途記録する。
- FrostbiteのFreeze／Projectile型は参照DLL内のメソッド本体が空で、凍結処理そのものを逆コンパイルで確認できない。実装はそのActor祖先を持つ本体Stun状態を検出するが、実ゲームの凍結状態の親関係・持続延長は未確認。
- 実ゲームのホスト＋クライアントによる9効果、距離猶予、再接続、死亡、部屋遷移、AI優先、確定会心に伴う本体の攻撃イベント表示は未実行。この環境で行ったビルド／Coreテストを実ゲームでの動作確認とは扱わない。

## 既存テストの期待値

同行刻印追加に合わせ、新設刻印数8→9、全旅人の刻印総数90→99、旅人ごとの生成行数を各+1にした。新規テストは追加していない。配置はREADMEの正規StarMapRender optimizer（全旅人・800万回）で再生成した。ビスマスの交差上限だけ100→121へ更新した（新しい星集合で293→121。重なり禁止・最小間隔・共通の改善率検査は維持）。

## 検証

- `python3 tools/star-manifest/validate.py`：全9旅人・outer合格。
- `python3 tools/balance/gen_cs.py --check`：終了コード0。
- MOD Releaseビルド：0エラー・既存警告6件。
- 既存Core限定テスト：95件合格・2件スキップ、追加115件合格、MemoryActivationAttribution 13件合格。スキップは既存の低速ゲート。全スイート／低速ゲートは未実行（依頼の限定検証方針）。
- `tests/Issue73.Native.Tests` Releaseビルド：既存の `_linkCoefficients` 1エラーで失敗。
- `tests/SodRpg.Mod.Startup.Tests` Releaseビルド：既存のConfigureMeleeCoop・meleeRangeToSpeed・coopDistanceFalloff・meleeBreak・meleeProximityGuardの5エラーで失敗。両プロジェクトの計6エラーは依頼文で別作業として除外されたもの。今回追加によるエラーは検出していない。
- 最終の定義・距離・帰属テスト再実行：108件合格・2件スキップ。上の95件とMemoryActivationAttribution 13件を含むため重複加算しない。配置38件も合格し、別実行の115件と合わせて重複を除く261件合格・2件スキップ。
- `StarMapRender --check`：全9旅人合格、全座標表fresh、全員の星の重なり0、最小間隔83.9以上。交差数はAurena 43、Bismuth 121、Cetus 96、Husk 101、Lacerta 85、Mist 771、Nachia 47、Vesper 145、Yubar 86。Mistには交差・線が星を通る箇所が残るが、既存の品質基準は満たす。
- 全9旅人をSVG/PNGへ描画して目視確認した。成果物と計測表は `/tmp/coopkeys-starmaps-20261016/`。これはオフスクリーンの配置描画で、実ゲームの操作確認ではない。
- 最終の生成座標を含むMOD Releaseビルド：0エラー・既存警告6件。実ゲームへの配置は行わず、ビルド出力先は `/tmp/coopkeys-build-mod`。
- `git diff --check`：合格。
