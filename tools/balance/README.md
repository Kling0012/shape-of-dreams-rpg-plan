# バランス外部定義・比較（Issue #149、#117・#119・#120・#121）

`forge.json` の鍛冶・覚醒・合成・分解・現行工房・イベント保証強化、`stars.json` の7種類の星の基準値・倍率・星ダメージの位階係数、
`run-growth.json` の鍛錬の閾値・上限・1スタック量・上限増分・効果増分、
`star-progression.json` の星XP曲線・費用・報酬・刻印枠の解放、
`gear.json` の装備レベル成長・固定攻魔上限、`sets.json` の通常セット2/3/6部位効果、
`pressure.json` の夢の圧・深度、`monsters.json` の悪夢・変種・敵行動、
`infinity.json` の供給予算・周期・圧段階上限を型付きC#へ生成します。
記憶ダメージはmanifestの480親＋258選択肢と旧ルート98成分、計836欄を移行済みです。
星ID・段数・費用・選択肢・保存形式5・Protocol 23は維持し、調整した有効値を内容照合に含めます。
段階2ではMemoryHaste・GimmickBoost・GimmickParam・Notable・Keystone・Statの
明示的な効果欄と旧星・汎用星・sampleの数値を追加しました。今回の切替ではゲーム値を変更せず、
生成後の採用済み定義と内容指紋を維持します。実行時のJSON読込はありません。
段階3では鍛冶の残りを `forge.json`（schemaVersion 2）へ集約しました。
保存済みの履歴番号・旧覚醒段・退役工房の返金原本は変更しません。

## コマンド

Python 3 と .NET SDK が必要です。`DOTNET` を指定しない場合は PATH の `dotnet` を使います。
.NET 8 ランタイムがない環境では、利用できる上位ランタイムへの移行を必要に応じて
呼出側で `DOTNET_ROLL_FORWARD=LatestMajor` に指定してください。ツールはこの設定を変更しません。

```sh
# 通常の全検証＋比較（低速テストは自動では実行しない）
DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/balance/run

# 既存の低速テストも追加
python tools/balance/run --slow

# 明示的なスモーク／レポート専用。検証済みという扱いにはしない
DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/balance/run --no-tests

# 一時ディレクトリで小規模に計測。相対出力先は呼出時の作業ディレクトリ基準
python tools/balance/run --no-tests --out-dir /tmp/sod-balance-smoke --runs 2 --players 4 --seed 1

# 生成と、書込なしの鮮度確認
python tools/balance/gen_cs.py
python tools/balance/gen_cs.py --check
```

`run` は実行可能です。任意の作業ディレクトリから絶対パスでも呼び出せます。
入力検証→生成→生成鮮度確認→BalanceSim Releaseビルド（1回）→
`python tools/test_changed.py --all`（`--slow` 時は同オプション追加）→
`python -m unittest discover -s tools/tests -p 'test_*.py'`→鍛冶→記憶効率→種類別星値→星XP→鍛錬・感度→圧・敵→Infinity→遠征→比較の順です。
`--no-tests` は2つのテスト実行を省略し、生成・ビルド・実測は実行します。
8モードは別プロセスで起動し、Coreの静的状態を共有しません。
既定の遠征は新規プロフィール3遠征×8人、seed=1、2ゾーン×2部屋。鍛錬の `v132stars` は同じ人数・遠征数・seedで4ゾーン×5部屋、
4旅人×深度0〜5×secure/greedy×6構成と入口のみの3感度条件を測定します。その他は既存モード既定です。
Infinityの常用比較は `--infinity-scope comparison`：30分×通常深度0/5と4構成の6行、
人数・seedはrunの指定値、周期は現在の既定周期。長時間の保証・蓄積予算感度は単体の `--infinity-scope full` で測ります。
500pt星図の長時間シミュレーションは実行しません。

## 鍛冶の調整（Issue #149 段階3）

数値原本は `forge.json`。1セルを編集し、`tools/balance/run` で生成・全通常テスト・比較を実行します。
生成先は `src/SodRpg.Core/Game/Balance/Forge.Generated.cs`。Coreの実行・候補判定・ゲーム内説明・
WikiGenの鍛冶説明・BalanceSimは同じ生成済み値を使い、実行時に表を読みません。

