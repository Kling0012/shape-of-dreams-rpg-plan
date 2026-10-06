# バランス外部定義・比較（Issue #149、#117・#119・#120・#121）

`forge.json` の鍛冶・覚醒・合成・分解・現行工房・イベント保証強化、`stars.json` の7種類の星の基準値・倍率・星ダメージの位階係数、
`run-growth.json` の鍛錬の閾値・上限・1スタック量・上限増分・効果増分、
`star-progression.json` の星XP曲線・費用・報酬・刻印枠の解放、
`gear.json` の装備レベル成長・固定攻魔上限、`sets.json` の通常セット2/3/6部位効果、
`pressure.json` の夢の圧・深度、`monsters.json` の悪夢・変種・敵行動、
`infinity.json` の供給予算・周期・圧段階上限、`powers.json` のPowerの時間・距離・条件（127欄）を
型付きC#へ生成します。記憶ダメージはmanifestの480親＋258選択肢と旧ルート98成分、計836欄を移行済みです。
星ID・段数・費用・選択肢・保存形式5・Protocol 23は維持し、調整した有効値（装備・セット・Powerを含む）を内容照合に含めます。
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
Infinityの常用比較は `--infinity-scope comparison`：30分×通常深度0/5・同部屋数の通常比較・4構成の7行、
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

Issue #234では一般供給だけを調整します。`rooms.lesser/normal/miniBoss.increment` を通常の敵構成へ合わせ、
`rates.relicsPerHour/shardsPerHour/tuningPerHour/xpPerHour/starXpPerHour` を増やしました。
`killMix`・`referenceSeconds`・希少品／保証・部屋cap・ボス周期・保存形式・Protocolは維持します。
時間／部屋・希少品の認可が不足しても、抽選前にRare以下へ限定し、一般供給は独立した時間予算で制限します。
道標の置換抽選もレア度上限を越えません。星XPの補充には通常と同じ `dreamDepth.starXpPerDepth` を適用し、
`bursts.starXp` だけは深度5のボス報酬を収めるため20から40へ増やします（旧保存のcreditはそのまま受理）。
既存の通貨星ボーナスは通常モードの台帳・贈り物除外・敵報酬除外をそのまま使います。
Markdownの部屋／ボス平均は発生源別で、供給全体をボス数で割った値ではありません。

実行比較：各行500プロフィール、seed=234、装備Lv1、深度0・道標なし・通常速度、
戦闘1時間。`normal-matched` は通常の報酬規則でInfinityと同じ38戦闘部屋＋3ボスへ揃えた模型です。
修正前後のInfinityは部屋・ボス・撃破数・戦闘時間が一致し、通常モードの既存行の出力は変わりません。
通常同数の撃破は合計349,331、修正前後はともに349,347（500人分）です。
表の遺物はMOD装備の発見数で、鞄あふれで欠片になった物も含みます。

| 単位・規則 | 遺物 | 欠片 | 調律石 | 夢XP | 星XP |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1戦闘部屋・通常同数 | 0.411 | 3.988 | 0.049 | 29.340 | 19.271 |
| 1戦闘部屋・修正前 | 0.340 | 2.712 | 0.057 | 16.277 | 9.641 |
| 1戦闘部屋・修正後 | 0.638 | 6.227 | 0.129 | 33.976 | 19.897 |
| 1ボス・通常同数 | 1.651 | 26.255 | 1.024 | 52.347 | 20.000 |
| 1ボス・修正前 | 0.000 | 0.579 | 0.022 | 1.157 | 0.000 |
| 1ボス・修正後 | 1.519 | 22.097 | 0.977 | 49.641 | 20.000 |
| 1時間・通常同数 | 20.578 | 234.102 | 4.952 | 1341.288 | 854.310 |
| 1時間・修正前 | 12.968 | 106.788 | 2.294 | 627.976 | 368.312 |
| 1時間・修正後 | 28.802 | 303.546 | 7.882 | 1442.700 | 818.150 |
| 1時間・通常標準（35部屋＋6ボス） | 24.224 | 307.050 | 7.942 | 1581.038 | 994.370 |
| 1撃破換算・通常同数 | 0.029453 | 0.335072 | 0.007088 | 1.919795 | 1.222780 |
| 1撃破換算・修正前 | 0.018560 | 0.152839 | 0.003283 | 0.898785 | 0.527143 |
| 1撃破換算・修正後 | 0.041223 | 0.434448 | 0.011281 | 2.064852 | 1.170970 |

部屋／ボス行は発生源別（部屋は撃破＋突破、ボスは撃破のみ）で、その後の確保／勝利／潜行は除きます。
1撃破換算は全供給÷全撃破数であり、個々の敵のドロップ率ではありません。
補充前の通常抽選は `Loot.cs:403-443`：Lesser／Normal／MiniBoss／Bossについて
装備期待数0.008／0.027／0.45／1.6、欠片期待数0.05／0.18／7.5／25、夢XP1／2／12／50、
星XP1／1／5／20（Heat・深度・悪夢・道標・依頼の加算を除く）。
修正前は認可拒否時にこれらがまとめて0になり、修正後は希少品だけを除外して一般供給の時間上限を適用します。

