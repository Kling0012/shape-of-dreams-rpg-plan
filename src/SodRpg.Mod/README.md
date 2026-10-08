# Dreamforge RPG（ゲーム内MOD）

このブランチは **Protocol 24・保存形式6（プロフィールリセットなし）**。最新mainの機能に加え、#255 のプレイヤー間トレードに対応しています。既存の協力機能は版・Protocol・内容の差でも警告だけで続行し、トレードの対応確認に失敗した場合はトレードだけを利用不可にします。変更は[更新履歴](../../CHANGELOG.md)を参照。

全14ボスセット84部位・11種のnative報酬adapterを実装済み。装備照合と報酬更新は#73の共通装備キャッシュ／epochを使い、`EntityAbility.SetAbility`／`RemoveAbility`で更新します。ボス撃破条件は共通の連番・ストリーム台帳へ記録します。[承認仕様と実装境界](../../docs/specs/issue-48-boss-sets.md)

計画書 Ver1.0 を目安に、最初に遊べる形へまとめたMOD。計画書の全要素ではなく、「持ち帰る装備」「帰還（確保）の決断」「自分の戦い方」「協力」の4本を、本作の既存ループ（ゾーン→ボス）の上に載せた。

## 本体連携の利用可否（Issue #109）

`NativePatchPreflight` は診断専用で、不一致・検査例外・未確認を適用拒否の理由にしない。Harmony内部copierによる合成ILのdry変換も、MOD全体や機能の起動条件にしない。実際の適用に必要な対象・捕捉フィールド・注入先の型は各パッチの初期化／`Prepare`／transpilerで確認し、失敗したクラスだけをスキップする。適用途中の失敗は自MODの該当クラスのパッチだけを戻し、別ownerと他クラスは残す。ロールバックの検査例外もクラスの失敗境界から外へ出さない。非公開メンバーの解決失敗は警告して依存する帰属機能だけを止め、型初期化や毎フレームの例外にしない。

| 機能 | 実際の適用・実行で止める範囲 |
| --- | --- |
| エレボス LastStarlight 連携 | 対象メソッドへの適用に失敗したクラスだけをスキップ。待機回数の事前診断は適用を拒否せず、実行中の想定外の待機ではその列挙の値変更・表示だけを解除 |
| Feather 遅延帰属 | source／effectの所定の捕捉フィールド・遅延dispatch対象を解決できなければ依存フックだけをスキップ。実行時の想定外callback・未捕捉の生成寿命は遅延帰属だけを警告して停止し、本体callbackは実行 |
| Baptism コルーチン帰属 | buff／effect／iteratorの所定の捕捉フィールド・factory／`MoveNext`を解決できなければ依存フックだけをスキップ。未捕捉の生成寿命はコルーチン帰属だけを停止。本体処理と独立した終了時爆発の連携は続行 |
| DoubleTap コルーチン帰属 | source／firedの所定の捕捉フィールド・factory／`MoveNext`を解決できなければ依存フックだけをスキップ。生成寿命の記録や本体の射撃は続行 |
| Nyx HerWorld 移動・tick帰属 | `ActiveLogicUpdate`のlocal 1が`Entity`互換でないなど、注入に必要な前提が合わなければ該当クラスをスキップ |
| 白夜／暗月 InkBeam 命中連携 | `Hit`の必要なdispatch／敵判定を解決・置換できなければ該当クラスをスキップ |
| HP実損失の帰属 | `currentHealth` setterのtyped delegateを取得できなければHP帰属パッチだけをスキップ。ホストの生成や本体のHP処理は続行 |
| ボス効果表示の送信 | 本体RPC送信のtyped delegateを取得できない。表示送信だけを止め、ゲームプレイは継続する |
| WinterDive のテレポート帰属 | 本体列挙子またはcaster捕捉フィールドを取得できない。従来の `Prepare` 判定を維持し、無効化ログを `Log.Warn` に統一 |

LastStarlight の捕捉はホスト上の生存中・登録済みの旅人が正規に装着した有効なgem／skillで、エレボス報酬段階が正の場合だけ行う。通常のLastStarlightはラップも値変更もしない。実行中に3回目の待機が現れた場合は、その列挙について警告を1回だけ出し、自分の値差分と表示を解除する。本体列挙子はそこで破棄せず、その待機から以後のyieldと完了処理をそのまま通す。正常な2回待機の遅延・持続時間の連携は維持する。保存形式・Protocolは変更しない。

## 遊び

| 仕組み | 内容 |
| --- | --- |
| 遺物 | 敵を倒すと各プレイヤーに個別に落ちる装備。6枠（主装備・頭・防具・手・足・装飾品）、レア度5段階、土台360種（すべてアイコン付き）、固有品1,190個（v1.30.1時点）。エピック以上は固有効果（連撃・逆襲・鉄の輪・吸命・棘・処刑・共鳴・追い風・護りの灯・灯守・烈火）を持つ |
| 確保と夢の深度 | 拾った物はまず未確保。新しいゾーンに着くと確保地点になり、「確保する」（保管庫へ）か「深く潜る」（深度+1：ドロップ率+35%/段・レア度上昇、代わりに防御-6・最大HP-4%/段、確保時の欠片ボーナス+25%/段）を選ぶ |
| 遺失物 | 全滅（投了を含む）すると未確保の遺物は遺失物（最大10）へ、欠片は25%だけ持ち帰る。次の遠征で戦闘部屋を3つ突破すると最良の1つを取り戻す（再び未確保） |
| 旅人ごとの星の経験（v1.27） | 通常撃破1・エリート5・ボス20、悪夢／変種は2倍、確保20・踏破100。k個目は6k+50経験（累計3k²+53k）、最大150ポイント（v1.31 開発版は300）。図鑑最大4・テスト設定を各旅人に加える。夢のレベルからは星ポイントを得ない |
| 狙い系統 | 遠征の外で攻勢・守勢・共鳴のいずれかを選ぶと、その系統の遺物（固有品を含む）が2倍出やすくなる |
| 系統セット | 同じ系統の遺物を2つ装着すると攻勢：攻撃力・魔力+5%／守勢：防御+8／共鳴：スキル加速+8、3つでさらに攻撃速度+6%／最大HP+6%／移動速度+4% |
| 悪夢の契約（v1.3で本体の呪いに） | 深く潜るとき、提示された契約から1つを結べる。代償は本体の呪い（Hatred の祭壇と同じもの、強度は弱・中・強）がランダムに1つ付き、本体の解除依頼で外せる。見返り（ドロップ率・レア度・欠片・経験・攻撃力など）は次の確保まで重なって効く |
| 合成 | 鍵なし・未装着の同じレア度をコモン5個／アンコモン5個／レア12個／エピック16個から1つ上のレア度を1つ。欠片10／20／60／300、固有品への合成は調律石4。結果の枠を選ぶと欠片1.5倍 |
| 図鑑の節目 | 遺物の種類を6種集めるごとに星図ポイント+1（最大+4） |
| 悪夢化エリート（v1.1で本体のエリートに） | パーティの最大深度に応じて敵の一部が悪夢化（深度1で通常2%・エリート20%、深度ごとに上昇。ボスは対象外）。HP+80%に加え、鋼殻・狂暴・巨躯・疾風・再生・魔力から1〜3つ。本体のエリート効果（MirageSkin：見た目と専用攻撃）も付く。頭上に名札が出て、倒すと1段上の格（通常→エリート相当、エリート→ボス相当）の戦利品 |
| 夢の変種（v1.24） | 深度2以降、対応する本体の敵が低確率で変種になる（深度2で6%、深度ごとに+2%、部屋に1体まで）。悪夢化とは重ならず、全敵共通の深度補正に専用の能力・結界・棘皮・吸収・破甲などを重ねる。一撃の上限は最大HPの8%、死亡時の爆発は半径4以内の旅人へ各自の最大HPの20%。各PCで専用の色・大きさと橙色の名札を表示し、撃破時は変種の報酬を受け取る |
| 今日の夢 | 日付（UTC）で決まる8種の変化（烈火の日・鋼の日・共鳴の日・黄金の夢・豊穣の夢・悪夢の夜・星降る夢・静かな夢）。遠征の開始日の夢がその遠征中ずっと効く |
| 旅人の星図（v1.27） | 従来の核＋記憶ごとの7ルート（Bismuthは実データに合わせ6）＋夢の輪8星。ルートは3段の星6つと1段・3ポイントの頂点。核の手前に6段で開き、前の星に1段で次へ進める。記憶連携はその記憶を装着中だけ有効。核の到達刻印は星のレベルで枠が増え、最大3つまで選べる（v2.0.2。レベル200で2つ、400で3つ）。無料で振り直せる |
| 記憶の合わせ技 / Memory-pair combos（v1.30） | 枝の内側の橋は3段の合わせ技。橋と両隣の4番目の星に各1段以上振り、両方の記憶を装着すると有効。命中の印・使用の窓を別の記憶でつなぎ、回復・障壁・追加攻撃・再使用短縮などが起こる。印はダメージを増やさず、追加ダメージは属性なし・連鎖なし。外側の夢の輪は従来の能力値を維持 |
| 旅人の熟練度 | キャラごとの撃破数で熟練度0〜10と称号。熟練度3で到達刻印を解放（v1.2で能力%は廃止） |
| Limbo 連動（v1.1） | 本体の Limbo 深度1ごとに遺物ドロップ率+10%・レア度上昇（v0.7 の独自の開始深度は廃止） |
| 固有効果（追加） | 雷鎖・爆砕・守護霊・血の渇き。固有品「雷鳴の双牙」「砕けた星核」「守護霊の衣」「血塗れの大槌」 |
| 夢の工房 | 欠片・調律石で恒久強化（大きな鞄・広い保管庫・依頼の引き直し）。v1.5で遺失物の灯・残響の増幅・契約の目利きは廃止し費用を返金 |
| 本体の通貨（v1.5） | 夢の商人はゴールド払い（本体の難易度補正）、確保地点でドリームダスト100→欠片10、遠征中は未確保の遺物を分解してドリームダスト。通貨はホストが動かし、成功したときだけ確定 |
| 夢の出来事 | 確保地点に50%で1つ：夢の商人（欠片で正体不明の遺物）・祈りの泉（弱い遺物を捧げて強い遺物+1）・賭けの杯（未確保の欠片を倍か無か）・迷い人の灯（遺失物を回収） |
| セット遺物 | 《潮鳴りの装い》（冷気・回避：Cetus/Mist向け）《灯守の誓い》（光・回復：Nachia/Aurena向け）《残火の誓約》（火・4発目の烈火）《黄昏の狩装》（闇・処刑人）《冬枯れの誓約》（冷気・守り）《星詠みの装束》（スキル・Ultimate）の3点セット（v1.9で6つ）。固有品として落ち、2点で能力、3点で固有効果 |
| 本体とのシナジー（v1.0） | 属性の増幅特性（火・冷気・光・闇）、命中時に属性を付与する固有効果（火種・霜・輝き・影）、4属性が揃うと爆発（四元の共鳴）、烈火は本体の4発目で発動、回避でMemoryのクールダウン短縮（回避の残響）、Ultimate後の強化（終の昂り）、本体の行動（Chaos・商人・強化・合成・分解・ハンター）の依頼 |
| 初めての動線（v1.4） | 初回起動で「ようこそ」の案内と初期装備（各枠1つ、最初の遠征で空の旅人に自動装備）。拾得・確保地点・確保・潜行・全滅・レベルアップ・悪夢・依頼・鍛冶などで一度だけヒント。記録タブから再表示・設定で非表示 |
| 旅人の専用固有品（v1.4） | 本体の旅人9人それぞれのキットに効く固有品（審問官の誓剣・サラマンダーの銃身・深海の外殻・星屑の写本・空殻の牙・霧払いの太刀・絆の鈴・黄金の聖杯・四冊目の物語） |
| 依頼 | 遠征の開始時に3つ（敵の撃破数・エリート・ボス・確保数・深度・レア発見・部屋突破から重複なし）。達成で欠片・調律石（未確保として鞄へ。確保の最中に達成したら直接保管庫へ）と経験値 |
| 遠征の結果 | ランの終わりに、撃破・発見・確保・遺失・最高深度・レベルの変化を表示する |
| 鍛冶 | 強化（通常+5まで、レア以下の欠片20/35/60/90/130、エピック以上は2倍）、再調律（特性1つを3候補から選び直し、調律石1/2/3、エピック以上は2/4/6、3回まで）、特性の洗い直し（欠片60×(レア度+1)・調律石2×(レア度+1)、エピック以上は基礎費用2倍、済ませた回数ごとに1.5倍して切り上げ）、分解、製作（到達した最高アイテムレベルで作る） |
| 限界突破 | 同じ枠・同じレア度以上の遺物1つを消費して強化上限+5。レアは1回（調律石5・欠片200）、エピック以上は1/2/3回目が調律石10/20/40・欠片400/800/1600（エピック2回まで、伝説3回まで）。+6以降の強化もエピック以上は基本費用2倍 |
| 覚醒 | 装着中の伝説に通常1・エリート5・ボス20の覚醒の力（悪夢化2倍）。累計5000/15000/37500で覚醒Ⅰ/Ⅱ/Ⅲ、固有効果1.25/1.5/1.8倍・特性1.1/1.2/1.3倍 |
| 協力 | 抽選と保存は各自のPC。能力の反映はホストが行う（ホストにMODが必要）。共鳴は近くの味方へも効果を分ける |
| 夢の圧（v1.27） | ゲームに参加中の人間の夢レベル・使用済み星ポイントの平均で敵HPと与ダメージを乗算。圧が高まると敵の数も増えるが、ボスの数は増やさない。係数の原本は `tools/balance/pressure.json`。人数補正・深度・悪夢・変種の既存強化と併用し、HP／攻撃の倍率はボスにも有効。HUDにホストの現在値を表示 |