| 表のセクション | 内容 |
| --- | --- |
| `enhanceFailure` | 失敗率3係数、失敗後の降格確率（0〜1）・段数 |
| `enhancement` | 基本上限、突破の上限増分、5節目、節目の固有効果倍率、能力／固有効果の42倍率、20強化費用、Epic以上の素材倍率 |
| `awakening` | 累計閾値・固有効果倍率・特性倍率の12欄、敵の格別獲得点・悪夢倍率 |
| `limitBreak` / `retune` | レア度別突破上限と費用、再調律の上限・候補数・基本費用・回数別増分 |
| `affixReroll` | 特性洗い直しの基本欠片・調律石と回数ごとの増加倍率 |
| `craft` / `synthesis` / `salvage` | 製作費用・幸運、合成の材料数／費用／枠指定倍率、分解報酬・強化費用返金の除数 |
| `guaranteedEnhancement` | 泉・鍛冶の祠・鍛錬の祭壇の保証強化段数、祠の基本欠片 |
| `workshop` | 現行6種の段別欠片／調律石と効果量。退役3種の返金定義は対象外 |

配列は0始まり。例：`enhancement.shardCosts[0]` は現在+0から+1への基本欠片、
`awakening.thresholds[1]` は覚醒Ⅰの累計必要点。`statPercents` / `powerPercents` は
100が1倍、`demotionChance` は0.5が50%。同じ数字でも異なる意味の原本は統合しません。
強化の20段・覚醒3段・節目5件・突破費用3件などの配列長は固定です。
範囲・整数性・積のInt32収容・閾値／節目の順序を生成時に検証し、不正な表は生成前に拒否します。
特性洗い直しの `growthMultiplier` は1〜8の0.5刻みのみ。
整数の有理数計算で最後に切り上げ、従来のInt32飽和を維持します。任意小数への丸めは行いません。
保証強化の候補は指定段数ぶん上限まで空きが必要で、実行と説明も同じ段数・費用を参照します。
ドリームダスト換算などの経済係数と、旧保存の返金／回復義務はこの表へ移しません。

`forge` 比較は実支払い額（Epic以上の素材倍率込み）・強化返金込みの分解報酬を列挙します。
特性洗い直しは済ませた回数0・1・2・3・10・Int32最大の代表条件、工房は対象以外0段、
出来事は必要な供物がある条件。覚醒点は深度等の外部補正前です。
上限・対象外の操作は `null` とし、架空の0や差分を作りません。
非拘束の係数変更などで実際の合法状態の結果が変わらない場合、原本の変更は `tableMetadata` に残ります。

同条件で2回実行した比較表の例（無調整移行なので差は0）：

| 指標 | 単位 | 現在 | 前回 | 差 | 相対差 |
| --- | --- | ---: | ---: | ---: | ---: |
| Epic 突破0 現在+0 次の強化支払い | shards | 40 | 40 | 0 shards | 0% |
| +20 固有効果倍率 | percent | 145 | 145 | 0 pp | 0% |
| 覚醒1 累計必要点 | points | 5000 | 5000 | 0 点 | 0% |

例えば基本欠片を20→21へ調整すると、Epicの支払いは40→42（+2 shards、+5%）。
これは操作例であり、チェックインした表の値は変更していません。
移行前の値は生成器内の凍結した互換参照との比較にのみ使い、欠落セルの既定値には使いません。
従来値は追加の鍛冶指紋レコードを生成せず、採用値が変わった欄だけ安定key・型・単位・値を
既存ContentFingerprint入力へ追加します。保存形式5・Protocol 23は維持します。

## 鍛錬・星XPの調整（Issue #149 段階4）

数値は無調整で移行しました。表の1セルを編集して `tools/balance/run` を実行すると、
型付き生成・全通常テスト・実測・前回成功との比較をまとめて実行します。

| 原本 | セルと単位 | 生成・実行先 |
| --- | --- | --- |
| `run-growth.json` / `effects` | `<星ID>/growth/threshold`、`cap`、`effects/<番号>/amount`、`capBonus`、`effectPct`。Choice子は `options/<番号>/growth/` | 既存の4旅人の `StarClusters/*.Generated.cs`。Compose・説明・ホスト帳簿は同じ定義を使用 |
| `star-progression.json` / `maxPoints`、`pointCost` | XPによる獲得点上限、`perPoint × k + offset` がk点目の必要XP。累計は同じ曲線の総和 | `Game/Balance/StarProgression.Generated.cs` → `StarProgression` |
| 同表 / `rewards` | 確保・踏破・通常／中ボス／ボス撃破の基本星XP、悪夢倍率。夢XP・覚醒点とは別原本 | 実際の撃破・確保・踏破経路 |
| 同表 / `keystone.unlockLevels` | 2・3枠目を解放する獲得星レベル | `KeystoneSlots` と既存表示 |