4倍速・深度0の1時間は遺物29・欠片357.758・調律石9・夢XP1793・星XP1195。
深度5・悪夢昇格の同時間は遺物29・欠片357.834・調律石9・夢XP1793・星XP2391です。
抽選Epic+／Legendaryの認可上限は修正前後とも0.586805／0.052875期待個数/戦闘時、
保証は0.25枠/戦闘時。47シナリオの実予約最大値は0.586387／0.047579で、上限を超えていません。
通常速度の星XPは通常標準より少ないままですが、同部屋数比較との差は約4%です。
保証・周期10/15/20・30/60/120分・宝庫・貯めた予算のボス抽選・合法帰還とcodecを実行しています。

本体報酬の確認（`sod-gamedata/decompiled/sod-decomp` の読み取りのみ）：
`Dew.Core/RoomRewards.cs:86-180` の新規Combat報酬は共通で、既定の通常報酬flowでは初回4部屋に記憶2、
以後は通常の4部屋poolで記憶1＋エッセンス1（Luck入りpoolでは5部屋）です。再生成でこのpoolを初期化しません。
`Dew.Core/Shrine_BossSoul.cs:147-187` の生存者別ゴールド／ダスト・限定記憶／エッセンス、
本体の通貨箱・ショップstockを削除するInfinity処理は見つかりませんでした。
ただし `InfinityMode.cs:495-497` の `noAdvance` は本体zoneIndexを進めず、
未訪問ショップ等は再生成で訪問機会を失い、既存の脇道／招待は無効です。これらの仕様は変更しません。
本体初期式の撃破ゴールド係数 `(1+0.4z)(1+0.2z)` はzoneIndex0/1/2/3で1／1.68／2.52／3.52ですが、
実アセットで上書きされ得ます。金額・箱／ショップ頻度・記憶／エッセンスの品質表は提供データにありません。
ゴールド／ダストのMOD星ボーナスは、0から通常の `CurrencyStars` の式へ戻します（星の数値・上限は変更なし）。
実機・マルチの受け渡し・壁時計1時間の取得量・実UIは未測定で、上の表は実Coreを通った戦闘時間模型です。

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


## ボス挙動表の調整（Issue #149 段階8 前半）

数値原本は `tools/balance/bosses/<boss>.json` の13ファイル（demon・skoll・infernus・ink・nyx・erebos・seeker・
azurak・primus・light・maw・obliviax・polaris）。ink.json は白夜／黒月の両セットを `white_*`／`dark_*` キーで持ちます。
1セルを編集して `tools/balance/run` を実行すると、型付き生成・全通常テスト・実測・前回成功との比較まで進みます。
生成先は `src/SodRpg.Core/Game/Balance/Boss<Boss>Balance.Generated.cs` 13本と `BossBalanceValues.Generated.cs`。
`BossProfiles.*.cs` は構造（ID・enum・説明文・報酬の段構成）と生成済み定数の参照だけを持ち、実行時にJSONを読みません。

| 表のセクション | 内容 |
| --- | --- |
| `shared` | ボス固有の共有定数（Demonの樹芽4定数等）と技ヘルパー埋め込み値（Skoll氷矢の持続・射程等） |
| `moves/<部位や段>/<profile>` | チャネルの `value`／`cap`（表示%、生成時に×1000でValueMilli/CapMilli）と、
  各アクションの `cooldownMillis`・`radiusMilli`・`count` 等のpayload欄 |
| `rewards` | 固有報酬のアクションpayload。Inkの白／黒・Azurakの段別条件は段ごとのセル |

- チャネルの `cap` 省略は `cap == value`（InkChannel／AzurakChannel形式）。`value` は正、`cap >= value`。
  アクション欄はBossActionコンストラクタの受理範囲（`count` 1〜64、`maxInstances` 1〜4、`angleMilli` 0〜360000 等）を
  生成時に検証し、不正な表はrender前に拒否します。
- `order`・段番号・`BossSetStage(2/3/6)`・セットの部位数、ID・enum・説明文（Txt）・イベント順序・契約文字列は
  構造なので表へ移しません。Power定数・Mod側native adapterの数値・説明文内の数値は段階8の後半で別に扱います。
- ボス値は既存の指紋レコード（`boss-schema`・`boss-channel`・`boss-action`・`boss-reward-action`）で全面的に照合されるため、
  表変更時に新しい指紋レコードは追加しません。有効値が変われば既存の内容照合が相手側で不一致を検出します。
- 無調整移行の回帰は `BossBalanceMigrationTests` が担当します。全チャネル・アクション欄と代表報酬値の表↔実定義一致に加え、
  表が移行時のまま（`BossBalanceValues.MatchesFrozenReference`）である限り、ボス指紋レコードの要約
  `591-685158953d5197ef` と全体指紋 `10579-ab35d865a569c87b` が移行前と一致することを確認します。
  1セルでも調整した表はこの固定照合を離れ、表↔実定義の一致検証だけが常時有効です。

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

## 段階6：ドロップ・経済・契約・日替わり・道標・出来事

数値の原本は次の7表です。名前・ID・enum・保存／通信の構造・関係フラグは従来のCore定義に残します。
同じ値でも異なる経済系は別キーです（商人のgold価格と旧shards価格、ArchiveとDreamOfferingのXPなど）。