装備の変更は遠征の外か確保地点でのみ、星図は遠征の外でのみ変更できる。

### 夢の圧による敵数増加（Issue #253）

- 通常の夢の圧には段番号がないため、最終HP倍率の増分を0.10ごとに1段へ換算する。夢レベル・使用星・深度・道標・Infinityの圧を含み、`floor((HP倍率−1)/0.10)` 段。人数は従来どおり平均と本体補正に任せる。
- 1段につき敵数+8%、上限+60%。ホストが各ウェーブの非ボス出現を数え、元敵の出現処理へ追加コピーを混ぜる。遭遇ごとのランダムな初期端数へ増加率を積む整数丸めで、体数とエリート／ミニボスの比率は期待値で維持する。元敵全滅後の追加ウェーブや同時追加1体の制限はない。
- 本体 `RoomMonsters.SpawnMonstersRoutine` の返すiteratorを包み、コピーも同じ `SpawnMonsterImp`・設定・`MonsterSpawnData` を使う。満員なら現在のウェーブ内で待つ。次の元敵もglobal／section人口へコストを加えて予測判定し、本体の上限・人口コスト・ウェーブ数は変更しない。別のiteratorと共有する全滅待ちには未出現の予約も数え、出現時に本体の寿命管理へ置き換える。`onFinish`／部屋クリアの別経路は追加しない。
- 追加体も通常の報酬経路へ通す。係数の原本は `dreamPressure.enemyCountAdditionalRewardBudget=0.20`、係数は `min(1, 0.20 / 敵数増加率)`。+60%なら1/3。本体ゴールド・XP・Stardust、MODの夢XP・星XP・覚醒・欠片・調律石を係数処理し、整数は確率丸め。遺物・ミニボスChaos・配当・回復は抽選確率を調整する。回復の不成立分は本体プールへ即座に返し、prepare／network spawn前で止める。元敵・ボス・部屋固定報酬は据え置き。
- Infinityの時間・部屋・無料出力予算は変更せず、追加体も撃破認可・報酬台帳へ入る。道標変換後の新規報酬を係数処理してから出力予算を消費し、宝庫の保留報酬も同じ順序。追加の直接報酬は同条件の元敵比で期待値+20%まで。体数の整数丸め、抽選結果、長い戦闘による時間クレジット回復、熟練度／依頼の実撃破数まで含む総収入を毎部屋+20%へ固定するものではない。
- Protocol・保存フィールド／形式は変更なし。本体の保存・初回同期済みActor辞書へ係数を記録し、MODでは既存の不透明な撃破EventIdへ係数を含めて、認証済み通知・遅延精算・Continueへ保持する。プール再利用ではこの機能の印だけを解除する。参加者もこの係数を読む実装が必要で、旧実装との混在時のMOD報酬上限は保証しない（通信やMOD全体の起動を版一致で止める変更はしない）。
- ホストのみ生成し、参加者へは本体同期を使う。遭遇ごとにwrapper・再利用gate・callbackを用意し、待機は共通の0.25秒yield。毎フレームの配列／LINQ／反射・新しい待機オブジェクトは追加しない。部屋停止で未出現予約を解放する。出現失敗・収まらない人口コスト・追加分の上限待ち時間切れ・optional API不在なら敵数増加だけを警告停止し、既に出た敵と本体の全滅待ちは残す。ILや非公開ライブラリの厳密一致をMOD全体の起動条件にしない。

根拠（本体の逆コンパイルは別ディレクトリの読み取り専用資料）：
`Dew.Core/RoomMonsters.cs:2142-2239`（ウェーブ数・人口予算・全滅待ち）、`:2274-2303`（出現と同期・寿命）、`:749-823`（別経路のミニボスとChaos）、
`GameManager.cs:236-251,489-505,1133-1154`（同時人口上限・集計・本体補正）、`Section_Monsters.cs:45-89`（区画上限）、
`Entity.cs:781-838`（同期的な死亡通知）、`PickupManager.cs:276-337`（本体の報酬対象判定）、`GameManager.cs:1073-1105`（XP・ゴールド）、`Actor.cs:154-158,549-570`（既存辞書と初回同期）、`SpawnManager.cs:487-527`（即時プール解放）。
既存 `HostAuthority.PressureDividend.cs:13-35` は出現の由来を記録する報酬用フックで、敵数を増やすものではない。夢の圧そのものは最終HPと与ダメージの倍率であり、AIの行動速度／行動選択は変更しない。