鍛錬は4入口・8親修飾・8選択肢の46欄を独立原本にしました。manifestはcanonical `valueRef`だけを持ち、
旧 `stars.json.runGrowth` とeffectPctのliteralは残しません。`amount` は表示単位で0.001刻み、
生成時に既存AmountMilliへ変換します。閾値はHP系なら最大HPの百分率、他は出来事の回数、
`cap` / `capBonus` はスタック数、`effectPct` は1スタック効果への上乗せ百分率です。
同じ数字でも別の星・選択肢のセルは統合せず、旅人／種類倍率を鍛錬へ掛けません。

不正な型・精度・欠落／余分な参照、既存受理範囲外は生成段階で拒否し、全入力のrender成功前には公開しません。
RunGrowthの通信受理上限・MaxEntries、保存済み履歴は表へ移さず固定です。
星点上限は既存の500を超えられず、XP総和・撃破XPの積はInt32内を要求します。
刻印の `maxSlots` は保存／通信の3枠構造なので3に固定、解放レベルは正・昇順・獲得点上限以内です。
旧保存移行の `LegacyXp`、星ダメージ位階の `stars.json.rankScaling` は別の意味なので変更しません。
実行時JSON読込・Protocol変更・保存形式変更・起動時の新しい必須検査はありません。

`star-progression` は全点の費用・累計XP・刻印枠と基本撃破／確保／踏破XPをCoreから取得します。
`v132stars` は既存の実BuildとRunGrowthLedgerから最終stack/cap、各ゾーンのstack、初到達zone、
最終stat、行動仮定への感度、遠征の星XPを構造化します。被ダメージ・障壁・パリィ・会心どめの頻度は模型の仮定であり、
実戦の勝率・踏破時間の予測ではありません。比較条件に模型値・感度条件・構成・runtimeを記録します。
未到達zoneはnull＋`not-reached`で保存し、到達／未到達の遷移は数値0からの差にしません。

```sh
# 星XPだけを直接出力（先に生成とReleaseビルド）
DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet run --project tools/BalanceSim -c Release -- \\
  --mode star-progression --metrics-json /tmp/star-progression.json

# 到達zone・能力値・感度を含む既存模型
DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet run --project tools/BalanceSim -c Release -- \\
  --mode v132stars --runs 1 --players 1 --seed 149 --metrics-json /tmp/v132stars.json
```

無調整移行の実測では、変更前後の `v132stars` の136表行の数値と内容指紋
`10579-ab35d865a569c87b` が一致しました。同条件の無変更比較は差0になります。
隔離した一時生成物で確認した調整例（リポジトリの表は無変更）：

| セル／指標 | 前 | 後 | 差 |
| --- | ---: | ---: | --- |
| `pointCost.perPoint` 6→7：1点目の必要XP | 56 | 57 | +1 xp |
| 同：500点の累計必要XP | 776500 | 901750 | +125250 xp |
| Vesper閾値11→12：深度0・secure・入口のみの最終stack | 60 | 58 | −2 stacks |
| 同：初到達zone | 4 | 未到達 | 4 → 未到達（相対差なし） |

移行回帰はコンパイルされた46欄と原本の一致、旧XP曲線・報酬・解放境界、
旧鍛錬指紋レコードの完全一致を確認します。有効値の調整は既存FNV内容照合へ流れ、
空白・キー順・比較条件やファイルのSHA検証は指紋に持ち込みません。

## 圧・悪夢・敵種・Infinityの調整（Issue #149 段階5）

179係数を無調整で移行しました。表の1セルを編集して `tools/balance/run` を実行すると、
生成・全通常テスト・Coreの実測・前回成功との比較まで実行します。ゲーム実行中のJSON読込はありません。

| 原本 | 調整欄 | 生成先 |
| --- | --- | --- |
| `pressure.json` | `dreamPressure` の無料夢レベル・HP／damageの夢レベル／星点／Infinity段係数7欄、`dreamDepth` の最大深度・HP／damage／幸運／覚醒／星XP／追加部屋7欄 | `Game/Balance/Pressure.Generated.cs` |
| `monsters.json` | `behavior` 27欄、`nightmare` 39欄、`variants` の確率・性質・欠片9欄、`variantStats` の30種47能力値 | `Game/Balance/Monsters.Generated.cs` |
| `infinity.json` | `rates` 10欄、共通 `killMix` 4欄、`bursts` 16欄、`rooms` の4種cap／increment8欄、`run` の既定／3周期／圧上限5欄 | `Game/Balance/Infinity.Generated.cs` |