| 表 | 調整対象 | 生成器 |
| --- | --- | --- |
| `loot.json` | rarity重み、tier別ドロップ／幸運、heat係数、追加遺物、選択重み、撃破素材、変種の追加欠片 | `loot_values.py` |
| `boss-sets.json` | ボスセットの通常／悪夢／深度ドロップと上限 | `boss_sets_values.py` |
| `economy.json` | dust⇔shards換算、商人gold、分解dust、Limbo補正、確保時の潜行ボーナス除数 | `economy_values.py` |
| `pacts.json` | 契約ごとの呪い強度・報酬・能力値、提示数、潜行ボーナス倍率 | `pact_values.py` |
| `daily-dream.json` | 日替わり効果の倍率・報酬、固有効果強化率 | `daily_dream_values.py` |
| `waypoints.json` | 道標ごとの量・倍率・提示数、遺物複製／分解・星XP・覚醒・調律石・宝庫の換算 | `waypoint_values.py` |
| `events.json` | 出来事の出現率／抽選重み、価格・確率・個数・報酬・heat増分・XP | `events_values.py` |

各表は `schemaVersion: 1` の閉じたschemaです。整数の個数／費用とdoubleの確率／倍率を区別し、
未知の欄・boolを数値として使った入力・非有限値・doubleに往復できない小数精度を生成前に拒否します。
生成先は `src/SodRpg.Core/Game/Balance/{Loot,BossSets,Economy,Pacts,DailyDream,Waypoints,Events}.Generated.cs`。
保証強化は段階3の `forge.json` に残し、出来事表へ重複させません。
Mastery、enum番号、関係フラグ、取引の待ち時間／照会回数、保存／通信／保留容量は対象外です。
`MaxDustEarnPerTrade`・`MaxBatchesPerTrade` 等の安全上限も広げません。
分解dustの最大額は採用済みforge表から検証し、既存の取引上限を超える調整は生成段階で拒否します。

### 操作

該当する表の1セルを変更し、従来どおり次の入口を使います（任意cwdから実行可能）。

```sh
DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor tools/balance/run
```

段階6時点のrunは従来の6モード（forge、star-efficiency、star-values、star-progression、v132stars、expeditions）に
次の3モードを加えた9スナップショットを保存しました。段階5・7・8のモード追加分は各節参照、
上の旧段階の「4モード」等の記述は当時の範囲です。

| 新モード | 比較する量 |
| --- | --- |
| `loot-economy` | heat／tier／rarity／floor別の正確な抽選分布、遺物・素材期待値、ボスセット率、gold／dust換算、Coreで実際に確保した欠片 |
| `pact-daily-waypoints` | 個々の契約／日替わり／道標の採用済み効果量、幸運の表示率、道標の各換算係数 |
| `events` | 出来事ごとの価格・確率・抽選重み・報酬と、heat／rarity／個数別の価格・XP・調律石 |

新モードも `<mode>.md`／`<mode>.json` と `current.json`／`comparison.md` に入り、
`tableMetadata` に7表の入力を記録します。各モードは別Coreプロセスです。
標準の初期表では新モードの比較行は378／1016／125行。IDは意味と条件軸で固定し、単位別に照合します。
`conditions` にruntime、実登録旅人、抽選floor方針、独立効果の方針、確保の模型入力（未確保欠片100、空の装備・依頼、Infinity無効）を保存します。
素材の値は補正前の期待値、商人goldは本体難易度補正前です。DPS・勝率・踏破速度・本体通貨取引の成功率は測りません。

レポートだけなら独立した未検証基準を使えます。検証済みの `last-success.json` は更新しません。

```sh
DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor tools/balance/run --no-tests --runs 1 --players 1
python tools/balance/gen_cs.py --check
DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet tools/BalanceSim/bin/Release/net8.0/BalanceSim.dll \
  --mode events --out /tmp/events.md --metrics-json /tmp/events.json
```

価格の実行・候補判定・日英の説明は同じ採用済み値を参照します。道標の幸運表示も `Loot.LuckPercent` 由来です。
ゲーム中のJSON読込・倍率再計算・文字列キー検索は追加しません。
共通生成器とrunの接続は `stage6_values.py`／`stage6_run.py` に分離し、既存の行は変更せず追記しています。
表／生成器の変更は `tools/test_changed.py` でも全スイート対象になります。

### 初期値の互換性と検証

移行前のCore実行から、数値定義・抽選結果／RNG状態・価格／換算・確保報酬を6652項目保存した
`tests/SodRpg.Core.Tests/Stage6OriginalValues.json` と照合する移行回帰を維持しています。
このfixtureは互換性の観測であり、調整原本でも実行時fallbackでもありません。
初期表だけを使った移行時の星図登録後ContentFingerprintは前後とも `10579-ab35d865a569c87b` でした。
この旧版固定値との全体指紋比較は、段階6が無調整でも他の表を調整すると成立しません。
#234のInfinity調整は内容照合へ正しく追加されるため、旧版指紋の固定値アサートは削除しました。
段階6の6652項目の実定義／RNG比較と、既存 `ContentFingerprintTests` の同一内容受理・異なる内容拒否の検証は維持します。
報酬表・生成物・段階6fixtureの値は、この回帰修正では変更しません。
値変更後は意味・型・単位・採用値の正準レコードを既存FNV-1a照合へ加えます。
ボスセット率は既存 `boss-drop` レコードを使い、二重に加えません。JSONの空白／キー順は指紋へ影響しません。
ファイルのSHA256検証は行いません。保存形式・Protocolは変更していません。
既存の数値期待値は生の表と独立した式／RNG分岐を使い、正当な調整で初期fixtureを再固定する必要はありません。

```sh
DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet build SodRpg.sln -c Release
DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/test_changed.py --all
python -m unittest discover -s tools/tests -p 'test_*.py'
```