指定Releaseビルドは警告5／エラー0、生成鮮度確認は成功、既存全テストは2,588成功／0失敗／既存skip4。新規テストは追加せず、既存nativeハーネスのActor保存辞書だけAPIへ追随した。一時コンソールに実装ファイルと実Harmonyを組み込み、合成した本体境界で2ウェーブ各25体へ各15体を追加し、最初の死亡前の混在、同時追加4体、区画上限4／global上限5、全滅後のクリア通知1回を確認。global上限3を先に満たす別条件では元10／追加6体、最大同時3体。満員のミニボス予約を別iteratorの全滅待ちも認識し、空き後に元＋追加を出現。満員待機10,000回は追加割当0バイト。元敵の本体通貨／XPは30、係数1/3の追加体は10、回復3,000抽選で977出現／2,023プール返却。native XPの整数境界、入れ子の元ミニボスChaos、出現失敗後も元6体を完走する経路も実行。通常／宝庫それぞれ5,000撃破で係数報酬を実行し、Infinityの認可・出力予算消費とpending撃破の保存往復を確認。実ゲーム実行ファイルはなく、Unityの実アセット・画面・協力通信・具体的な子敵生成・実プール動作は未確認。元敵と無関係なparentで出る敵には由来を推測して印を付けない。

### Infinityのボス間隔補正（Issue #270）

| ボスまでの戦闘部屋 | 既存の圧へ加える段数 | 圧とは別枠の敵数 | 通常遺物の抽選・出力・Epic予算倍率 |
| ---: | ---: | ---: | ---: |
| 20 | 0 | +0% | ×1 |
| 15 | +2 | +200% | ×1.5 |
| 10 | +4 | +400% | ×2 |