`pressure` は夢Lv1/5/10/20/30×使用星点0/50/250/500×深度0〜5×道標なし／儚い記憶
×Infinity段0/1/10/100を `DreamPressure` の実式で列挙します。潜行Heatは夢の深度とは別軸です。
悪夢の出現確率・接頭効果数・能力値、30変種の能力値・欠片倍率、行動係数も独立した単位で比較します。
単独接頭効果の投影であり、ランダムな複合結果・勝率・実クリア時間を予測しません。

`infinity` はEpic+／Legendaryの解析認可予算と、実 `Rules.OnKill` を通った認可／抑止、
遺物・欠片・調律石・夢XP・星XP・覚醒の供給／時を保存します。全43係数も直接比較できるので、
模型の条件で拘束しないburst／cap変更を見落としません。期間・撃破構成・時間・装備・乱数・人数等の模型条件を記録します。
周期はshort/middle/long/defaultの選択役割を固定条件とし、その時点の数値を指標と `resolvedIntervals` に記録します。
内容指紋が異なるだけでは比較を拒否せず、模型・外生条件・型・単位の違いで比較可否を判断します。
全セッションを戦闘時間とする経済模型であり、通信性能・実機の戦闘速度の測定ではありません。

表は欠落／未知キー・bool・非有限値・整数欄の小数・受理範囲外を生成前に拒否します。
連続係数は最大6桁の小数精度で既存float/doubleへ生成し、演算型・順序・丸めを維持します。
深度は既存の5、圧段階は100、周期は固定graph容量4096を超えられません。
秒換算3600・epsilon・graph容量・enum／bit値・変種の色／見た目Scaleは調整表に移しません。
数値入りの悪夢／変種説明とInfinity予算説明は同じCore値を使用します。
帰還記録の過去の周期・深度・圧は現在の上限で書き換えず、固定した保存受理境界で保持します。
保存フィールド・保存形式5・Protocol 23・既存の機能単位の内容不一致ゲートは変更しません。

```sh
# 通常の全検証＋前回成功比較
DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/balance/run

# 直接計測（先に生成とReleaseビルド）
DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet run --project tools/BalanceSim -c Release -- \
  --mode pressure --metrics-json /tmp/pressure.json --out /tmp/pressure.md
DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet run --project tools/BalanceSim -c Release -- \
  --mode infinity --infinity-scope comparison --players 1 --seed 149 \
  --metrics-json /tmp/infinity.json --out /tmp/infinity.md
```

無調整移行の内容指紋は変更前後とも `10579-ab35d865a569c87b`。
最小限の移行回帰は旧式の圧・深度・敵定義・供給結果と、段階5追加前の内容指紋計算との完全一致を確認します。
悪夢20種・変種30種の日本語／英語説明も、throwaway実行で移行前と完全一致しました。
以下は隔離した一時生成物で実測した調整例（チェックインした表は無変更、表示のみ丸め）：

| セル／指標 | 前 | 後 | 差 | 相対差 |
| --- | ---: | ---: | --- | ---: |
| `dreamPressure.healthPerLevel` 0.025→0.030：Lv30・星0・深度0のHP倍率 | 1.625 | 1.75 | +0.125倍 | +7.6923% |
| `nightmare.BerserkAttackPct` 40→44：狂暴の攻撃力 | 40% | 44% | +4 pp | +10% |
| `rates.shardsPerHour` 180→198：欠片の認可予算／時 | 180 | 198 | +18 shards/hour | +10% |
| `run.defaultInterval`／`shortInterval` 10→11：既定周期 | 10 | 11 | +1 rooms | +10% |

調整した有効数値だけを型・単位・安定key付きレコードで既存FNV内容照合へ追加します。
凍結した旧値は互換比較専用で、欠落セルのfallbackには使いません。ファイルSHAやJSONの空白・キー順は使いません。


## 装備・通常セットの調整（#119・#121）

- 装備の攻撃力・魔力の固定値はLv1で100%、以後+5%/レベル、Lv40以上で295%、合計上限250。
  その他の固定能力は従来の+3%/レベル（Lv40以上217%）、%能力はレベル成長なし。
- 通常セットの3/6部位の純ダメージ係数を旧値の1.25倍にし、整数は四捨五入。
  助走の2部位AttackFlat・結晶の2部位PowerFlatは10から15。
  個別cap・A枠+120%／B枠×1.8の計画値・ボス効果・確率・CD・スロウ・対象数・範囲・持続・付与数は据え置き。
  Finale（終曲）は奥義の残りCD短縮のため、提案書の対象一覧から除外して旧値を維持する。
- `gear.json`・`sets.json` を変更後、`python tools/balance/gen_cs.py` で生成する。
  全入力を検証・renderしてから既存のpublish経路で書き込み、`--check` は書き込まない。
  ゲーム内では生成済み定義だけを使用し、JSON読込・Python・追加の起動検査を要求しない。