実機Unityの描画はこのCore／CLI検証の対象外です。

### 比較の実行例（段階6の移行確認）

同じ出力先・`--no-tests --runs 1 --players 1` で初期表を保存した後、
`events.json` の `stoneBroker.shards` だけを35から36へ一時変更してrunを実行しました。

| 指標 | 単位 | 現在 | 前回 | 差 | 相対差（丸め） |
| --- | --- | ---: | ---: | ---: | ---: |
| `events/stoneBroker/shards` | shards | 36 | 35 | +1 | 約+2.857% |

新3モードの条件・指標ID・単位は同じで、1519行中の差分はこの1行だけでした。
実際のCoreでも35欠片では候補を使えず、100欠片から36を支払い、64欠片と調律石2を得ました。
日英の候補表示も36を参照しました。移行コミットの表は元の35です。
不正な `offerChance: 1.1` は `events.offerChance: expected probability in 0..1` で拒否され、
生成物も前回の基準も変わりませんでした。レポート専用の実行は検証済み基準を作りません。
## 装備の種類別原本（Issue #149 段階7）

この節が装備の最新の操作手順です。上の過去段階の記載にある `sets.json` は
`equipment/sets.json` へ移動済みです。`gear.json` は装備レベル成長だけを担当し、
固定攻撃力・魔力の上限は `equipment/caps.json` に集約しました。

| 原本（`tools/balance/` 以下） | 対象 | 編集する数値 |
| --- | --- | --- |
| `equipment/bases.json` | 土台600種 | IDごとの `implicitValue` |
| `equipment/uniques.json` | 固有品1,418定義（参照だけの部品も収録） | `powers[].value` 2,092欄、`link.value` 357欄 |
| `equipment/sets.json` | 通常セット48組 | `twoPiece` / `threePiece` / `sixPiece` の307欄 |
| `equipment/affixes.json` | 6枠・98特性 | `pools[枠][Stat]` の `min` / `max` / `weight` |
| `equipment/power-pools.json` | 6枠・207固有効果候補 | `pools[枠][Power]` の `min` / `max` |
| `equipment/caps.json` | 固有効果95・能力値26の上限 | `power[Power]` / `stat[Stat]` |

名前・lore・土台／セット／効果の関係は既存の定義を維持します。通常セットの
既存表にある名前も据え置きです。ボス14セットの技・報酬payloadは段階8の対象で、
2/3/6部位の技参照や報酬段階番号は調整数値にしません。
保存形式・Protocol・既存の機能ゲートは変更しません。

表の1セルを編集し、次の1コマンドで生成・全通常テスト・実測・前回成功比較を行います。

```sh
DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/balance/run

# 生成だけ／書込なしの生成鮮度確認
python tools/balance/gen_cs.py
python tools/balance/gen_cs.py --check

# 装備定義／既存セット予算の個別実測（先に生成・Releaseビルドを行う）
DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet tools/BalanceSim/bin/Release/net8.0/BalanceSim.dll --mode equipment --metrics-json /tmp/equipment.json
DOTNET_ROLL_FORWARD=LatestMajor /usr/bin/dotnet tools/BalanceSim/bin/Release/net8.0/BalanceSim.dll --mode sets --metrics-json /tmp/sets.json
```

従来の6モードに `equipment` と `sets` を追加し、それぞれ別プロセスで取得します。
`equipment` は実際のCore定義の値・型・単位を列挙し、相対重みを確率と混同しません。
土台・固有品・通常セット・抽選範囲・上限に加えて、PowerPoolsから派生する銘品も比較します。
通貨上限は戦闘能力の上限と別のID・単位です。値を持たないセット部品・ボス参照は
`null` と参照状態を記録し、実測0に置き換えません。型または単位が異なる行の差は出しません。

`sets` は既存 `SetBalance` の48組・同枠代替品とのPowerScore比較を実行します。
2/3/6点の予算、代替との差、6点ボーナス自体の利得・占有率、中央値と既存許容帯判定を
未丸めのJSONで保存します。アイテムレベル10、強化／覚醒なし、各枠Epic／Legendary各64抽選、
既存の固定seedを条件に記録します。通常連携・ボス技・戦闘DPS・勝率は模型に含めません。

銘品の `{power, band}` は実数値の原本ではありません。名前付き遺物は土台の枠の範囲から、
旧来の別枠候補を持つものは明示的な `rangeSlot` から値を生成します。小セットの3点効果も
明示的な `rangeSlot` を使います。low=1/6、mid=2/5、high=13/20の帯をDecimalで
四捨五入（half-up）し、範囲内に収める従来値を維持します。
1枠だけの範囲変更でも、他枠の一致を必須条件にせず、その参照元の派生品だけを再生成します。
`NamedItems.Data.cs` と240土台の `new-bases.json` 参照メタデータも共通生成の出力に含め、
全入力の検証・render成功後に一括publishします。

低レア度ツールの旧240土台の数値コピー、C#抽選表を読む経路、set5の旧数値提案は廃止しました。
`tools/import_v129_content.py` は過去MarkdownのID対応を監査するだけで、数値を書き戻しません。
旧 `--write` は廃止しました。新土台のメタデータは `valueRef` で原本を参照します。
ゲーム実行時には生成済みint／decimal定数・既存型付き配列だけを使用し、JSONを読みません。