- 原本は `tools/balance/infinity.json` の `intervalScaling`。`InfinityRunState.PressureStage` に段数を加え、既存HP・攻撃・圧由来の敵数をまとめて上げる。全体の圧上限は100段のまま。敵数は `pressure.EnemyCountMultiplier + EnemyCountBonus` であり、独立分には圧の+60%上限を適用しない。
- #253の各ウェーブの予約・人口ゲートを再利用し、1元敵につき複数の追加体を順に出す。global／section人口とウェーブ数は据え置き、満員なら空くまで待つ。追加体の報酬係数は圧と間隔の合計増加率から `min(1, 0.20 / bonus)` を求める。元敵の係数は1のまま。
- `Loot.RollKill` は既存の抽選を残し、Legendary確率の重みを除外した独立の追加抽選をEpic以下・現在の認可上限以内で行う。生成済み係数を使う `InfinityIntervalScaling.OrdinaryBudgetMultiplier` により、通常遺物の出力とEpic期待値の実効予算も20／15／10部屋で×1／×1.5／×2とする。Legendary・ボス限定セット・道標の固定保証の抽選と予算は増幅しない。増員報酬係数とは別に掛かるが、実際の発見数／時が倍率どおりになる保証ではない。
- ホストが選んだ既存 `Interval` の同期・Continue保存から補正を導出する。Protocol 24・保存形式6・Infinity codec1を変更せず、旧帰還記録の圧値も書き換えない。不一致でMOD全体やこの補正を止める新規ゲートはない。増員失敗時の停止は#253の敵数機能だけ。
- 既存の保存済み予算scalarは基準周期のcredit単位を維持し、通常遺物1個の出力消費は `1 / 通常予算倍率`、固定保証は1とする。倍率を増やす周期のLegendary・限定品は、専用の高レア・Legendary台帳で満額認可済みなら通常出力枠で再抑止しない。抽選確率と専用rate／burstは増やさず、20周期の出力規則は変更しない。高レア台帳は `Legendary期待値 + (Epic以上期待値 − Legendary期待値) / 通常予算倍率` を消費し、補充・burstの保存値を変えずに通常分だけ実効容量を増やす。追加体の時間・部屋・高レア認可は最終報酬係数を織り込んだ期待消費量で行い、後段で間引く前の全量予約を避ける。固定保証は全1回分を予約する。
- 開始画面に日英の3列で難度・圧加算・敵数加算・通常遺物倍率と希少品の据え置きを表示する。1920×1080の既定スケールでは列幅344px・高さ60px。文字列・GUILayoutの列サイズはキャッシュする。Unity起動プログラムが提供されておらず、実画面／実coop／実Continue／実際の踏破速度は未確認。
- 実装ソースと実Harmonyを使った一時コンソールで、元敵20体に対し開始時の20／15／10設定で追加0／43／86〜87体、区画上限2・global上限3内の混在、全滅後予約0を確認。global上限1／区画3でも追加86体・最大同時1体。真のボスはコピー0、追加出現失敗の注入後も元20体を完走し、予約とactive waveは0。段階JITを無効にした待機1万回の測定は追加割り当て0bytes。合成本体境界での証拠であり、Unity内のFPS測定ではない。
- 初期実装の300人×1時間・seed1比較ではLegendaryの観測数が少なく、伝説の出力維持や減少原因の証明にはならない。追補では同一模型の基準実装と変更後を比較し、実際の発見数と正規化した予算消費を区別する。測定条件と証拠は [バランス資料](../../tools/balance/README.md#issue-270同一模型の修正前後比較) を参照。

### 中断した遠征の遺物（Issue #263）

- 未精算の遠征が残ったまま別の遠征を開始すると、前の鞄の遺物を自分のプロフィールの「受け取り待ち」に保存します。死亡・敗北・勝利・確保帰還と、保存済みの終了結果は対象外です。欠片・調律石は従来の精算のままです。
- 遠征中のMODメニュー上部（各タブの内容より前）に「中断した遠征の遺物を受け取る（N個）」を表示します。1遠征1回、現在の鞄へ受け取ります。満杯時は既存の弱い品からの欠片化と、あふれた遺物の追加ドリームダスト設定を使います。受け取り待ちがないときは非表示、再開処理・移動・保存・取引の待機中は操作できません。
- 直近の中断1回分だけをボタンの対象とし、さらに中断した場合は古い未受け取り分を遺失物へ移します。遺失物の上限を超えた分も既存の欠片化で残します。予約品は受け取り対象に混ぜず、協力プレイでも各自の保存分だけを扱います。
- 受け取りID・受け取った遠征ID・処理済みの元遠征IDをプロフィールに記録し、受け取り直後に確定保存します。保存失敗が確定した場合は操作前へ戻します。書き込みの確認が時間切れなら権利を復活させず、受け取った状態を保持して保存を再試行します。この機能の保存待ちでMOD全体を止めません。
- 元の遠征を受け取り前にContinueで再開すると、その受け取り待ちは消えます。受け取り済み／遺失物へ移した元遠征と、受け取った遠征を再開するときだけ、最新の財産・予約・受領記録を保ちます。戦闘進行は巻き戻しますが、確保・分解・交換した遺物やあふれた分の報酬は復活させません。無関係な次の遠征のContinueは従来どおりです。
- 保存形式は7（形式3〜6からのリセットなし）。旧保存は受け取り待ちなしとして読みます。通信の共通Protocol・Helloは変更せず、自分の鞄への移動に新しいホスト通信は使いません。任意の追加ドリームダストだけは既存のホスト処理を使います。
- **EN:** The MOD menu shows **Claim interrupted expedition relics (N)** during an expedition. Claim the latest interrupted satchel once per expedition; older unclaimed batches move to Lost & Found. Defeats are excluded. Existing overflow and optional Dream Dust rules apply. Receipts save immediately. Continue cancels unclaimed source rights, and preserves current ownership only for affected source/receiving expeditions to prevent duplication; unrelated expeditions retain normal rollback.
- 実機のUnity描画・協力通信は未確認です。本番UI／セッション処理をリンクした一時スモークで、日英・1920×1080のボタン行の収まり、押下・即時保存・非表示・再押下拒否・古いContinueを確認しました（他タブの内容・フォント描画は境界代替）。

### プレイヤー同士の交換（Issue #255）

- MODメニューの「交換」タブで同じセッションのプレイヤーを選んで申し込み、相手が承諾します。双方が未装着の保管庫・鞄の遺物と、欠片・調律石の数（鞄の所持分を含む）を提示し、相手の遺物名・レア度・強化値・効果を見て「この内容で交換を確認」を押します。内容の編集は双方の確認を解除します。受け取る遺物は保管庫、素材は確保済み残高へ入ります。保管庫容量と整数の所持上限を超える交換は成立しません。
- ロビーまたは遠征の戦闘外（既存の装備変更可能な時機・クリア済みの部屋）だけ利用できます。戦闘・部屋／ゾーン移動・移動投票・本体やMODの保存中は不可です。取消、切断、120秒無操作、安全な状態の終了で中止します。確認後の他の財産操作は一時保留し、交換内容の変更・取消は「交換」から操作できます。
- ホストが双方の所持・未装着・残高・受入容量を再検証します。双方が出す資産を予約して各自のプロフィールに確定保存した後だけ、ホストが取引ID付きの**両者一組の成立記録**を `coop-trades.json` に一括保存します。片側だけ成立する記録は作りません。成立直後は双方のプロフィールを保存し、応答紛失／再接続では元のホストの記録を照会して一度だけ受け取ります。中止は保存済みの取消記録を確認してから予約品を返します。保存障害で結果不明なら予約を勝手に解除せず、トレードだけを保留します。
- **Continueとの整合**：交換の予約・成立・取消時に、保管庫・鞄・素材などの経済状態と乱数位置を既存の全RunCheckpointとロビー基準に一括反映し、非再帰の経済スナップショットと実行済みIDも保存します。本体に埋め込まれた古いチェックポイントにも、この保存済み経済状態を適用します。ホストの成立台帳は本体の巻き戻し対象外です。戦闘進行は従来どおり巻き戻しますが、交換時点までの財産と遺物IDの生成位置は戻しません。参加者の再開・再接続時機が違っても、渡した資産の復活や受領の二重実行を防ぐためです。
- **互換性**：共通Protocol 24とHelloは変更せず、専用の任意通信version 1を追加しています。ロビーでも使えるMirror通信を既存の `GameSettingsManager.customData` の対応広告で準備し、その広告の新しい更新を見てからだけ専用パケットを送ります。古い保存に残った広告だけでは送信しません。非対応・古い版・返事なしは交換だけ不可で、他の協力機能は続けます。保存形式6は予約・受領記録を旧版が捨てて財産を上書きしないための更新で、形式3〜5からのプロフィールリセットはありません。
- 実機のIMGUI描画・ソケット通信は未確認です。リンクした本番アダプターの一時スモークで、参加者同士／ホスト参加の交換、確認解除、両者保存、再送、取消・時間切れ、予約保存失敗、応答紛失と再読み込み、古いContinueからの重複防止を確認しています。
- **EN:** Open **Trade**, select a session peer, request and accept, offer unequipped stash/satchel relics and owned shards/tuning, then both confirm. Edits clear both approvals. Lobby and safe expedition moments only; cancel, disconnect, travel and 120-second inactivity abort. Both outgoing escrows must be durably saved before the host atomically journals both payouts. Receipts deduplicate retries and both profiles save immediately. Trade economics are reflected into retained Continue points plus a durable overlay; native progression rewinds, exchanged assets do not. Original-host queries recover lost results. Unsupported or silent peers disable trade only. Shared Protocol 24/Hello are unchanged; profile format6 retains escrow/receipt safety without resetting old profiles. Live Unity rendering and real multiplayer remain unverified.

### 星の位階と鍛錬（Issue #117・#120）

- 星由来のダメージ量・増幅量に `r(p)=1+1.5×p/500`（pは使用済み星点）を適用する。125/250/500/504点で1.375/1.75/2.5/2.512倍。装備由来の量、能力値、加速・CD・回復・軽減・無敵・確率・対象数・範囲・持続・発動条件は変えない。属性付与の値はnativeのスタック数と確率で、ダメージ係数ではないため据置。星の記憶ダメージはCrescendoの合計120%上限の外へ加え、Crescendo自身の最大40%は維持する。
- 個別のMemoryDamageは位階適用後に2.5%/点以上。購入費用cに対して `ceil(2.5×c/r(c), 0.001)` を基礎値の下限とし、最小の購入時点でも満たす。費用1の不足成分524件は1または2→2.493、実出力は1点時2.500%。接続星の費用を含む実戦ビルド全体の効率保証ではない。
- 割当の有効性にも、使用済み点で他の星ダメージが増える実出力を含める。局所的に上限へ達した星でも、その増幅が実際に増えるなら購入・保持できる。整数切捨て／千分率丸めでダメージが増えない場合は、その位階だけで有効とは扱わない。明示的な永久無効化は引き続き購入不可。返還では位階も減り、既存の一括返還承認を必要とする。
- 鍛錬F2：Vesperの閾値20→11%HP、Cetus16→9%HP、Mistの入口上限80→60、空殻の入口上限92→45・攻撃力/スタック0.69→1.38・上限星の増分各23→12。獲得1つと速度Choiceの2倍は維持。空殻の上限星2つでの素の最大攻撃力は95.22のまま。既定行動モデルの入口のみでは深度0は全4種Z4、深度1は全4種Z3（行動による差があり、全プレイヤーへの保証ではない）。
- 星図の本文は基礎値を示し、現在の位階倍率とダメージ係数を別表示する。数値は `tools/balance/stars.json` から生成する。星ID・rank・choice・費用・星図の保存項目・通信項目は維持し、ホストは取得内容から再計算する。内容が変わるため協力する全員を同じ版に揃え、鍛錬の更新は帰還後・次の遠征開始から適用する（遠征中の更新を推奨しない）。
- **EN:** Star damage quantities/amplification use `r(p)=1+1.5×p/500`, where p is spent points: ×1.375/1.75/2.5/2.512 at 125/250/500/504 points. Equipment, stats, haste, cooldowns, healing, reduction, immunity, chances, targets, radius, duration and activation conditions stay unchanged; native elemental stack/chance values are not damage coefficients. Star memory damage sits outside Crescendo's combined 120% cap; Crescendo retains its own 40% maximum. Each MemoryDamage effect meets 2.5%/point after rank even at conservative purchase cost c, using a base floor `ceil(2.5×c/r(c),0.001)`; 524 cost-1 components change 1 or 2→2.493, yielding 2.500% at 1 point. This excludes connector costs, not a whole-build guarantee.
- **EN:** F2 changes Vesper/Cetus thresholds 20→11%/16→9% HP, Mist entrance cap80→60, and Husk cap92→45, attack per stack0.69→1.38 and each cap-star bonus23→12. Gain1/double-gain choices remain; Husk's two-cap-star base maximum stays95.22. Under the default action model all four entrances cap at Z4/Z3 for depth0/1, not for every player. Descriptions separate base and current-ranked values. The balance table generates numerics; IDs/ranks/choices/costs, Star Map save fields and wire fields stay unchanged, with authoritative host recomputation and existing content matching. Everyone in co-op must use the same build. Update after returning, before the next expedition, not mid-run.
- **EN:** Allocation validity includes real damage gained through total spent points. A locally capped star can remain purchasable if it increases another star's ranked damage; unchanged output after integer/thousandth quantization is not sufficient. Explicit permanent disables still prevent purchase. Refunds reduce rank and retain the existing atomic-refund approval.

### 鞄のあふれと欠片・追加ダスト（Issue #200・#252）

- 容量を超えると、レア度の低い遺物から外し、同レア度ではスコアの低いものを先に外す。取引予約中の遺物は対象外。保管庫・遺失物のあふれも従来どおり欠片。
- 1個あたりコモン3／アンコモン6／レア12／エピック30／伝説60欠片（`Content.SalvageShards`）。強化値は加算しない。Infinityの無料供給上限と道標による素材化の抑止は従来どおりで、抑止された分は別素材へ振り替えない。
- 外した遺物の欠片量・個数をローカルで集計し、そのtick内で持ち主の `Profile` の `Materials.Shard` へまとめて加算する。先に保存・確保の境界へ達した場合は、その前に加算する。未確保の鞄素材には入れず、確保やホストの返事を待たない。通知もまとめて「鞄あふれ：欠片 +N（遺物 M 個）」と表示し、道標で素材化を抑止された個数も示す。追加ダスト設定がOFFなら新しいあふれの夢のダスト・RPCは発生しない。遺物ごとのあふれ通知・取引はどちらの設定でも作らない。
- 保存は通常のまとめ保存と既存のチェックポイントに従い、あふれごとの準備保存は行わない。現在の通信はProtocol 24、保存形式は上記 #255 の形式6。鍛冶・記録タブで利用者が選ぶ手動分解の報酬・取引処理は変更しない。
- ゲームのMOD設定に日英の「鞄からあふれた遺物で、欠片に加えてドリームダストも受け取る」を追加（既定OFF）。本体の設定保存を使う各プレイヤー自身の選択で、ホストの設定を参加者へ強制しない。ONなら欠片は全額のまま、旧換算と同じコモン15／アンコモン30／レア60／エピック150／伝説300ドリームダストを追加する（強化加算なし）。道標の報酬停止では両方ゼロ。Infinity無料供給の欠片上限は維持し、追加ダストは旧換算どおりレア度の全額。
- 追加ダストは遺物ごとのオブジェクトを作らず遠征の累積値へ加算し、フレームごとに最大1通で設定・遠征ID・台帳ID・累積値をホストへ送る。ホストは受信時には付与せず、持ち主ごとの差分をまとめて `EarnDreamDust` を最大1回／フレームだけ呼ぶ（その中の `AddDreamDust` と本体獲得RPCも各1回）。再送・古い累積値は台帳の支払済み値で除外。最後の未確認分は遠征終了後も保持し、支払済みの前回遠征の領収値も1件保持して、最終応答の紛失で次の遠征の追加ダストを止めない。
- 累積値・台帳ID・未確認分と支払済み台帳は既存の本体中断チェックポイントへ同じ時点で保存・復元する。任意の新メッセージ型だけを追加し、固定のHelloと既存パケット、Protocol 24は変更しない。参加者の設定／対応の返事が未到着、旧版、台帳の不一致、本体通貨の異常では警告し追加ダストだけを無効にする。支払状況不明の分は別台帳へ再払いしない。欠片・手動取引・MOD全体は停止しない。
- 前回遠征の未確認分は、別の遠征が始まった時点で確定不能（ホストは自分のnativeランの外では支払わない）とみなし、1回だけ警告して放棄する。この放棄で追加ダストだけを諦め、遺物の受け取り・欠片化と新しい遠征での追加ダストの再有効化を妨げない。同じ遠征IDの再開（続きから）が可能な間は保持し続ける。
- **旧保存の互換復旧（v2.3.1〜v2.4.0・旧 #123）**：保存に残るあふれの `PendingTrades` は削除せず、既存の受領記録を繰り返し照会して解決する。旧ダスト取引そのものは再送しない。同じ台帳で未払い・未送信と確認された旧義務は、既存の欠片回復処理に従う。支払い済みならダストも欠片も重複付与しない。支払い済みか不明な間は保留を残して欠片を追加せず照会を続け、確認不能な取引を利用者が明示放棄した場合も、支払われた可能性のあるダストに欠片を重ねない。本体中断保存と台帳の復元も維持する。この経路は旧義務専用で、新しいあふれには使わない。
- **EN:** Overflow removes the lowest-rarity, then lowest-score relic, excluding reserved relics, and locally totals 3/6/12/30/60 shards for Common/Uncommon/Rare/Epic/Legendary. Shards are credited within the tick or before an earlier save/secure boundary. The owner's saved mod-setting checkbox is OFF by default; ON adds the old unenhanced conversion amount of 15/30/60/150/300 Dream Dust without reducing shards. Waypoint reward suppression grants neither; Infinity's existing shard caps remain, while Dust uses the old full rarity rate. One cumulative update per frame lets the host grant the unpaid difference with at most one native EarnDreamDust/AddDreamDust/earned RPC per owner per frame. Receipts deduplicate retries and survive native checkpoint rollback; pending final totals survive run end, and one completed-run receipt handles a lost final ACK. Missing capability/settings, older peers or currency/ledger failures disable only the extra Dust with a warning. Existing packets, Protocol 24 and manual trades remain unchanged; no per-relic overflow trades, notices or save barriers are created. Profile format6 is described under #255 above.
- **EN:** An unconfirmed remainder from a previous expedition becomes provably unsettleable once a different expedition is active (the host pays only inside its own native run): it is abandoned with a one-time warning, forgoing only the extra Dream Dust. Relic pickup, shard conversion and the bonus in the new run re-arm and continue; the remainder is retained while the same run can still resume through Continue.
- **EN — legacy saves:** v2.3.1–v2.4.0 overflow `PendingTrades` remain recoverable through repeated receipt queries, not retransmission of Dust trades. Obligations confirmed unpaid/unsent in the same ledger use the existing shard fallback; recorded payment grants neither Dust nor shards again. Unknown payment status preserves the hold without extra shards, including when explicitly abandoning an unverifiable transaction that may already have paid Dust. Native checkpoint/ledger recovery remains in place for these old obligations only.

### 中断と「続きから」（Issue #97）

- 「メニューに戻る」「デスクトップに戻る」では未完了の遠征は中断のまま残る。メニューの全タブに中断中の案内を表示し、遠征が終わるまでプロフィールの切替と星図の変更はできない。装備・鍛冶の操作条件は従来どおり。「ロビーに戻る」の確認後は下記 #112 の敗北精算を行う。
- 本体の保存があるときは「続きから」で再開する。本体の保存に結び付いたMODチェックポイントへ、同じ遠征IDの鞄・未確保の欠片・撃破と報酬の状態を戻す。MODの最新プロフィールだけをそのまま重ねるのではなく、本体が再開する地点にそろえる。ただし上記 #255 の交換に反映した経済状態は巻き戻さない。
- プレイヤー間の交換を反映した経済状態以外では、再開時にその後の遠征で得た経験・確保済み報酬と保留取引も保存地点に合わせ、本体の通貨と既存の手動取引台帳を一緒に戻す。ロビーの装備変更は残し、鍛冶・工房などの変更も保存地点の遺物・素材で成立する場合は残す。巻き戻りで必要な遺物・素材がなくなる場合は、ロビーの財産変更をまとめて戻し、画面に理由を表示する。
- #104：製作・上等製作・合成など、乱数を使うロビー変更を残す場合は、その結果とロビー変更後の乱数状態を一緒に引き継ぐ。保存後の遠征で消費した乱数も含む現在の状態を採用し、消費回数の加算では再構成しない。ロビー変更を戻す場合や乱数を使う変更がない場合は、乱数も保存地点へ戻す。プロフィールから生成する遺物Uidは、保管庫・遺失物・分解待ち・鞄・保留報酬の既存Uidとの重複を避ける。確認はUid生成時だけ行い、毎フレームの処理や保存項目は増やさない。ホスト・参加者ともに同じCore処理を使う。
- 協力プレイではホストが再開する本体保存・チェックポイントに従う。参加者だけでホストの遠征を再開することはできず、各自のMOD保存に対応するチェックポイントが必要。本体の「続きから」がない場合も、中断中の表示だけで再開を保証するものではない。
- チェックポイントのない旧保存も読み込めるが、過去の保存時点のMOD報酬状態を後から復元することはできない。未完了の遠征が残っていることと、本体の保存から安全に再開できることは別。
- 新しい本体保存のチェックポイントが参加者側にない場合は、その遠征の報酬を停止して案内する（最新状態で続けて二重報酬を得ることはしない）。別IDで新規開始したときの未確保品の精算は従来どおり。通信はProtocol 24と中断対応の相互確認を使い、保存形式6の交換状態は上記の方式で保持する。
- #180：保存障壁では各参加者の報酬を固定し、本体の `onSaveEnded` 後に保存ファイルのIDを読み戻して確定通知を送る（終了通知は書き込み失敗後にも来るため、成功の証拠にはしない）。確定したIDより前だけを整理し、直前1件と以後の未確定候補は保持する。確認できないときは警告して履歴整理だけを停止し、MOD全体や進行中の報酬は止めない。失敗が続く間はMOD保存が大きくなるが、確認成功後に整理する。既に削除された候補の復元はできない。確定通知で報酬を再採取する旧参加者を防ぐため、Protocol 22で導入した保護を現在のProtocol 24でも維持する。
- **EN (#180):** Freeze each guest's rewards at the ordered save barrier. After `onSaveEnded`, read the native file's ID before sending a commit notice; the event also fires after failed writes. Cleanup retains the committed ID, its predecessor and all later unconfirmed candidates. Verification failures warn and pause cleanup only, not the MOD or current rewards. MOD saves grow while failures persist, then shrink after confirmation; previously deleted checkpoints cannot be reconstructed. Protocol 24 retains the Protocol 22 protection against re-capturing rewards on commit notices; matching versions are recommended, not required for admission.
- #48の未払い撃破と撃破factは、`bossTypeName`・`bossDropNightmare`・`bossDropDepth`も共通codecでチェックポイントへ保存・復元する。`rt.Boss`の予告・印・CDなどは部屋／Hero寿命の一時状態なので保存しない。本体再開で旧Heroを破棄し、新しいHeroのruntimeと復元済み装備・Buildから作り直す（mainの寿命規則を維持）。
- Infinityの累計Combat部屋数・周期・圧段階を決める状態、地図／区間／部屋の世代、共通選択のreceipt、報酬予算・入場済み部屋・帰還記録も同じチェックポイントへ戻す。本体の復元完了時に地図と照合し、追加のlazy-ready通知は待たず、ロード前の最新状態とは比較しない。時刻観測とACKの一時状態をリセットし、ロード・切断中の時間を予算に足さない。ロビーで選んだ次回のInfinity設定は維持する。
- #259 続き：通常モードも含め、復元完了で共有の復元フラグを解除し、保存されたゾーン進行の追従から確保選択へ進む。完了通知が30秒届かなければ警告し、本体のrunId・ゾーンが有効で遷移終了済みの場合は保存地点を復元して進める。別の遠征IDのチェックポイントは保持して復元だけをスキップする。本体ロード自体が未完了なら完了を捏造せず、その遠征のMOD報酬・Build反映のみ保留し、実際の完了通知で回復できる。チェックポイント不足の参加者保護、台帳と交換予約、巻き戻し→受領→新しいBuildの順序は維持する。
- **EN:** A suspended expedition locks profile switching and Star Map edits until it ends. Use Continue if a native save is available; guests follow the host. MOD checkpoints restore the same run's satchel, unsecured shards, kills and rewards to the native save's point. Each participant needs a matching local checkpoint. Legacy saves remain readable, but missing checkpoints cannot reconstruct past MOD rewards; a suspended-run notice does not guarantee that Continue is available.
- **EN (#104):** Retained random lobby edits keep both their results and the live RNG state, including post-checkpoint expedition draws. Rejected edits or edits without RNG consumption keep the checkpoint RNG state, except for the irreversible trade economy/RNG overlay described under #255. Profile-generated relic IDs skip IDs already held in stash, lost-and-found, pending salvage, satchel, deferred rewards or cooperative escrow. Current profile format6 and co-op Protocol 24 are described above.

### Infinity割り込みの互換性

- native対象の不足、パッチ適用・実行の失敗では、警告を出して **Infinityだけを無効化**する。MOD全体を停止せず、ゲーム全体をpauseしない。preflightは警告のみで、Harmonyの非公開内部APIや厳密なIL検査を起動の必須条件にしない。
- 必要なInfinityパッチが実際に適用できた場合にロビーからONにできる。無効時もOFFへ切り替えて通常遠征を開始できる。参加者の版・Protocol・内容・対応可否の差、Hello未着／遅延は警告のみで、開始・進行・装備・報酬を拒否しない（#246）。読めない通信だけを警告して捨て、次の通信を処理する。台帳の未精算、所有者やランIDの確認、本体保存とMODチェックポイントの整合保護は維持する。
- 無効化されたInfinityの保存内容は通常遠征へ書き換えずに保持し、新しいInfinity進行・報酬だけを止める。
- 実行中の検査で止めた Infinity は、その遠征が終わりロビーに戻ると使える状態へ戻る（`InfinityMode.RecoverAfterExpedition`。再起動までの無効は起動時のパッチ不足＝`DisablePermanently` だけ）。ロビーで止まった場合も、一度遠征を挟むまでは戻さない。地図表示の割り込み（`InfinityMapPresentation`）は1つの割り込みの失敗が5回に達するまで警告のみで、達したときだけ従来どおり止める。

### Infinityの地図とゾーン（Issue #208・#232）

- 開始部屋・現地図の訪問済み部屋と次の1部屋だけを表示する。新しい部屋に入るごとに次室を1つ開示し、ボスは実Combatクリア10／15／20部屋の周期に達したときだけ次室として現れる。商人・イベントは周期に数えない。
- 本体の有限グラフ・部屋寿命・読み込みを維持し、現地図を使い切ったら精算と全員の保存ACK後に同ゾーンを更新して続ける。保存待ちは5秒ごとに再試行し、30秒で警告してその遠征だけ解除する。未精算の撃破・報酬・取引の確認と重複排除は維持し、続きからでは報酬とreceiptを一緒に巻き戻す。全世代の地図を蓄積しない。全体／ミニ地図・ゲームパッド・tooltipの候補はホストが既存Mirrorで同期する。続きからもnative node statusから同じ次室を復元する。
- ボス撃破後の「潜行」では本体の全 `Zone_` リソースから順序・tierに関係なく次のゾーンを抽選する。候補が複数なら直前のゾーンを避け、ボスも移動先の本来の候補から抽選する。開始ゾーンと、ボス前の部屋不足による同ゾーン更新は従来どおり。
- ランIDから作る専用シードと保存済み区間／地図世代で抽選し、ホストのnative zone・node statusと既存共有状態を同期する。Continueも保存されたゾーンと地図へ戻る。本体のゾーン番号・ambientLevelは進めず、既存の深度・夢の圧で難化する。ゾーン切り替えが失敗した回だけ警告1回で元のゾーンを更新する。
- #267：ボス後の帰還・潜行・移動先はホストが決め、契約・道標・出来事は各自が選ぶ。ホストの確定操作後、接続中の全員の選択完了／スキップを最大60秒待ち、未選択は契約・道標なしで進む。切断した人は待たない。待機中はホストに相手名と残り秒数、ゲストに自分の選択の案内を折りたたみパネル・通常パネル・HUD・メニューへ表示する。
- 個人の選択は遠征・地図・区間・選択revision・本人に結び付け、1秒ごとの再送とACK、初回受領値の固定で重複適用を防ぐ。最後のパーティ決定は遅着参加者用に1件保持し、共有の道標snapshotで本人の選択を上書きしない。旅人・召喚・戦利品への道標効果は本人の値、本体の共通敵・夢の圧は従来のホスト規則を使う。保存ACKは選択完了とは別で、遠隔の追加待機は60秒期限の残りに収める。ホスト自身の保存・報酬台帳の確認は維持し、通信不一致・未着はその個人選択だけを警告して期限で解除する。地図生成・公開順、通常モード、PC要件は変更しない。
- 協力プレイは全員Protocol 24への更新を推奨する（旧版の純白の勝利処理には意味の差がある）が、版の差を理由に機能は止めない。Infinity codec version 1・既存runtime envelopeの項目は変更なし。#267は個人選択の任意RPCとsettings receipt、最終選択envelopeの省略可能項目を追加する。旧保存は従来のゾーンのまま再開し、次の潜行から抽選する。通常モードは変更しない。[設計と根拠](../../docs/specs/issue-95-infinity-mode.md)。
- **EN:** Infinity reveals visited rooms and one next room, with native bosses after 10/15/20 combat clears. Delve after a boss chooses among all native zones, avoiding the previous one when alternatives exist; exhaustion before a boss regenerates the same zone. Run-specific seeds and saved epochs keep zone/boss choices reproducible, with host synchronization and Continue restoration. Native zone index/ambient difficulty stays unchanged; existing depth/pressure scaling and reward limits remain. Failed switches warn once and regenerate the previous zone. Save waits retry every five seconds and, after 30 seconds, warn and lift only that expedition's save hold; unsettled rewards/trades and deduplication remain guarded. Continue may roll back rewards and their receipts together, except for the irreversible player-trade economy above. Protocol 24 is recommended; differing versions/content and missing Hello only warn, never disable existing features. Unreadable messages are individually discarded with a warning. Current profile format6 is described under #255 above.
- **EN (#267):** The host chooses Return / Delve and the destination; each player chooses their own pact, waypoint and event. Confirmed progression waits for connected guests to finish or skip, with a 60-second deadline and missing selections treated as no pact / waypoint. Disconnected players do not hold progression. Named waiting/countdown and personal-choice prompts remain visible in the collapsed panel, choice panel, HUD and menu. Owner-scoped ACK/retransmission freezes the first accepted selection; the retained final decision tolerates delayed or reordered synchronization. Traveler, summon and loot effects use the owner's waypoint; shared enemy pressure retains the host's rules. Remote save ACK waits fit within the remaining deadline; the host's own save and reward receipts remain guarded.


### 「ロビーに戻る」の敗北精算（Issue #112）

- 確認後の `DewNetworkManager.RestartSession()` を対象に、ホスト・本体の未決着・MOD の未決着・本体と MOD の runId 一致を確認する。結果画面からの通常復帰、決着待ち／精算済みの遠征、`EndSession()` のメニュー・デスクトップ復帰やキック経路は対象外。IL の厳密一致や MOD 全体の起動条件は追加しない。
- ホストは既存の `DreamforgeRunChoicesMsg` に `lobbyReturnRunId` と敗北・道標の状態を載せ、信頼性のある本体 Actor RPC でロビー遷移前に通知する。各 PC は対応する遠征だけを既存の `TryConcludeRun()` → `Rules.EndRun(..., false)` で一度だけ精算する。未確定の撃破や道標は既存の順序で処理し、保留分があればロビー遷移後も同じ敗北の精算を継続する。遷移中に参加者をホストと誤認しないよう、精算開始時の権限も保持する。
- 本体の中断保存は変更・削除しない。終了した runId を MOD 保存の省略可能な `lobbyReturnedRunIds` に記録し、チェックポイント復元でも履歴を消さない。後からその本体保存を再開しても MOD の遠征や報酬は作らず、「この遠征は『ロビーに戻る』で終了済みのため、MOD の報酬は出ません」と案内する。ホストの再開通知でも終了状態を伝える（`continueResumeSession` の予約値 `lobby-returned`）。
- 判別できない（本体と MOD の runId が食い違うなど）場合はその回は何もせず中断のまま（警告1回）で、次の正しい「ロビーに戻る」では精算される。判別中の本体操作の例外では警告を出し、ロビー復帰時の敗北判定だけを無効にする。読めない帰還通知はその1件だけ捨てる。通常の中断・MOD の他機能は停止しない。保存形式は5のまま。通信はProtocol 24だが、版の差だけでは拒否しない。
- 報酬停止中の Infinity 遠征（#131）は敗北待ちに入れない。その回は中断のまま（警告1回）で、Infinity の回復後に正しい帰還があれば精算される。精算待ちの保存・読込を経てから Infinity が停止しても、別 runId の遠征開始で待ちを放棄する（帰還済み runId は保持し、旧ランは通常の未解決ランと同じ扱い）。参加者の遠征未開始・観戦・ロード中・ゾーン番号未着の通知は見送るだけ（#132）で、機能は無効化しない。破損した帰還通知もその1件だけ警告して捨て、実際の本体操作の例外だけをその機能のfail-soft対象とする。
- 実機のメニュー表示・ロビー遷移・協力通信は未確認。リンクした本体境界テスト（`LobbyReturnTests`）で、ホスト・参加者の敗北精算・結果画面の非精算・判別できない場合のスキップ・判別失敗の無効化・「続きから」の案内と runId 保存・停止中の Infinity 帰還のスキップと再開・精算待ちの放棄・未開始参加者への通知の見送りを確認している。

### 純白の保留中の撃破の精算（Issue #71・#88）

- 純白の入口で選択を保留したまま戦った撃破には、戦ったときの潜行深度と道標を記録し、精算ではその値を使う。確保・潜行・勝利のどれで確定しても、戦った深度より浅くも深くもならず、次のゾーン用に選んだ道標（封じられた宝庫など）は当てはまらない。
- 道標・夢の深さはホスト共有、確保／潜行・契約は各自の選択。純白ではホストの確定後も参加者本人が選ぶまで撃破報酬を保留し、自動潜行で選択を飛ばさない。ソロ・ホストも明示選択まで保留する。通常ルートの戦闘継続による自動潜行は変えない。
- 保留したまま Primus を倒して勝つ確定では潜行しない。選択待ちを解くだけで、深度・確保ボーナス・最深記録は戦った深さのまま確保される。ホストも参加者も同じ確定経路を使う。
- 深さ0で出た敵も出現処理は済ませた扱いにする。あとで潜行して深さが1以上になれば、#60 の深度の揃え直しの対象になる。
- 保留中の撃破の `heat`/`waypoint` を維持し、欄のない旧保存データは精算時の現在の状態を使う（従来どおり）。#88では旧参加者の自動精算との意味の差を示すためProtocol 17へ更新した。現在は同版を推奨するが、版の差は警告のみ。実機での確認はまだ。

### 商人の遅延応答と保存互換（Issue #67）

- 商人の購入は個人の出来事の提示識別子に予約する。応答待ち・結果不明・確認不能の間は、同じ提示から再購入できない。新しいゾーンの提示は別の識別子を持つ。
- 遅れて成功応答が届いても対価は付与し、購入元と識別子が一致する商人だけを消費する。品質の抽選には、保存済み取引の購入時の潜行（`Heat`）を使う。ホスト自身と参加者は同じ購入・受領経路を使う。
- 保存形式は据え置き。提示の `eventId` と取引の `merchantOfferId` を追加し、既存の取引・結果不明・確認不能の記録は維持する。旧保存の提示には読み込み時に識別子を補う。購入元不明の旧商人取引が残る間は再購入を止め、成功しても現在の提示は消費しない（購入元を推測しない）。
- 通信形式は変更せず、Protocol 15を維持する。ゲーム内UIと2台協力プレイでの遅延応答は実機未確認。

### 夢の圧による欠片・悪夢化の倍率

- 圧が高いほど、敵を倒したときに欠片が出やすく、敵が悪夢化しやすくなる。圧の大きさは敵HP倍率の増分（夢レベル・使用星・深度・道標を含む。Infinityの圧段階は含めない）で、`倍率 = min(上限, 1 + 係数 × (HP倍率 − 1))`。係数と上限の原本は `tools/balance/pressure.json` の `dreamPressure`（欠片 0.25・上限1.5、悪夢化 0.20・上限1.5）。
- 欠片：確率で出る雑魚・通常の敵は出る確率に倍率を掛け（100%が上限。頭打ちの分は量で補う）、必ず出るエリート・ボスは量に倍率を掛ける。小数は確率で切り上げて期待値を保つ。倍率1では乱数の消費も従来と同じ。契約・今日の夢・道標の欠片倍率とは掛け算で重なる。
- 悪夢化：既存の深度による確率・装備の強さ・今日の夢の倍率に、さらに掛ける。深度0では悪夢化しない従来の規則は変えない（道標の効果は従来どおり）。確率は100%を超えない。ボスは対象外のまま。
- 欠片の倍率はホストが敵の死亡時点の圧から決め、撃破EventIdの末尾（`|shards:` ＋16桁の16進）に載せて参加者へ送る。末尾がなければ倍率1なので、Protocol・保存形式は変更なし。悪夢化の倍率はホストの出現処理だけで使う。
- インフィニティ中は両方とも対象外（供給予算が別にあり、実測した調整を保つため）。

### v1.27 の星図と移行

- 追加は506星（62ルート434星＋夢の輪72星）。核を含む容量はVesper/Lacertaが224、Cetus/Yubar/Husk/Mist/Nachia/Aurenaが223、Bismuthが202ポイント。通常の最大300＋図鑑4では全部を取れない（v1.31 で予算を150→300に。10倍の星図は別作業で追加）。
- 星のレベルは獲得済みポイント数（0〜300）。1個目は56経験、150個目までの累計は75,450、300個目までの累計は285,900。踏破時の自動精算には確保の20を重ねず、踏破100のみを得る。経験は全滅でも失わない。
- `starXp`のない旧保存は、その旅人の撃破数×1.2（端数切り捨て）から移行する。既存ID・段数は維持し、新しい予算を超える旅人だけ無料で全振り直しにして知らせる。
- 核の効果・IDは維持。Yubar「事象の地平」だけ1段の星の障壁を5→4にし、核の合計を35→32（効果上限40の80%）へ収めた。
- 連携は従来の別枠の条件付き補正。記憶加速は同じ発動の合計100%まで、余韻は装着条件を満たす発動元のうち最大値1つ（5秒・重ならず時間を延長）。装着条件を外すとその余韻も解除する。
- 夢の圧は各参加者の夢レベル1〜30・使用済み星0〜300を制限してから平均する。未受信の参加者は1・0、死亡者は含め、ロビー・観戦者・退出者は除く。最大でHP×2.375・与ダメージ×1.675。同じ進行度なら1人と4人で同じ倍率。
- 通信は当時Protocol 5だった（現在は冒頭のProtocol 24）。同じ版を推奨するが、版の差だけでは能力を拒否しない。`Build`の`d:`が夢レベル、`a:`が使用済み星で、ホストの倍率は`DreamforgePressureMsg`で共有する。
- 本クローンには`.ref/dump`がないため、共有の`C:/Temp/sod-reflect/dump`で署名を照合。`EntityStatus.finalStatsProcessors`・`FinalStats.maxHealth`と`Actor.dealtDamageProcessor`・`DamageData.ApplyAmplification`を使い、再計算は本体のHP割合を保つ。Coreの遠征・配分・保存／移行・通信・圧は実行スモーク済み。Unity上の1080p表示と実際の協力通信は未確認。

### v1.30 の合わせ技 / Memory-pair combos

- 枝はロードアウトの順を基に、アイデンティティA → Q-A → R-A → 移動 → アイデンティティB → Q-B → R-B と並ぶ。Bismuthは視界 → 無垢な魂 → 歪な疾走 → 業火物語 → 勇敢なる心 → 歪な精神。星と橋のIDは変更しない。
- 62組の定義は`PairCombos.cs`に集約。効果量はレビュー済みの1・2・3段の値をそのまま使う（単純な比例計算にはしない）。
- 移動の記憶は原則として受け手。ダメージのある重装タックル・フロストチャージは命中だけを起点にできる。回避・移動の使用そのものでは発動しない。
- Equip both memories and allocate at least one rank in the bridge and both adjacent fourth stars. Marks last four seconds; the Cataclysm and Serpentine Blessing windows last twelve seconds and other windows last four seconds. Cooldowns apply only where specified; extra damage cannot start or complete another combo.
- 統合後の通信はProtocol 9。新しい`c:`セクションは合わせ技IDと橋の段数を運ぶ。旧版は非空の未知セクションを無視できないため、参加者全員の版を合わせる。
- 星図の説明は日本語・英語で生成し、2つの記憶の装着条件を✓／・で表示する。Unity内の表示と実戦は未確認。

### 星の説明・取得条件・左ナビ（Issue #101）

- 星の本文は「何が増える／減る」→発動条件→対象・数値の基準・持続・再使用間隔の順で日英生成する。通常星の数値は1段あたりと明示し、段ごとに異なる合わせ技は段別の値を列挙する。
- 連携相手は `記憶『公式名』` / `Memory “official name”`、`エッセンス『公式名』` / `Essence “official name”`。対象限定の強化は障壁・属性付与・追撃など実際の効果と連携する記憶を表示する。確率の加算はパーセントポイント、効果量の相対強化は元の値への倍率も示す。
- 星図・ツールチップ・二択カード・WikiGen は `StarMapPresentation.EffectDescription` の共通本文を使う。Wiki は日英を併記し、Unity用の装飾タグだけ除く。刻印かどうかは効果の型で判定し、同じ領域にある通常星へ刻印の固定費用を表示しない。説明生成の例外は警告を出し、その星だけ簡易表示にする。取得・戦闘効果やMOD全体を停止しない。
- 取れない星には未取得の前提星・隣の星の名前、必要／現在の段数・熟練度、必要／残り／不足ポイント、刻印の満杯の枠を表示する。二択には取得済みの候補名と「両方は同時に取得できない」を表示し、無料の切替は従来どおり許可する。
- 左の「星一覧」は星団名・星団内の順番・個々の星名・効果要約・取得済み／取得可能／条件不足・現在段数を表示する。クリックは既存の `StarJumpTo` でその星へ移動する。リストは可視行だけ描画し、本文と状態は内容・言語・取得状態の変更時に作る。星図の座標・結線・描画位置計算は変更しない。
- 旅人別TSVの出力：`DOTNET_ROLL_FORWARD=Major dotnet run --project tools/WikiGen -c Release -- --export-stars <出力先>`。従来の列・セル内の改行エスケープを維持し、`display_ja/en` と `wiki_ja/en` は同じ本文になる。

Gemini案より実コードを優先した箇所：

| 案・疑義 | 採用しない理由 |
| --- | --- |
| `cetus.bridge.b5.choice` の +0.25% を +25% へ換算 | `ModifierUnits.FromPercent(0.25)` は25単位、適用倍率は `(10000+25)/10000 = 1.0025`。+0.25%が実効果。 |
| 記憶加速を「クールダウン回復速度 +」 | Hostは最大クールダウン基準の即時短縮。残り時間を減らす量として記載する。 |
| 次の通常攻撃強化を「通常攻撃ダメージの8%／12%」 | `MemoryPrimedRuntime` は攻撃力・魔力の高い方を基準にした独立追加ダメージ。 |
| パリィの使用条件に「反撃成功」を追加 | 対応星の条件は `ConfirmedUse`。成功限定の条件を足さない。 |
| 汎用刻印の費用を `RankCost=1` にそろえる | 刻印費用は専用の3ポイント。型の一般ランク費用を購入費用として読まない。 |
| `DreamDustDelvePct` を「潜行中だけ +10%」 | `CurrencyStars.DreamDustPercent` は通常も増加し、潜行中は増加分が2倍。通常+10%・潜行合計+20%と記載する。 |
| Powerに配置ルートの記憶の装備条件を追加 | `Build.ComputeTree` はこれらを無条件で加算する。配置と発動条件は別。 |
| エッセンス追加枠を配置ルートの記憶だけに限定 | `HostAuthority.GemSlots` は装備中のアイデンティティ／移動枠に作用する。 |
| 烈火を「通常攻撃が4回命中ごと」・四の型を「毎3発へ周期短縮」 | 烈火は強化4発目の発射で準備して次の命中で消費。四の型の接続先は強化攻撃の開始位置で、周期短縮とは断定しない。 |
| アンストッパブルを「全状態異常無効」、属性Statを「全属性ダメージ一律増加」 | 接続先は行動妨害無効と本体の属性効果増幅。効果範囲を広げて説明しない。 |




## 操作

- F6：Dreamforge のメニュー（装備・鍛冶・星図・工房・記録）
- F7：確保する、F8：深く潜る（確保地点のパネル表示中）
- キー・言語・表示倍率・拾得通知・HUD の表示量（詳しく／簡潔／なし）はゲームのMOD設定から変更できる
- 星図の「全体を表示」は現在の星の位置から表示倍率を決め、端に余白を残す。拡大縮小は通常0.15〜3倍だが、大きな星図では全体が収まる倍率まで下限を下げる。星の検索・星群一覧からは現在の倍率のまま目的の星へ移動できる。
- **ゲーム全体の軽量化（v1.8）**：MOD設定の「ゲームが裏にあるときのFPS上限」（既定20、0で無効）と「軽量化」（なし／軽め／強め）。
  - 裏にあるときのFPS上限：Alt+Tab などでゲームが裏に回っている間、フレーム数を抑えて電力と発熱を減らす。戻ると本体の設定に戻る。
  - 軽め：影の届く距離を6割にし、影の段階を2つまで、追加ライトを2つまでにし、遠くの物を早めに粗く描き、キャラのメッシュの焼き込み間隔を0.15秒にする。
  - 強め：さらに影を4割・1段階に、エフェクト品質を本体の「低」相当に、アンチエイリアス（MSAA）をオフに、メッシュの焼き込み間隔を0.2秒にする。
  - 最大（v1.8.1）：強めに加えて描画解像度を0.75倍にする（画面は少しぼやけるが、描画の重さが大きく減る）。
  - どれも見た目と描画だけを変え、ゲームの進行や協力プレイの同期には触れない。「なし」に戻すと元の値に戻る。

開発者モードのコンソール（`）で使える確認用コマンド：`dreamforge_perf`（このMODの1フレームあたりの処理時間）、`dreamforge_status`、`dreamforge_stats`（キャラの最終能力値と補正）。

### 開発専用：native 記憶・エッセンス2品

既定は無効。起動時に `Application.persistentDataPath/QuickSave/Mods/DreamforgeRPG/` の **`dev.flag` と `native-dream.flag` の両方**がある場合だけ有効になる。協力で確認する場合は全参加者の MOD と両フラグを揃える。

- コンソールの `dreamforge_native_status` で有効状態と2品それぞれの登録 readiness をログへ出す。ホストだけが `dreamforge_native_give 0`（護りの種）と `dreamforge_native_give 1`（芽吹きの種）で **本体のワールド拾得物**を作れる。
- 通常操作で拾い、護りの種をエッセンス枠、芽吹きの種を通常記憶枠へ装着する。護りの種は品質I固定：装着先の記憶を使うと最大HPの3%の障壁を4秒、8秒に1回。芽吹きの種は native passive：Monster を5体倒すごとに自分と10m以内の味方 hero を各対象の最大HPの8%回復する。
- 解除後の停止、再装着・部屋移動の重複なし、ソロ Continue、フラグなし／MOD無効で元の品へ戻ること、協力での表示・効果・各PCのログを確認する。保存は `Gem_C_Quicksilver` / `St_C_MassProtection` と既存辞書内の文字列の印で、新しい保存型・network schema・assetId は足さない。
- 同じ具体型の native 制約により、原型 Quicksilver と護りの種は同時装着できない。試作品と原型の交差合成は拒否する。入手は開発 grant のみで、独立したドロップ・ショップ・図鑑行・新実績は追加しない。

**この2品の Unity 実機確認はまだない。** 品ごとの初期化失敗は警告1回で vanilla fallback とし、通常の起動を止めない。[方式比較・コード根拠・PC確認手順](../../docs/specs/v2.10-dream-essences.md)を参照。

## 構成

| ファイル | 役割 |
| --- | --- |
| `DreamforgeMod.cs` | ModBehaviour の入口。キー入力、ライブリロード時の後片付け、確認用コマンド |
| `ClientSession.cs` | 各PCの処理。プロフィールの読み書き、撃破・ゾーン移動・勝敗をルールへ流す、Build をホストへ送る |
| `HostAuthority.cs` | ホストの処理。Build を StatBonus として付け、固有効果（PowerRuntime）の結果をゲームへ作用させる |
| `HostAuthority.BossRuntime.cs` / `HostAuthority.BossMechanisms.cs` / `HostAuthority.BossDemon.cs` | 装備profileのホストdispatch、8共通executor、荒ぶる樹界の予約／回収／列／左右爪循環。記憶・エッセンス変更でも独自状態を破棄 |
| `HostAuthority.BossNativeAdapters.cs` | native主撃・記憶・移動完了・damage・ヒステリーの親／instance寿命と固有速度補正 |
| `HostAuthority.BossVisuals.cs` / `ClientSession.BossVisuals.cs` | owner/run/room/equipment epoch付き生存snapshotとMODの幾何表示（1秒同期、pause対応） |
| `DreamforgeUi.cs` / `UiStyles.cs` | IMGUI のメニュー・HUD・確保地点パネル・通知。日本語はOSのフォントを動的に読み込む |
| `Patches.cs` | メニュー表示中のキャラ操作の停止 |
| `NetMessages.cs` | ゲームの CustomRpc で送る型 |
| `about/` | Workshop 用（metadata.json・description.txt・icon.png・preview.png）。画像は `tools/make_about_images.py` で再生成できる |

ゲームのルールはすべて `../SodRpg.Core/Game/`（純C#、`dotnet test` で検証）にあり、このプロジェクトはそれをコンパイルに取り込んで1つのDLLにする（ゲームは読み込み時にアセンブリ名を書き換えるため、DLLを分けない）。

v1.23 の固有効果は、終曲・会心の余韻・足枷・結晶共鳴・獲物の誇り・溢れる命・祈願・飛び火の8種。ホストがスキル使用・会心命中・状態異常・超過回復・聖堂使用・火の付与に反応し、エッセンス品質とハンター追跡度を0.25秒ごとに参照する。祈願はゾーン読み込み時にリセットする。各効果の初回発動時に `[DreamforgeRPG] power Finale triggered` の形でログを出す（効果名は列挙値名）。

v1.23 のイベントとメンバーの署名は `.ref/dump/` で確認した。実機でのイベント配信とクールダウン割合の挙動は未確認。Core の境界条件の回帰テストは `NewPowersV123Tests.cs` にあるが、今回のファイル編集のみの作業ではビルド・テストを実行していない。

v1.24 の変種通知は `DreamforgeVariantMsg`。途中参加向けに悪夢と同じ5秒間隔で再送し、クライアントはスポーン前の通知を保持してモデルの準備後に色・大きさを付ける。死亡・セッション切替・MOD解除時に補正を外す。ホストはセッション最初の変種出現と最初の死亡爆発をログに出す。

変種のゲームAPI署名は `.ref/dump/` の `Entity`・`EntityVisual`・色／変形修飾子・`DamageData`・`Actor`・`ActorManager`・`ZoneManager` で照合した。ダメージプロセッサの登録／解除は既存の旅人向け実装と同じ方法を使う。当時はプロトコル2への更新と旧版拒否を行ったが、#246では読めるBuild・取引を版の差だけで拒否しない。実機でのプロセッサ順序や描画・協力同期は未確認。

## ビルドと配置

```
dotnet build src/SodRpg.Mod -c Release -p:GameDir="D:\app\stm\steamapps\common\Shape of Dreams"
```

`GameDir` は環境変数 `SOD_GAME_DIR` でも指定できる。ビルドすると `<GameDir>/Mods/DreamforgeRPG/` に DLL と `about/` が置かれ、ゲームのMOD管理で有効化できる。Workshop への登録は、開発者モードのMOD管理で本MODを選んで「Upload」から行う（初回は非公開で作成される）。

保存先は `<persistentDataPath>/QuickSave/Mods/DreamforgeRPG/profile.json`（Windowsでは `%USERPROFILE%/AppData/LocalLow/Lizard Smoothie/Shape of Dreams/...`）。一時ファイル→読み戻し→置換で書き、直前の内容を `.bak` に残す。

## 実機確認の状況（2026-10-01、Windows 11、ゲーム r.1.4.0.13_s）

確認済み：中断（ロビーに戻る）後に新しいランを始めたときの全滅精算（遺失物・残響）、最初のゾーンで確保地点を出さないこと、セット効果と狙い系統の表示、遠征結果パネル（確認用コマンド経由）、読み込み、日本語UI、遺物の装着・比較、ホストでの能力補正の反映（`dreamforge_stats` で攻撃力%・防御・移動速度が一致）、ドロップ・レベルアップ・確保の流れ（模擬撃破）、ライブリロード、他MOD 2つとの併用。

既知の制限：協力時のBuildはホストが正規入力から再構成し、上限と所有者を確認する。信頼できる仲間との協力を前提とする。版・Protocol・内容の差は警告のみで、読み取れる通信の処理を続ける。異なる旧版のゲーム上の意味まで一致する保証はない。

未確認（プレイヤーによる確認待ち）：実際の敵の撃破によるドロップ、ゾーン移動時の確保地点、全滅・踏破の精算、固有効果の体感、2人以上の協力プレイ、数値の手触り。