- 保存済み特性・固有効果は再抽選しない。基礎能力とセット効果は再計算されるため旧装備にも反映する。
  新規抽選とホストの許容値は同じ能力値別成長式を使用する。協力プレイは全員同じ内容定義が必要。

**EN:** Equipment flat Attack/Power grows by 5% per item level (100% at Lv1, 295% from Lv40),
capped at 250 in total. Other flat stats retain 3% growth; percentage stats do not grow with level.
Normal 3/6-piece pure-damage coefficients are multiplied by 1.25, rounded half up; Run-Up/Crystal
2-piece flat bonuses become 15. Caps, A/B plans, boss effects and non-damage parameters are unchanged.
Finale is cooldown reduction, not pure damage, so its existing values are retained.
Saved affix/power rolls remain intact; implicits and set bonuses are recalculated. Save format 5 and
Protocol 23 remain unchanged; co-op content matching includes the generated gear/set balance records.

## 記憶ダメージの調整

調整するのは `stars.json` の1セルです。旅人別の例：

```json
"byHero": {
  "Hero_Yubar": { "MemoryDamage": 1.10 }
}
```

- `effects` のキーは `<星ID>/value`、Choice子は `<星ID>/options/0/value` または
  `options/1/value`、旧ルートの記憶成分は `<旧星ID>/link/value`。
- 有効値は `base × byKind.MemoryDamage × byHero[Hero_*].MemoryDamage`。
  種類・旅人倍率の省略は1。全体調整は `byKind`、旅人だけの調整は `byHero`。
- 共有外縁（`outer.json`）は `byKind` だけを使います。旅人倍率は同旅人の
  固有星・旧ルートへ適用し、同IDの移行行がある場合は移行後の効果だけを採用します。
- `1.10` は相対10%増。4%は4.4%になります。10ポイントを足す指定ではありません。
  段数・費用・ID・前提・接続や、CapGに併存する仕掛けは変えません。
- manifestのMemoryDamage欄は `value` を持たず、`valueRef` で表を参照します。
  原本の併存、参照漏れ、余分な表キー、未知の種類・旅人を生成時に拒否します。
- PythonはDecimalで計算し、MemoryDamageを既存 `LinkDef.Value` → `ValueMilli` へ通します。
  倍率の積が1以外なら適用後の値を0.001刻みへ最近接・偶数丸めします。倍率の積が1なら丸めません。
  有効値は正、ValueMilliはInt32の範囲内。表現不能な結果は該当pathを示して失敗し、
  切捨て・上限拡大はしません。MemoryHaste、Stat、Power、旧仕掛けのAmountは整数のままです。
- `run` / `gen_cs.py --check` は鍛冶・装備・通常セット・星定数・全9星図・登録ファイルをまとめて扱います。
  全入力の検証・renderが成功してから変更のある生成物だけを書きます。
  `python tools/star-manifest/validate.py` と単体の星生成も同じ解決済み値を検証します。

## その他の星の調整（Issue #149 段階2）

`effects` に移したmanifestの独立数値欄は次のとおりです。Choice子もそのkindに含め、
shared外縁を旅人数倍には数えません。旧factory・汎用星・sample・保持する橋の数値は別に638欄です。

| 種類 | 独立数値欄 | 対象 |
| --- | ---: | --- |
| MemoryHaste | 262 | 記憶の加速量（整数） |
| GimmickBoost | 3,367 | 仕掛けの量の増加率 |
| GimmickParam | 813 | 時間・半径の増加率、確率のポイント、追加対象数 |
| Notable | 1,246 | Power、仕掛けの量・CD・rank表、回数、IdentityStrikeの距離・時間・対象数 |
| Keystone | 99 | typed変換のpct/from/to/delta/max、Grant、保持するPower |
| Stat | 36 | stat.perRank（整数） |

例えば `multipliers.byHero.Hero_Yubar.GimmickBoost` を `1.10` にすると、
固有の増加量2%は2.2%になります。共有外縁は変わりません。
全体倍率は `multipliers.byKind.<kind>`。基準値そのものは `effects` の該当1セルを変更します。
有効値は各効果の `base × byKind[kind] × byHero[owner][kind]`、Choice子は子のkindで1回だけ解決します。

- MemoryHaste／Boost／Paramのmanifest欄はMemoryDamage同様の `valueRef`。
  nested欄は `{"valueRef":"<星ID>/gimmick/value"}`、Choiceなら `options/<番号>/` を挟みます。
  Power／Statは `power/perRank`／`stat/perRank`、刻印は `keystone/upsideSpec/<番号>/<欄>`。