新しく移した数値の初期有効内容は、既存FNV-1aの意味付きレコードidentityを互換参照として
凍結しています。初期値は追加の内容照合レコードを出さず、移行前のContentFingerprintを維持します。
採用済み値が変われば、安定ID・型・値のレコードを既存内容照合へ追加します。
通常セットは既存の指紋レコードをそのまま使います。互換参照は数値のフォールバックではなく、
JSONの空白／キー順・比較条件・ファイルハッシュは新しい内容照合に含めません。

移行前後の実行時定義を直接比較した例（無調整）：

| 指標 | 単位 | 移行前 | 移行後 | 差 | 相対差 |
| --- | --- | ---: | ---: | ---: | ---: |
| Weapon / Momentum 抽選上限 | percent | 5 | 5 | 0 pp | 0% |
| `named.armor.001` Momentum（派生値） | percent | 3 | 3 | 0 pp | 0% |

別の一時ビルドでWeapon / Momentumの `max` だけを5→6にした実動確認では、
Core抽選上限が6、上の派生銘品が4となり、内容指紋も変わることを確認しました。
この一時変更はチェックインした表・生成物には反映していません。

## Powerの時間・距離・条件（Issue #149 段階8・前半）

Power側の調整数値の原本は `tools/balance/powers.json` です。
1効果の「表・Core計算・Mod適用・説明表示」が同じ生成定数を参照します。
生成先は `src/SodRpg.Core/Game/Balance/Powers.Generated.cs`（`PowersBalance`）で、
`PowerRuntime` の公開定数は同クラスへのエイリアスとして維持します。

| セクション | 対象 | 例 |
| --- | --- | --- |
| `runtime` | `PowerRuntime.cs` の公開定数55＋障壁の最初の遅延 | `MomentumDuration`、`ChainChance`（double） |
| `newPowers` | `PowerRuntime.NewPowers.cs` の定数3と式内の調整値、新Power44種の適用時間・距離 | `RunUpDistance`、`TollOfGrudgeThreshold`（比率） |
| `host` | Mod側だけで適用する値 | `AegisShieldDuration`、`BulwarkRange` / `FrenzyRange` |
| `lastStarlight` | `HostAuthority.BossLastStarlight.cs` の適応delta | `LastStarlightDelayReductionMax`、`LastStarlightAttractionCap` |

- 単位はキーごとに固定です。`seconds`／`meters`／`count`／`percent`／`gold` は正、
  `ratio` は0より大きく1未満。しきい値は従来のfloat比率（0.3＝30%）のまま保存し、
  表示は `:0%` で整数パーセントへ出します。intとfloat/doubleの区別もキーごとに固定です。
- 同じ数字でも別の効果・別の意味のセルは統合しません（例: `BasicSplashRadius` は
  会心の飛沫と詠唱の薙ぎの共有適用半径、`ConditionalPowerCap` と `PrimedPercentCap` は別原本）。
  本体由来の属性スタック上限（光5・闇5）、Q/W/Eの3枠、報酬段階番号は調整対象外です。
- Core式内の値（45秒ゲート、3体→4体、60%蓄積など）は生成定数へ置き換え、
  Mod側の適用（半径・障壁時間・スロウ30%・2秒など）も同じ定数を使います。
  `lastStarlight` の段階番号（stage 1/2/3の境界）はBossProfiles側の報酬構造なので
  この表では扱いません。BossProfiles本体の数値は段階8ボス側の担当範囲です。
- 移行は無調整です。初期表は追加の指紋レコードを出さず、ContentFingerprintは
  移行前と同じ `10579-ab35d865a569c87b` を維持します（検証時点）。
  1セル変更すると採用値の正準レコードが `balance:powers:v1:` 接頭辞で内容照合へ加わります。
- 説明文（`Content.FormatPower` と `NewPowersV129.Describe` の日英両文）は生成定数を
  補間します。移行時に2言語×全Power×代表値の全文が移行前と一致することを確認しました。
  ゲーム実行時のJSON読込・Protocol・保存形式の変更はありません。

## 本体の追加枠上限と復帰時の整理（Issue #248）

`stars.json.essenceSlots` を原本として、`StarRankBalance.g.cs` に
星1つの追加量・場所ごとの上限・旅人ごとの合計上限を生成します。
星・鍵石／刻印・遺物・セットなど、Buildへ入るすべての出所を集計した後、
アイデンティティ記憶と移動の記憶のエッセンス枠をそれぞれ最大+1、合計最大+2へ丸めます。
通信から復元したBuildにも同じ制限を適用します。保存形式・Protocol・星IDは変更しません。
本体の祠などによる元の枠数はMODの追加量ではなく、取り除きません。

### 追加枠を持つ星の全一覧と最大値

各セルは `h.<旅人の英小文字>.route.<下記の名前>.slot` の星IDです。
すべて1段・追加量+1。アイデンティティは「記憶の器を広げる」、
移動は「回避の器を広げる」。鍵石／刻印・装備／セット・生成manifestの追加効果には、
現行定義で本体の枠数を増やすものはありません。記憶の装着枠・遺物の装備枠も増やしません。

| 旅人 | アイデンティティの星 | 移動の星 | 星の素の合計 | 修正前の実効上限 | 修正後の実効上限 |
| --- | --- | --- | ---: | ---: | ---: |
| Aurena | `claw`、`beautiful-threat` | `feathery-dash` | +3 | +2 | +2 |
| Bismuth | `prismatic-eyes` | `distorting-sprint` | +2 | +2 | +2 |
| Cetus | `icy-veins`、`charged` | `frost-charge` | +3 | +2 | +2 |
| Husk | `killing-flow`、`wind-scar` | `flash-step` | +3 | +2 | +2 |
| Lacerta | `double-tap`、`powder` | `nimble-dodge` | +3 | +2 | +2 |
| Mist | `en-garde`、`priorite` | `fast-feet` | +3 | +2 | +2 |
| Nachia | `pack-heart`、`circle-life` | `dreamy-waltz` | +3 | +2 | +2 |
| Vesper | `resolve`、`mercy` | `charge` | +3 | +2 | +2 |
| Yubar | `converging-stars`、`exotic-matter` | `flicker` | +3 | +2 | +2 |

素の合計は重複するアイデンティティの星も数えた理論値で、星点予算を超えて
星図全体を同時取得できるという意味ではありません。修正前も場所ごとの能力値上限と
`EssenceSlots.ClampAdded` が実効量を抑えており、設計値だけで実効追加量が+2を超える経路はありません。

### 出所別の修正前→修正後（全旅人）

エッセンス枠の**追加量**を比較します。「前」は修正前の定義・集計上限で、
報告された実機や既存セーブの異常値の最大値ではありません。
星の素の合計は上の一覧、下表の星は場所別上限を適用した実効上限です。
鍵石と刻印は同じ定義を二重加算せず、装備には遺物・セットを含みます。

| 旅人 | 星 | 鍵石 | 刻印 | 装備 | MOD合計 | 本体の正規追加枠 |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Aurena | +2→+2 | 0→0 | 0→0 | 0→0 | +2→+2 | N→N（下記） |
| Bismuth | +2→+2 | 0→0 | 0→0 | 0→0 | +2→+2 | N→N（下記） |
| Cetus | +2→+2 | 0→0 | 0→0 | 0→0 | +2→+2 | N→N（下記） |
| Husk（空殻） | +2→+2 | 0→0 | 0→0 | 0→0 | +2→+2 | N→N（下記） |
| Lacerta | +2→+2 | 0→0 | 0→0 | 0→0 | +2→+2 | N→N（下記） |
| Mist | +2→+2 | 0→0 | 0→0 | 0→0 | +2→+2 | N→N（下記） |
| Nachia | +2→+2 | 0→0 | 0→0 | 0→0 | +2→+2 | N→N（下記） |
| Vesper | +2→+2 | 0→0 | 0→0 | 0→0 | +2→+2 | N→N（下記） |
| Yubar | +2→+2 | 0→0 | 0→0 | 0→0 | +2→+2 | N→N（下記） |

本体側は9旅人共通の `HeroSkill.GetMaxGemCount` に旅人固有の倍率がなく、
追加処理は `Se_Shrine_Chaos_StatBonus.AddEssenceSlotBonus` とその絶対値反映です。
本体の初期フィールドはQ/W/E/R各3、Identity・Movement各0。
汚染された混沌の祠のコード既定値は追加量1、取得判定上限4で、
MODなし・既定設定・全対象と報酬が取得可能なら、
Q/W/E/R各+1、Identity+4、Movement+0、合計**+8**が取得判定から導けます。
ただし実際の上限・報酬候補・旅人prefabを持つUnity assetsは供給されておらず、
実設定と到達可能な最大Nは未確定です。MOD枠も祠の取得判定には含まれるため、
取得順によって本体の取得可能量が変わり、常に「本体+8とMOD+2」を同時取得できるとは限りません。
保存された本体カウンタ自体にはこの取得上限の丸めがないので、4を超える値を
MOD由来と決めつけて引いたり、MOD共通+2へ丸めたりしません。

本体の星座装着枠は別単位です。`HeroConstellationSettings` の既定は
各系統3→5（+2）、4系統すべて既定なら合計+8ですが、旅人ごとの実設定は未確定です。
これはエッセンス枠でもMODの星図点でもなく、今回の制限では変更しません。

根拠は本体 `HeroSkill.cs:201-287,1166-1204`、
`Se_Shrine_Chaos_StatBonus.cs:38,60-78,404-466`、
`Shrine_CorruptedChaos.cs:149-156,183-187`、`EditSkillShrine.cs:189-199`、
`HeroConstellationSettings.cs:6-8`、`Hero.cs:109-115`、
`HeroLoadoutData.cs:182-232`、`DewProfile.cs:1313-1329`。
`Dew.Contents.dll` の全ILを調べた枠参照も、祠の上限読取と本体カウンタ追加だけでした。
調査用のIL走査は起動時検査や本体API依存を増やすものではありません。

### 空殻の専用経路・返金・協力プレイ

- `Hero_Husk` の枠追加は上の3星だけです。`killing-flow` と `wind-scar` は
  Identityに各+1、`flash-step` はMovementに+1。Identityの重複は+1へ、
  MOD全出所の合計は+2へ制限します。
- v2.4の#169は専用の3記憶間連携・仕掛け・鍵石の再設計です。
  `docs/specs/v2.4-husk-trio-starmap.md`、Husk manifestと生成定義の
  10鍵石・Choice・Mechanismに追加枠はありません。旧ルートの `.slot` は維持され、
  revision 2の返金対象26星や2リング中心を別の枠追加として数えません。
- 本体の `Hero_Husk` は剣の表示状態、`Se_D_TheKillingFlow` は攻撃速度から攻撃力への変換、
  `St_D_ScarOfTheWind` は移動後の攻撃効果です。本体星
  `Se_Star_Husk_L_DodgeChargesAndRangeWithPenalty` の `addedCharge=1` は回避回数で、
  エッセンス枠を増やしません。