- 段ごとの仕掛け量があるとき `gimmick.value` は `valuesByRank/0` を参照し、原本を重複させません。
  nativeの生贄の盾／静水の数値は `legacy/<刻印ID>/native/<欄>` に集約し、Grantからも同じ原本を参照します。
  旧手書き定義は `legacy/` キーの生成済み型付き定数を使います。
- 同IDの移行前効果は倍率・指紋から除外します。移行後も保持する旧Powerは採用側として1回だけ適用します。
  未使用だったNotable／Statのtop-level `value` コピーはnullにし、typed payloadだけを原本にします。
- 整数の個数・回数・Power・Stat・旧Amountは整数のまま。小数倍率で端数が出れば該当pathを示して生成失敗します。
  scoped率・確率とtyped機構の量／rank表は0.01刻み、通常GimmickDefの量は0.0000001刻み、
  刻印の連続Set/Addは既存decimal、既存の秒／距離はfloat。新しい種類では黙って丸めません。
  異なる単位を持つNotable／Keystoneの倍率は複数欄に作用するため、個数まで変更したくない場合は個別セルを使います。
- enum／受け手条件のarg、ID・費用・段数・接続、無量のflag／0 sentinelは倍率対象外です。
  鍛錬／進行、共通の橋受付時間・盾の既定値・Powerの実行規則や安全上限は別ドメインで、この段階では移しません。
- 内容照合は採用済み有効値の正準レコードと既存FNV方式を使います。表の空白・キー順・倍率の書き方は指紋に入りません。
  初期切替は従来の内容指紋を維持し、有効値が同じになる基準値／倍率の相殺も同内容として扱います。


### #117/#120 の位階と個別下限

`rankScaling` は `damageGain: 1.5`、`pointDenominator: 500`、`maxPoints: 504`。
`StarRankBalance.g.cs` に点あたり0.003、最大倍率2.512を生成し、Coreが消費します。
星由来のダメージ量・増幅量だけが `r(p)=1+0.003p` 倍されます。
能力値、回復、加速、CD、確率、対象数、継続時間は対象外です。

固有星の各MemoryDamage効果は、位階適用**後**の効率2.5%/点を保守的に満たすよう、
親星のRankCostを `c` として基礎値 `ceil(2.5*c/(1+0.003*c), 0.001)` を採用します。
既存の有効基礎値で既に満たす効果は据置。1点星の必要基礎値は2.493%、3点星は7.434%です。
取得には少なくとも `p>=c` の支払いが必要なため、接続専用星を数えなくても下限を保証できます。
Choice子にも親星の費用を使い、実ツリーが採用した旧ルートの記憶成分も同じ条件で確認します。
表の数値は基礎値であり、500点の値を基礎値として再度位階倍するものではありません。

### #120 F2 の遠征の鍛錬

manifestの `growth.threshold/cap/effects/*/amount/capBonus/effectPct` は正準 `valueRef` だけを持ち、
数値は `run-growth.json.effects` の `<星ID>/growth/<欄>`（Choice子は `options/<番号>/growth/<欄>`）。
解決済みの数値を既存schema検証・星生成へ渡すので、日本語／英語の効果説明も生成値を使います。
RunGrowthのtrigger・stat・target・doubleGainはmanifestの既存形式を維持します。

F2はVesper閾値11%HP、Cetus閾値9%HP、Mist入口cap60、
空殻入口cap45・攻撃力+1.38/stack・上限星各+12。入口gain1と速度Choiceのgain2は据置です。
空殻の上限星2つの素の最大は `(45+12+12)*1.38=95.22` で旧値 `138*0.69` と同じ。
既定のBalanceSim行動では入口のみ・深度0は4種Z4、深度1は4種Z3に揃える案です。
全プレイヤー・全深度の到達保証ではなく、守り重視など行動仮定を変えると結果は変わります。
鍛錬は遠征ごとにリセットされるため、更新は帰還後・次遠征から適用してください。
旧スタックを新定義へ復元する遠征中更新は安全な移行とは扱いません。

ゲーム起動時にJSON・Python・ファイルハッシュは使いません。値は生成済み定数／星定義で、
戦闘中の文字列キー検索はありません。位階はBuildの再集計時に計算します。
MOD起動時の登録失敗は警告し、その旅人の生成星図・移行規則だけを外します。
開発ツール側は失敗を非ゼロで伝えます。

## 軽量な記憶効率

`star-efficiency` は実登録 `HeroSigils.TreeFor` の有効MemoryDamage Linkを列挙します。
記憶別の効果量は `Value × MaxRank`、費用はその効果星の `RankCost × MaxRank` の合計、
効率は `合計ダメージ% / 合計点`。接続専用の別効果星の費用は含めません。
CapGは星全体の費用を使い、分子には記憶ダメージ成分だけを入れます。

Choiceは全A（option 0）／全B（option 1）の2構成を別集計し、同記憶の2択も両方を足しません。
各記憶行はその構成の投影であり、異なる記憶行を合計した購入ビルドではありません。
混合Choiceの全探索や接続込みの実戦効率は測定しません。
`private` / `shared` / `total` を分け、旅人×構成×由来ごとの記憶効率min/maxも出します。
費用0の効率は `null`／`—`。このmin/maxは表示構成内の記憶間比較で、選択最適化の上下限ではありません。

従来の `damagePercent` / `percentPerPoint` / `minPercentPerPoint` / `maxPercentPerPoint` は
基礎値のまま維持し、同じmodelVersion・conditionsで前後比較できます。
位階を考慮する別指標は `minimumRank*`（効果ごとに親RankCost点）と
`at500Points*`（すべて同じ500点）。JSONの各contributionとentryにも両者を保存します。
位階投影metadataは比較条件ではなく、既存基準にない指標は「追加/なし」と表示します。
最小購入点は個々の効果の独立した下限、500点は固定条件の投影であり、全星を同時購入したBuildではありません。
各段の各効果をCoreの `StarDamageScaling.ScaleMilli` で0.001%単位へ丸めてから合計します。
同じ記憶の全星をまとめて位階倍するBuildの集計とは丸め順序が異なる独立投影です。

## 種類別・意味単位別の星値

`star-values` は実登録 `HeroSigils.TreeFor` の6種類を、旅人・all-A/all-B・private/shared・星ID・
意味・欄・単位ごとに列挙します。秒・距離・個数・率・Power・Statをまとめて足しません。
段ごとの表はrank別の行、保持するPowerは1回、選択された `Mechanism.Replaces` は旧機構成分だけを除外します。
費用／MaxRankはメタデータで、値は採用された定義量です。位階・GB合成・発動頻度・DPS・成長は模型化しません。

```sh
DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet tools/BalanceSim/bin/Release/net8.0/BalanceSim.dll \
  --mode star-values --out /tmp/star-values.md --metrics-json /tmp/star-values.json
```

比較表の列は既存どおり現在／前回成功／差／相対差。星の追加・削除・未観測を0に置き換えません。
例えば同条件でYubarのBoostを1.10倍にした場合、2%→2.2%の差は0.2 pp、相対差は10%です。

## 出力と前回成功基準

既定の出力先は `tools/balance/results/`（gitignore対象）。成功した実行ごとに
日時付きディレクトリへ次を保存します。

- `forge.md` / `forge.json`: 鍛冶・覚醒・合成・分解・現行工房・保証強化の意味別実測値。
- `star-efficiency.md` / `star-efficiency.json`: 旅人×記憶×全A/全B×由来の効果量・費用・%/点と旅人別min/max。
- `star-values.md` / `star-values.json`: 6種類の旅人×構成×由来×星×意味×欄×単位別の採用済み値。
- `expeditions.md` / `expeditions.json`: 既存Simulationの遠征・節目・容量計測。
- `current.json`: 4モードのスナップショット、検証状態、日時、生成入力の `tableMetadata.forge`（schemaVersion 2と全鍛冶表）／`tableMetadata.stars`（schemaVersionと倍率・位階係数・鍛錬数値）／`tableMetadata.gear`・`tableMetadata.sets`（装備・通常セットの原本）、既存Gitコミットの `gitRevision`（取得不能時null、比較表示はunknown）。
- `comparison.md`: 現在／前回成功／差／相対差。

検証済み基準は `last-success.json`。`--no-tests` は独立した
`last-report-only.json` を参照・更新し、検証済み基準を置換しません。
それぞれ初回は「前回なし」、その成功結果を次回の基準とします。
全必須ステップ成功後に一時ファイルを置換して基準を原子的に更新します。
生成、ビルド、テスト、いずれかの計測、レポート書込に失敗すれば以前の基準を維持します。
`--out-dir` を変えれば基準も独立します。