- 個別返金・全振り直し・鍵石解除・依存関係の一括返金はいずれも
  `ClientSession.MarkDirty(true)` から新しいProfileの `Build.Compute` を送ります。
  #202の変更は移行通知の重複抑制で、通知表示を追加枠や返金差分として適用しません。
  `ProfileCodec` / `AuthoredStarMigration` の移行後にBuildを計算します。
- ホストは `HostBuildValidation` で出所から再計算し、同じ完全Buildの再送をまとめます。
  本体枠への書込はサーバーだけ。クライアントの `OnApplied` は要約を保持するだけで、
  本体のSyncVarも絶対値を同期し、参加者ごとの追加加算はありません。
- 本体の混沌の祠は通常報酬でもIdentityを保存された本体カウンタへ書き戻します。
  本体0+MOD1の状態へ本体カウンタ1が書かれると、数値が同じ1なので従来の台帳は
  本体分をMOD分と誤認しました。そのまま返金すると本体1も0へ減りました。
  既知の公開 `NotifyUpdate` が正常終了した時だけIdentityの旧MOD所有量を破棄し、
  現在のBuildのMOD分を再適用します。Movementや任意の `SetMaxGemCount` は対象外です。
  このHarmonyパッチの失敗は既存のクラス単位の隔離に従い、MOD全体を停止しません。
- ビルド済みDLLの診断実行では、この同値書き戻しを100回反復して、
  修正前は本体1+MOD1が1、返金後0。修正後は2を維持し、返金後も本体1を保持しました。
  これは正規枠の誤減算の再現であり、報告された増殖の原因の断定ではありません。

### 確認した不具合と安全な復帰

- 本体の `DewPersistence.GemData` は装着場所を保存し、`ApplyPlayerData` は枠番号を
  そのまま `HeroSkill.EquipGem` へ渡します。本体のこの復元経路には枠上限の確認がありません。
- 修正前の `HostAuthority.UpdateGemSlot` は枠数が減った時しか溢れを調べなかったため、
  既存セーブの高い枠番号のエッセンスが、枠数の維持・増加時には残りました。
  修正後は正常に読み戻せた本体枠数に対し、毎回装着場所を整理します。
  `HeroSkill.UnequipGem` の既存経路で元のエッセンスを所有者付きで足元へ戻し、
  破壊・再生成しません。取り外しに失敗したものは次回に再試行します。
- 本体が追加枠を既に取り消した後のMOD解除で、その枠をもう一度引かないようにしました。
  枠数の不一致回数による機能停止は廃止し、部品単位の台帳を再適用・ホスト再生成でも共有します。

原本の場所は `HeroStarRoutes.AddEssenceSlots`、集計は `Build.ComputeTree`、
通信の丸めは `Build.Decode`、本体への反映は `HostAuthority.GemSlots.cs` です。
本体側の根拠はローカルの `DewPersistence.cs:272,277,1214-1218,1333`、
`HeroSkill.cs:926-969,998-1012,1180-1200`（r1.4.0.13の逆コンパイル）です。
通常の反復適用は修正前も増殖せず、報告された実機でのすべての発生条件まで特定したわけではありません。
本体UIは `GetMaxGemCount`、操作は `EditSkillManager` の同じ上限を使います。
Q/W/E/Rや装着記憶数を増やさず、MOD由来の追加量を既存範囲に保ちます。
実機・協力プレイの表示と操作は未確認です。

### v2.7.2の枠消失経路と追加修正

比較対象は公開タグ `v2.7.2` と `fix/star-slots`。ホストでの報告は受領済みですが、
枠の出所と消失のタイミングは未着で、下記の再現が実際の報告原因だったとは断定しません。
本体側の調査範囲は提供されたr1.4.0.13のDLL・逆コンパイルです。