JSONの `modelVersion` と正規化 `conditions` が異なるモードは比較不可として現在値だけを表示します。
ContentFingerprintの変更は調整による差を比較する目的なので、比較不可の理由にしません。
`tableMetadata` は比較互換条件に含めず、調整前後の定義を追跡するために表示します。
`conditions` には実測した.NETランタイム（framework/version/OS/processArchitecture）と、
`RegisterAllGenerated` 後の実登録ツリーから取得・重複排除・ソートした `registeredHeroes` を含めます。
登録候補カタログではなく `HeroSigils.All` の旅人を `TryGetRegisteredTree` で確認します。
ランタイム／登録集合の違いは比較不可です。Git revisionは由来の記録で、互換条件にはしません。
Git取得は開発ツールのみで行い、ゲーム起動の依存を追加しません。
指標は安定IDの和集合で照合し、消えた行は現在「削除」、増えた行は前回「追加/なし」、
いずれも架空の0や差分を表示しません。
相対差は `(現在−前回)/前回 ×100%`。前回0の場合は `—`、未到達の節目は `null`／「未到達」で、
架空の0遠征として扱いません。同条件・同係数で2回成功すれば測定値の差は0です。
失敗率・記憶ダメージ (%) の絶対差は percentage points (`pp`)、効率の差は `%/点`、
相対差は `%` です。未到達割合は0〜1のfractionで、差もfractionのまま表示します。
小数は `Decimal` で読み込み・差分計算し、成功基準にもJSONの数値として保存します。

## 実測JSONの契約

BalanceSim単体でも利用できます（先にビルドしてください）。

```sh
dotnet tools/BalanceSim/bin/Release/net8.0/BalanceSim.dll --mode forge --metrics-json /tmp/forge.json
dotnet tools/BalanceSim/bin/Release/net8.0/BalanceSim.dll --mode star-efficiency --metrics-json /tmp/star-efficiency.json
dotnet tools/BalanceSim/bin/Release/net8.0/BalanceSim.dll --mode expeditions --runs 2 --players 4 --metrics-json /tmp/expeditions.json
```

JSONは `modelVersion`（forgeは2、その他は既存版）、`mode`、`contentFingerprint`、`conditions`、比較用 `metrics` を持ちます。
`metrics` は安定した `id`、表示用 `label`、未丸めの `value`、`status` を持ちます。
System.Text.Jsonで直接数値を書き出し、Markdownの丸め値や文言を逆解析しません。

記憶効率の `metrics` はdecimalの値と `unit`（`percent` / `points` / `percent/point` / `nodes`）を持ち、
費用0の効率は `status: "no-damage-nodes"`。単位が異なる行の差は作りません。
`entries` に効果星ごとの `contributions`（星ID・選択肢番号・段あたり値・段数・費用）、
`configurations` にChoiceの選択、`choiceOptions` に両選択肢、`ranges` にmin/maxと該当記憶を保存します。

鍛冶の `forge` 配列は `id`、`label`、decimalの `value`、`unit`、`status`、条件軸の `axes`。
`metrics` は同じ値・単位を比較用に投影します。`status` は `measured` / `cap` / `inapplicable`、
上限・対象外の操作値は `null`。単位は `percent` / `ratio` / `levels` / `count` / `shards` /
`tuning` / `relics` / `points` / `rooms` で、同じ単位の値だけ比較します。
`conditions` に代表再抽選回数と工房／出来事の前提も記録します。
旧forge modelVersion 1とは比較不可で、最初の新版実行は現在値だけを表示します。
遠征は `samples` のプレイヤー順生値、`milestones.reachedAt`（未到達はnull）、`capacity.values` と、
その平均／線形補間の中央値・P10・P90等を出力します。Coreの鍛冶式を複製しません。
`--metrics-json` は forge / star-efficiency / star-values / expeditions に対応し、他モードは重い計測前に引数エラーになります。
JSON出力を指定しない既存モードの挙動は維持します（setsの既存問題はstage0対象外）。

旧初期表の星図登録後ContentFingerprint `10575-81dd841870386d98` は、元の数値内容を表す互換情報です。
初期の採用済み記憶成分の既存FNV-1a内容identityは凍結した互換情報で、編集可能な既定値ではありません。
有効数値を変更すると正準key/milliレコードがContentFingerprintへ追加されます。
相殺された基準値・倍率が同じ有効内容を作る場合や、移行行で置換された旧成分だけを変える場合は指紋を変えません。
位階のgain・分母・最大点と鍛錬の解決済み数値も正準レコードを生成し、
既存のContentFingerprint通信照合に含めます。別のファイルハッシュ検証は追加しません。
既存の鍛冶期待値テストは原本 `forge.json` から独立計算する期待値へ移行し、実際の失敗率・固定seedでの成功失敗結果を検証します。
これは安全性、形式、契約の検証を緩めたり、任意の係数で全テストが常に成功すると保証したりするものではありません。
本レポートは実際のCore呼出と固定条件のシミュレーションであり、実ゲームの勝率や経済の保証ではありません。