| 経路 | v2.7.2の状態 | ブランチの状態・根拠 |
| --- | --- | --- |
| 部屋・通常ゾーン移動 | 本体は人間の同じHero/HeroSkillを保持。移動だけで台帳が入れ替わる経路は確認できない | 本体 `Hero.cs:142-151`、`ZoneManager.cs:1284-1289,1485-1501`。非活性化で台帳を永久退役させる旧処理は既に修正済み（下記） |
| #232ランダムゾーン・Infinity地図再生成 | `TravelToZone(..., noAdvance:true)` で移動し、別の遠征や旅人を生成しない | `src/SodRpg.Mod/InfinityMode.cs:570-594`、本体 `ZoneManager.cs:1299-1333,2372-2376,2523-2561`。ゾーン用効果のリセットと枠の所有台帳は別 |
| 保存・読み込み・「続きから」 | 本体は枠数を保存せず、装着場所と祠のカウンタを先に復元。待機中や完了時に巻き戻し前のBuildを適用できる | 今回修正：既存・待機中のBuildを破棄し、本体復元中は枠変更・取り外しを保留。Profileの巻き戻し後に開く。本体 `DewPersistence.cs:810-816,915-961,1317-1383`、`src/SodRpg.Mod/ClientSession.Continue.cs:131-186`、`HostAuthority.BuildValidation.cs:14-25` |
| 再接続・途中参加 | 再接続はGUID別の本体保存から新しい旅人を復元。初回途中参加で他人の汚染された祠の枠カウンタを複製する処理はない | 本体 `GameManager.cs:657-684`、`DewPersistence.cs:1388-1399`、`DewPlayer.cs:4293-4320`。今回修正：再開した保存プレイヤー・オフライン再参加の出所を追跡し、同じGUIDでも別の接続オブジェクトは復元証明を再取得。`src/SodRpg.Core/Game/GemSlotContinueSources.cs:48-103` |
| ホスト／参加者 | 本体枠の変更はホストだけ。参加者の再開ProfileはHello受信で後から巻き戻るので、合法だが別の遠征由来のゼロ枠Buildが先に届き得る | 今回修正：既存Helloのrun/checkpoint/resume識別子で成功した巻き戻しを返答し、その後の完全Buildを検証して初めて削減を解放。古い集約・キャッシュを破棄するので、同じencoded Buildでも新しい出所を処理。`ClientSession.Hello.cs:53-111`、`HostAuthority.Hello.cs:58-59`、`HostAuthority.BuildValidation.cs:47-59,110` |
| 本体の祠・他の追加枠 | 本体の絶対値書き戻しと旧MOD所有量が混同され、返金で本体分を減らし得る | 公開NotifyUpdateの観測は既に修正済み。今回さらに保存された本体Identityカウンタを下限にして、任意パッチの失敗時も本体分を減らさない。`src/SodRpg.Core/Game/GemSlotLedger.cs:44-56`、`src/SodRpg.Mod/HostAuthority.GemSlots.cs:141-175`。提供DLLでの本体ゲームプレイ書込は混沌の祠のみ。Q/W/E/R、星座枠や本体カウンタの上限は変更しない |
| 遠征中の星取得・振り直し | 通常の星購入・返金UIは遠征中にロック。依存条件の一括返金などは新しいProfileからBuildを送る | `src/SodRpg.Mod/ClientSession.cs:181,183-190`、`DreamforgeUi.cs:2256,2602`。今回の制限は同じ旅人の正規の振り直しを止めず、削るのは確認済みMOD所有量だけ。前の変更で台帳の再適用・削減済み枠の二重減算を修正済み |
| #246の不一致警告 | バージョン・内容・Helloの遅延は警告のみ。ただし旧枠競合ラッチは別に残る | ラッチ・旧disabled通知による枠非表示は既に撤去済み。今回の出所確認にも版・Protocol・内容の一致を条件にしない。`src/SodRpg.Mod/ClientSession.GemSlotConflict.cs:23-26`、`HostAuthority.Hello.cs:58-86` |
| RPCだけの再登録 | 旅人が生存したままserverActorが交換されると旧Unhookが枠を削減できる。本体の上記移動ではserverActor交換は確認できない | 条件付き経路も今回修正：RPC再登録では枠を戻さず、部品の台帳と装着物を保持。実際の解除・非活性旅人の片付けとは分離。`src/SodRpg.Mod/HostAuthority.cs:1092-1093,1567-1573` |

**v2.7.2から既に直っていた部分**：
`GemSlotLedger.cs:38-39,61-87` の「10秒に3回の変化で停止」、
`HostAuthority.GemSlots.cs:120-132` の両場所停止、
同 `143-149` の非活性部品の永久退役、
`GemSlotLedger.cs:91-94` の盲目的な旧所有量減算、
同値・増加する本体の絶対値書き戻しの誤認、
`HostAuthority.GemSlots.cs:183-185` の縮小時だけの溢れ整理です。
これらは本ブランチの前2コミットで修正済みで、今回の新規修正と区別します。

**互換性と安全側の制限**：
復元証明はバージョン認証ではなく、既知の保存地点の出所確認です。
通常の遠征・初回途中参加・本体枠・他のMOD機能はHelloなしでも続行します。
復元済みの参加者が返答を送らない旧版などでは、保存済みエッセンスを支える
可能性のあるMOD末尾と既存MOD所有量だけを、共有+2・場所別+1以内で保護し、
その部品の未確認の縮小を保留します。高い旧バグ枠を本体分と決めつけて残しません。
返答だけでは古いBuildを適用せず、返答後の完全Buildの検証成功が必要です。
不正Build・別保存地点の返答では保護を解放せず、同じ接続の再返答で二重巻き戻しもしません。
Protocol 24・保存形式5・既存メッセージの形式は変更していません。

**実行した確認**：
追加修正前のブランチで、本体カウンタ1が返金で0になるケースと、
本体復元中のゼロ枠Buildで保存エッセンスが外れるケースの2件が失敗することを確認。
修正後は枠アダプタの回帰20件と再開処理の回帰14件が成功しました。
実際のホスト受信・集約・検証・枠反映ソースをコンソールで実行し、
本体1＋未確認MOD2と元の3エッセンスを保持、
互換性情報が異なる一致返答だけでは削減しない、不正Buildでも保持、
返答後に同じencodedの正規ゼロ枠Buildを検証するとMOD2だけを元の物のまま戻し、
本体1とその装着物は保持、再送でも二重取り外しなし、を観測しました。
この実行は本体API境界のダブルを使った診断です。Unity実機・実際の協力プレイの表示は未確認です。
Releaseビルドは警告5・エラー0、`python tools/balance/gen_cs.py --check` は成功。
`DOTNET=/usr/bin/dotnet DOTNET_ROLL_FORWARD=LatestMajor python tools/test_changed.py --all` は
**2587成功・0失敗・既存4スキップ**（Native 91、Core 2426＋4スキップ、Startup 70）。
今回の最小回帰は8ケースです。
