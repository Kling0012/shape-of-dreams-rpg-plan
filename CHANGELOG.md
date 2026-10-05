# 更新履歴（アップデートノート）

Dreamforge RPG（Shape of Dreams 用MOD）の更新履歴。新しい版が上。
各版は GitHub の [Releases](https://github.com/Kling0012/shape-of-dreams-rpg-plan/releases) にもあり、最新版には導入用の zip を添付する。
これから作るものは [開発計画](docs/dreamforge-roadmap.md)、利用者の判断待ちは [保留事項](docs/dreamforge-pending.md) を参照。

---

## 未リリース / Unreleased

---

## v2.3.1 — インフィニティと重さの修正（2026-10-06）

v2.3.0 でインフィニティが使えなかった問題と、遊んでいると重くなる問題を直しました。 / Fixes Infinity being unusable in v2.3.0 and several causes of heavy slowdowns.

### 更新前に確認 / Before updating

- **セーブデータはそのまま引き継げます**（形式は v2.3.0 と同じです）。 / Saves carry over (same format as v2.3.0).
- **協力プレイでは、参加者全員がこのバージョンに更新**してください（Protocol 21 のままですが、ホストの判定が変わっています）。 / Everyone in co-op should update (still Protocol 21, but host-side checks changed).

### 不具合の修正 / Bug fixes

- **インフィニティを開始できない**：ロビーで理由なく「インフィニティは無効です」と出たり、ソロやホストでも「全員の対応MODが必要です」と出て開始できなかったりしました。ホスト自身は確認の対象から外し、参加者だけを確認するようにしました。参加者の画面では、ホストの返事を待つ間「ホストの設定を待っています…」と表示します。 / **Infinity could not start**: the lobby showed "Infinity is disabled" without a reason, and solo or host games were blocked by the "everyone needs a compatible mod" check. The host is no longer checked against itself; only joining players are. Joining players see "Waiting for the host's settings…" until the host answers.
- **インフィニティで開始しても通常のマップになる**：開始直後のマップ作成で MOD 側の処理が失敗し、インフィニティが止まって通常のマップとボス部屋になっていました。最初のマップから正しくインフィニティになるようにしました。 / **Infinity started on a normal map**: a failure right after the first map was generated turned Infinity off, leaving the normal map with a boss room. Infinity now applies from the first map.
- **ドリームダストへの変換で固まる**：カバンが満杯のときに敵をたくさん倒すと、あふれた遺物1個ごとに保存が走り、ゲームが大きくカクついたり固まったりしていました。あふれはまとめて処理し、保存は1回にしました。どの遺物がダストになるか・量は変わりません。 / **Freezes when overflow turns into Dream Dust**: with a full satchel, every overflowing relic triggered a full save, causing heavy stutter during mass kills. Overflow is now settled in one batch with a single save; which relics convert and the amounts are unchanged.
- **爆発が多いと重くなる**：「華麗なる芸術家」など爆発をたくさん起こす記憶や多段ヒットで、MOD の命中ごとの処理が増えて重くなっていました。効果は変えずに処理を軽くしました。 / **Slowdown with many explosions**: memories such as Explosion Artist and multi-hit attacks multiplied the mod's per-hit work. It is now much lighter, with no change to effects.
- **ビスマスの記憶スロットが空になる（ホスト）**：Q と R に同じ本を装備して始めると、記憶が装備されないことがありました。 / **Bismuth's memory slots were empty (host)**: starting with the same book in Q and R could leave memories unequipped.
- **同じ記憶を2枠に入れると星・連携が動かなくなる**：同じ記憶を2つの枠に装備すると、ほかの記憶の星や連携まで動かなくなることがありました。 / **Stars and links stopped with the same memory in two slots**: it could also stop stars and links of your other memories.
- **装備タブのボタンが画面外に出る**：変更と払い戻しの欄が出ているとき、装備タブの左の欄をスクロールできるようにし、下のボタンが押せるようにしました。 / **Gear tab buttons off screen**: the gear tab's left column now scrolls, so its lower buttons stay reachable while the refund panel is open.

---

## v2.3.0 — 星図の作り直しと遊びやすさの改善（2026-10-05）

星図を見やすく作り直し、鞄・鍛冶・遠征の終わり方などの扱いを変えました。見つかった不具合も直しています。 / A clearer star map, changes to the satchel, the forge and how a run ends, plus bug fixes.

### 更新前に確認 / Before updating

- **セーブデータはそのまま引き継げます**（形式は v2.2.0 と同じです）。 / Saves carry over (same format as v2.2.0).
- **協力プレイでは、参加者全員がこのバージョンに更新**してください。通信の仕組みが変わりました。 / Everyone in co-op must update; the network protocol changed.
- 更新後はゲームを再起動してください。 / Restart the game after updating.

### 新しい要素・変更 / New and changed

- **星図の配置**：星を記憶ごとの区画にまとめ、星団ごとに並べ直しました。どの星がどの集まりに属するかが分かりやすくなり、線が重なって見づらい所も大きく減りました。 / **Star map layout**: stars are grouped into a sector per memory and laid out by cluster, so it is clear which group each star belongs to, with far fewer overlapping lines.
- **星の説明**：説明の最初に「何が増えるか・減るか」を出し、続けて「いつ・何に・どう働くか」を書くようにしました。連携の相手は「記憶『◯◯』」「エッセンス『◯◯』」と種類と名前で示します。取れない星には、何が足りないか（隣の星が未取得、ポイント不足など）を表示します。 / **Star descriptions**: each starts with what goes up or down, then when, on what and how it works. Links name the Memory or Essence. Locked stars say what is missing.
- **星図の左の一覧**：星ごとに名前・効果の要約・状態（取得済み／取得可能／条件不足）を表示し、クリックするとその星へ移動します。 / **Star list on the left**: shows each star's name, a short effect summary and its state; click to jump to it.
- **鞄があふれたとき**：遠征中に鞄の上限を超えたら、レア度の低い遺物から、その遺物の持ち主の夢のダストに換えるようにしました（これまでは一番弱い遺物を欠片に換えていました）。道標の効果で報酬が得られないときは、今までどおり何も得られません。 / **Satchel overflow**: extra relics now turn into the owner's Dream Dust, lowest rarity first (previously the weakest became shards).
- **鍛冶の強化の失敗**：失敗しても +0 に戻らなくなりました。失敗したときは、半分の確率で強化値はそのまま、半分の確率で1段だけ下がります。 / **Forge failures**: a failed enhancement no longer resets to +0. Half the time nothing changes; otherwise it drops one level.
- **「ロビーに戻る」**：遠征中にホストが「ロビーに戻る」を選ぶと、その遠征は失敗（敗北）として精算されるようになりました。「メニューに戻る」「デスクトップに戻る」は今までどおり中断で、「続きから」で再開できます。 / **Return to Lobby**: when the host chooses it mid-run, the run now ends as a defeat. Return to Menu and Quit to Desktop still suspend the run for Continue.

### 不具合の修正 / Fixes

- **装備のボタンが見えない**：ロビーの装備画面で、装着・外す・鍵のボタンが画面の外に押し出され、見えなくなっていました。ボタンを詳細の上に移しました。 / **Missing gear buttons**: in the lobby the equip/unequip/lock buttons were pushed off the window. They now sit above the details.
- **ボスが悪夢化する**：一部の道標を組み合わせると、ボスが悪夢化することがありました。 / **Nightmare bosses**: some waypoint combinations could turn bosses into nightmares.
- **インフィニティの配当**：保存の照合が合わず報酬を止めているあいだも、夢の圧の配当だけが入ってしまうことがありました。 / **Infinity dividends**: pressure dividends could still pay out while rewards were halted after a save mismatch.

この版の変更は、コードとシミュレーションで確認したもので、実際のゲームと協力プレイでの確認はまだです。おかしな点があれば教えてください。 / These changes were verified by code and simulation, not yet in the live game or co-op. Please report anything odd.

---

## v2.2.0 — インフィニティモード（2026-10-05）

新しい遊び方「インフィニティモード」を追加しました。ひとつの世界で、終わりなく遠征を続けられます。 / Adds Infinity Mode: an endless run in a single world.

### 更新前に確認 / Before updating

- **セーブデータはそのまま引き継げます**。 / Saves carry over.
- **協力プレイでは、参加者全員がこのバージョンに更新**してください。通信の仕組みが変わりました。 / Everyone in co-op must update; the network protocol changed.
- 更新後はゲームを再起動してください。 / Restart the game after updating.

### 新しい要素 / New

- **インフィニティモード**：ロビーの「夢の深さ」の近くで ON にできます（ホストが設定し、参加者にも伝わります）。最初のゾーンの世界から出ずに、部屋を進み続けます。 / **Infinity Mode**: turn it on in the lobby next to Dream Depth (the host decides; everyone sees it). You stay in the first zone's world and keep going.
  - 戦闘の部屋を決まった数（10・15・20 から選べます）クリアするたびに、そのゾーンのボスが現れます。 / Every 10, 15 or 20 cleared combat rooms (your choice), the zone's boss appears.
  - ボスを倒して報酬を受け取ると、確保の画面が出ます。「確保して帰還」でその遠征を終え、「深く潜る」でそのまま続けます。 / After the boss and its reward, the secure screen opens: secure and return to end the run, or delve to keep going.
  - クリアした部屋が増えるほど、敵が少しずつ手ごわくなります（上限あり）。 / Enemies grow tougher as cleared rooms add up (capped).
  - 「続きから」で再開しても、進み具合が戻ります。 / Progress is restored when you resume with Continue.

### 不具合の修正 / Fixes

- **ゲームの更新や他の MOD と合わない機能**：起動時の確認で合わない所が見つかっても、一部の連携（LastStarlight）がそのまま動き、本体のスキルの処理を途中で打ち切ることがありました。合わない機能だけを使わないようにし、想定外の動きのときは本体の処理をそのまま通すようにしました。 / **Features that no longer match the game or other mods**: one boss gear link (LastStarlight) could stay active after a mismatch and cut a game skill short. Only the mismatched feature is now turned off, and unexpected cases fall back to the game's own behavior.

インフィニティモードはコードとシミュレーションで確認したもので、実際のゲームと協力プレイでの確認はまだです。おかしな点があれば教えてください。 / Infinity Mode was verified by code and simulation, not yet in the live game or co-op. Please report anything odd.

---

## v2.1.2 — 鍛冶の遺物が消える不具合の修正（2026-10-05）

「続きから」で遠征を再開したあとに鍛冶で遺物を作ると、遺物が消えることがある不具合を直しました。 / Fixes relics disappearing when crafting after resuming a run with "Continue".

### 更新前に確認 / Before updating

- **セーブデータはそのまま引き継げます**（形式は v2.1.1 と同じです）。 / Saves carry over (same format as v2.1.1).
- **協力プレイでは、参加者全員がこのバージョンに更新**してください。 / Everyone in co-op should update to this version.
- 更新後はゲームを再起動してください。 / Restart the game after updating.

### 不具合の修正 / Fixes

- **鍛冶で作った遺物が消える**：ロビーで遺物を作ってから「続きから」で遠征を再開し、もう一度遺物を作ると、前に作った遺物とまったく同じ遺物（内部の識別番号が同じもの）ができることがありました。その場合、セーブを読み込み直すと片方が消え、支払った欠片も戻りませんでした。再開しても前の製作の続きから抽選するようにし、同じ識別番号の遺物ができないようにしました。 / **Crafted relics vanishing**: crafting in the lobby, resuming with "Continue" and crafting again could produce an exact copy of the earlier relic (same internal ID). On reload one copy was dropped and the shards were not refunded. Rolls now continue from the earlier craft after resuming, and duplicate IDs are no longer created.

この修正はコードとテストで確認したもので、実際のゲームでの確認はまだです。おかしな点があれば教えてください。 / This fix was verified by code and tests, not yet in the live game. Please report anything odd.

---

## v2.1.1 — 起動しない不具合の緊急修正（2026-10-05）

v2.1.0 で、MOD が起動しない（入れても動かない）ことがある不具合を直した緊急の修正版です。 / Urgent fix for v2.1.0: the mod could fail to start at all.

### 更新前に確認 / Before updating

- **セーブデータはそのまま引き継げます**（形式は v2.1.0 と同じです）。 / Saves carry over (same format as v2.1.0).
- **協力プレイでは、参加者全員がこのバージョンに更新**してください。 / Everyone in co-op should update to this version.
- 更新後はゲームを再起動してください。 / Restart the game after updating.

### 不具合の修正 / Fixes

- **MOD が起動しない**：v2.1.0 では、起動時の確認でゲーム本体や他の MOD との違いが1つでも見つかると、MOD 全体が止まり、入れても何も表示されないことがありました。確認で問題が見つかっても MOD 全体は止めず、合わない部分だけを使わないようにしました。 / **Mod not starting**: in v2.1.0, a single mismatch with the game or another mod found at startup stopped the whole mod. Now only the affected part is turned off and the rest keeps working.
- **ボス装備のアイコン**：v2.1.0 の配布ファイルで、ボス装備の専用アイコンが正しい場所に入っておらず、表示されていませんでした。 / **Boss gear icons**: the v2.1.0 package put the boss gear icons in the wrong folder, so they did not show.

---

## v2.1.0 — ボス限定の装備セット（2026-10-05）

14体のボスに、それぞれのボスだけが落とす装備セットを追加しました。 / Fourteen bosses now each drop their own exclusive gear set.

### 更新前に確認 / Before updating

- **セーブデータはそのまま引き継げます**（形式は v2.0.3〜v2.0.5 と同じです）。 / Saves carry over (same format as v2.0.3–v2.0.5).
- **協力プレイでは、参加者全員がこのバージョンに更新**してください。通信の仕組みが変わりました。 / Everyone in co-op must update; the network protocol changed.
- 更新後はゲームを再起動してください。 / Restart the game after updating.

### 追加 / New

- **ボス限定の装備セット（14セット・84部位）**：森の悪魔、スコール、インフェルヌス、白夜、暗月、ニュクス、エレボス、シーカー、アズラク、プリムス、光の精霊、大顎、オブリヴィアクス、ポラリスの14体が、それぞれ全部位（6部位）の専用セットを落とします。そのボスを倒したときにしか手に入りません。 / **Boss-exclusive sets (14 sets, 84 pieces)**: each of fourteen bosses drops its own full six-piece set, obtainable only by defeating that boss.
- **ボスの技を使う装備**：各部位とセット効果は、そのボスの技をプレイヤーが使える形にしたものです。2・3・6部位とそろえるごとに、戦い方が変わります。たとえば森の悪魔のセットでは、踏みつけの衝撃波や、時間差で噴き出す樹木で戦います。 / **Gear that fights like the boss**: piece and set effects turn the boss's own moves into player abilities, and the way you fight changes at 2, 3 and 6 pieces — the Forest Demon set, for example, stomps and grows trees that burst out after a delay.
- **記憶・エッセンスとの連携**：そのボスが落とす記憶やエッセンスと一緒に装備すると、それらの固有の効果に作用する連携効果が発動します。2・4・6部位で段階的に強くなります。セットだけでも、記憶・エッセンスだけでも、これまでどおり使えます。 / **Links with the boss's memory or essence**: equipping the matching memory or essence adds a link that changes how that reward works, growing at 2, 4 and 6 pieces. Each still works on its own.
- **専用のアイコン**：84部位すべてに、ボスごとの色と意匠でそろえた専用のアイコンを付けました。 / **Dedicated icons**: all 84 pieces have their own icons, styled per boss.
- **確認用のコマンド**：開発者用のコマンドで、セットの受け取りや、ボス撃破の抽選を試せます（開発者モードのときだけ）。 / Developer-only console commands let you grant a set or test the boss drop roll.

### 不具合の修正 / Fixes

- **「続きから」の再開**：メニューからロビーに戻り、タイトルの「続きから」で再開したとき、ゲーム本体だけが前の保存地点に戻り、MOD の鞄や撃破の記録は戻らないことがありました。巻き戻った部屋の報酬を二重に得られる可能性があったので、MOD の状態も同じ保存地点にそろえるようにしました。あわせて、ロビーで中断中の遠征があるときは、その旨と、遠征を終えるまで利用できない操作を表示します。 / **Continue**: after returning to the lobby and using Continue, the game could roll back to an earlier save point while the mod's satchel and kill records did not, allowing rewards to be earned twice. The mod now rolls back to the same point, and the lobby shows when an expedition is suspended and what is locked until it ends.

追加した効果の多くは、まだ通常のプレイや協力プレイでのテストが十分ではありません。おかしな点があれば教えてください。 / Many of the new effects are not yet fully tested in the live game and co-op. Please report anything odd.

---

## v2.0.5 — 分解・純白ルート・起動時の読み込みの修正（2026-10-05）

v2.0.4 のあとに見つかった不具合を3つ直しました。 / This release fixes three bugs found after v2.0.4.

### 更新前に確認 / Before updating

- **セーブデータはそのまま引き継げます**（形式は v2.0.3・v2.0.4 と同じです）。 / Saves carry over (same format as v2.0.3 and v2.0.4).
- **協力プレイでは、参加者全員がこのバージョンに更新**してください。通信の仕組みが変わりました。 / Everyone in co-op must update; the network protocol changed.
- 更新後はゲームを再起動してください。 / Restart the game after updating.

### 不具合の修正 / Fixes

- **分解と確保**：遺物の分解を待っているあいだに確保すると、遺物が保管庫に残ったまま、分解の対価も受け取れてしまうことがありました。分解を待っている遺物は、結果が出るまで別枠で保管するようにしました。 / **Salvage and secure**: securing while a salvage was still pending could keep the relic in your stash and also pay out the salvage. Relics waiting on a salvage are now set aside until the result arrives.
- **純白ルートの協力プレイ**：ホストが選択を確定すると、参加者の保留中の報酬が、参加者本人が確保か潜行かを選ぶ前に、潜行として精算されていました。本人が選ぶまで精算を待つようにしました。 / **Pure-white route in co-op**: when the host decided, participants' pending rewards were settled as a delve before they chose to secure or delve. They now wait for each player's own choice.
- **起動時の読み込み**：セーブデータの初回の読み込みでエラーが起きると、MOD がまったく動かなくなっていました。修正後は、読み取り専用で起動して画面で知らせ、セーブデータを上書きせずに、自動で読み込み直します。 / **Loading at startup**: a read error on the first save load stopped the mod entirely. It now starts read-only, tells you on screen and retries automatically, without overwriting your save.

---

## v2.0.4 — 夢の圧の追加報酬の修正（2026-10-05）

v2.0.3 で見つかった、報酬の不具合を1つ直しました。 / This release fixes one reward bug found after v2.0.3.

### 更新前に確認 / Before updating

- **セーブデータはそのまま引き継げます**（v2.0.3 と同じ形式です）。 / Saves carry over (same format as v2.0.3).
- **協力プレイでは、全員このバージョンに更新**してください。 / Everyone in co-op should use this version.
- 更新後はゲームを再起動してください。 / Restart the game after updating.

### 不具合の修正 / Fixes

- **夢の圧の追加報酬**：コインバーストや忘却の咆哮でとどめを刺したときに、夢の圧の効果で未確保の欠片が1個追加されることがあります。v2.0.2 以降、この抽選が行われていなかった不具合を直しました。 / **Dream-pressure bonus**: finishing enemies with Coin Burst or Shout of Oblivion can grant one extra unsecured shard through dream pressure. Since v2.0.2 this roll never happened; it works again.

---

## v2.0.3 — 不具合の修正と軽量化（2026-10-05）

v2.0.2 のあとにまとめて見直しを行い、見つかった不具合を直しました。長いプレイで重くなる問題も、さらに軽くしています。 / After a full review following v2.0.2, this release fixes the bugs it found and further reduces slowdowns on long runs.

### 更新前に確認 / Before updating

- **セーブデータはそのまま引き継げます**。ただし、この版で一度セーブするとセーブデータの形式が新しくなり、古い版の MOD ではそのセーブデータを読み込めなくなります（セーブデータが消えるわけではありません）。古い版に戻すかもしれない場合は、更新の前にセーブデータを控えておいてください。 / Saves carry over. Once this version saves, the save format is upgraded and older mod versions can no longer load that save (it is not deleted). If you might roll back, back up your save before updating.
- **協力プレイは全員この版に**してください。通信の仕組みが変わりました。 / Everyone in co-op must update; the network protocol changed.
- 更新後はゲームを再起動してください。 / Restart the game after updating.

### 不具合の修正 / Fixes

- **遠征が進まなくなる**：協力プレイで撃破の記録が1件でも届かないと、確保や遠征の決着が止まったままになることがありました。届かない記録は30秒で打ち切り、遠征が先に進むようにしました。 / **Runs getting stuck**: in co-op, one missing kill record could stop securing and the end of the run indefinitely. A missing record is now settled after 30 seconds so the run moves on.
- **進行が失われる**：セーブの書き込みに失敗したとき、やり直さずにそのままになっていました。失敗したら再試行するようにしました。報酬を付与する途中で問題が起きても、撃破の報酬が消えないようにしました。 / **Lost progress**: a failed save write was never retried; it now is. Kill rewards are no longer lost if something goes wrong while granting them.
- **商人**：購入の応答が遅れたとき、同じ商人から何度も買えたり、次のゾーンの商人が消えたり、品物の質が購入時と違う深さで決まったりしていました。 / **Merchant**: when a purchase reply was slow, you could buy from the same merchant again, the next zone's merchant could vanish, and item quality could use the wrong delve depth.
- **純白ルート**：選択を保留したまま倒した敵が、戦った深さとは違う深さで精算されていました。勝ったときに深さや確保のボーナスが1段多くなることもありました。 / **Pure-white route**: kills made while the choice was pending were settled at the wrong depth, and winning could add one extra depth step to bonuses.
- **潜行を深めると敵が回復する**：純白で潜行を深めたとき、削った敵の HP が一緒に増えていました。 / **Enemies healing on delve**: deepening the delve in the pure-white route raised damaged enemies' current HP.
- **通知の名前**：星図ポイントや熟練度の通知に、旅人の内部名（Hero_Vesper など）が出ていました。 / **Notification names**: star-point and mastery notices showed internal traveler IDs such as Hero_Vesper.
- **ホストの処理が止まる**：ホスト側で1か所に問題が起きると、ほかの処理まで止まっていました。悪夢・変種の敵の名前表示が一斉に消えることもありました。 / **Host processing stalls**: one failing step on the host stopped everything after it, and nightmare/variant nameplates could all disappear at once.
- **長いプレイで重くなる**：戦闘中の処理の負荷と、撃破の記録の再送、セーブデータの大きさを減らしました。途中参加や再接続でホストが固まることも減ります。 / **Slowdowns on long runs**: combat work, kill-record resends and save size are reduced, and hosts no longer freeze when someone joins mid-run or reconnects.

不具合の多くはコードから原因を突き止めて直したもので、実際のゲームと協力プレイでの確認は一部まだです。おかしな点があれば教えてください。 / Many fixes were found by reading the code and are not yet fully checked in the live game and co-op. Please report anything odd.

---

## v2.0.2 — 刻印を最大3つまで・不具合の修正（2026-10-05）

今回の版では、刻印を最大3つまで選べるようにし、v2.0.1 のあとに報告された不具合をまとめて直しました。 / This release lets you choose up to three keystones and fixes the bugs reported since v2.0.1.

### 更新前に確認 / Before updating

- **保存データはそのまま引き継げます**。振った星と刻印の選択もそのまま残ります。 / Saves carry over, including spent stars and chosen keystones.
- **協力プレイは全員この版に**してください。通信の版が変わりました。 / Everyone in co-op must update; the network protocol changed.
- 更新後はゲームを再起動してください。 / Restart the game after updating.

### 追加 / New

- **刻印を最大3つまで選べます**。星のレベル200で2つ、400で3つになります。 / **Up to three keystones**: a second slot at star level 200 and a third at 400.

### 不具合の修正 / Fixes

- **封じられた宝庫**：ボスを倒したあとの戦利品が、ゾーンを移ると消えていた問題を直しました。払い出しは「×3」と表示され、HUD でため込んだ数も見えるようになりました。悪夢化した敵の討伐ログが、払い出しの通知に押し流されることもなくなりました。 / **Sealed Hoard**: post-boss loot no longer vanishes when you travel, payouts show as "x3" with the held count on the HUD, and nightmare kill notices are no longer pushed out by payout messages.
- **純白ルート**：入ったときに、道標・確保/潜行・契約の選択がきちんと出るようになりました。ボスを倒すと確保され、持ち帰れます。選択を保留したまま戦っても、敵の強さは正しく上がります。 / **Pure-white route**: the waypoint, Secure/Delve and pact choices now appear, defeating its boss secures your loot, and enemies scale correctly even while the choice is pending.
- **長いプレイで重くなる・固まる**：敵を倒すたびに保存していたのを、まとめて保存するようにしました。ボスを倒すと雑魚が一斉に倒れる場面で、固まりにくくなります。 / **Slowdowns and freezes on long runs**: saves are now batched instead of written on every kill, which helps when a boss dies and every enemy dies with it.
- **刻印**：複数の刻印を選んだとき、条件を満たしていない刻印の効果が、別の刻印の条件で動いてしまう問題を直しました。 / **Keystones**: with several keystones chosen, one keystone's effect can no longer activate on another keystone's conditions.

### 星図 / Star map

- **ドラッグ**：星を選んでいるときに、ドラッグが引っかかる・図が飛ぶ・意図せず星が選ばれる、といった問題を直しました。 / **Dragging**: no more stutter, jumps or accidental star picks while a star is selected.
- **二択の星**：両方の候補を左右に並べて、比べられるようにしました。マウスを乗せた星と、つながっている星を色付きの輪で示します。 / **Choice stars**: both options are shown side by side, and rings highlight the hovered star and its connected stars.

不具合の多くはコードから原因を突き止めて直したもので、実際のゲームと協力プレイでの確認は一部まだです。おかしな点があれば教えてください。 / Many fixes were found by reading the code and are not yet fully checked in the live game and co-op. Please report anything odd.

---

## v2.0.1 — 取引の安全性と図鑑の修正（2026-10-04）

v2.0.0 後の不具合の修正です。新しい機能はありません。**実機（ゲーム本体・2台での協力プレイ）での確認はまだ**のため、試験版（pre-release）として公開します。 / Fixes since v2.0.0; no new features. Not yet verified in the real game or in two-player co-op, so this is published as a pre-release.

### 更新前に確認 / Before updating

- **保存データはそのまま引き継げます**。未確定の取引も保存されます。 / Saves carry over; unresolved trades are saved too.
- **協力プレイは全員を同じ版に**してください。 / Everyone in co-op must use the same version.
- 更新後はゲームを再起動してください。 / Restart the game after updating.

### 修正 / Fixes

- **図鑑**：銘品と組の分類をゲーム画面から選べるようにしました（#28）。初期装備の銘品も銘品として図鑑に記録され、すでに受け取った分も起動時に補われます（#29）。 / The codex screen now offers Named and Mini sets; starter named relics are recorded as such, and already-granted ones are backfilled.
- **鍛冶**：エピックを36回洗い直したときに費用が負になり、欠片が増えてしまう不具合を直しました（#30）。 / Fixed the epic affix-reroll cost going negative on the 36th reroll.
- **取引**：10秒を過ぎても取引を捨てず、ホストの確定結果を照会して、支払い済みの対価を一度だけ受け取るか、未実行なら遺物を返します。取引idに世代を付け、MODの再読み込みで過去の取引と衝突しないようにしました（#26 #27）。未確定の取引は保存されます。**協力プレイは全員を同じ版に**してください。 / Late trade responses are no longer lost: unresolved trades are kept, queried against the host and settled exactly once; trade ids carry a generation and the host rejects a reused id with a different request.
- **取引**：ホストの取引台帳が失われた（ホストのMOD再読み込み・接続の変更・記録の上限超過）あとでも、「記録がない」ことを「未実行」と見なさず、決済済みの購入・交換・分解を取り消さないようにしました（#36）。ホストの台帳の識別子を取引に結び付け、確かめられない取引は返却も対価も確定せず保留します（手放すにはコンソールで `dreamforge_trades_giveup`）。未確定の取引は全種類で64件までしか始められず、保存・読込・復元の上限も揃えたので、受け付けた取引は再読み込みの後でも解決できます（旧版が書いた65件以上も全件復元）（#37）。 / A trade is no longer cancelled just because the host has no record of it: each trade is bound to the host ledger it was sent to, and when that ledger changed (host reload, new connection, history cap) the result is held instead of refunded. Unresolved trades are capped at 64 for every kind and the cap now matches save, load and restore (older saves above 64 restore in full).
- **取引**：購入・交換・分解の要求を、記録（取引の識別子・内容・ホストの台帳・分解の予約）がディスクへ確実に保存できてからホストへ送るようにしました（#40）。書き込みに失敗したとき、または保存先が無効なときは、取引を送らず（ホストの通貨は動かず）、メッセージを出してやり直せる状態に戻します。これまでは、ホストが支払いを済ませた後に保存の失敗や異常終了が起きると、結果を照会するための識別子を失い、対価を受け取れないことがありました。 / A trade request is now sent to the host only after its record (id, contents, host ledger, salvage reservation) is confirmed written to disk. If the write fails or saving is unavailable, nothing is sent (host currency is untouched) and the trade can be retried. Before, a save failure or crash after the host had charged could lose the id needed to settle the trade.

---

## v2.0.0 — 装備の種類を大きく増やしました（2026-10-04）

これまでの版をまとめた正式版です。以前のリリースは取り下げ、この版に一本化しました。 / This release consolidates all earlier versions; previous releases have been withdrawn.

### 更新前に確認 / Before updating

- **保存データはそのまま引き継げます**（星図・遺物・図鑑など）。 / Saves carry over.
- **協力プレイは全員を同じ版に**してください。 / Everyone in co-op must use the same version.
- 更新後はゲームを再起動してください。 / Restart the game after updating.

### 新しい装備 / New gear

- **土台が各枠60種から100種に**（合計600種）。新しい240種はすべて専用のアイコン付きです。 / **Bases per slot 60 → 100** (600 total); all 240 new bases have their own icons.
- **家系**：土台は「氷霜・炎・光・闇・守り・疾風・癒し・召喚・記憶・無垢」の10の家系に分かれ、家系ごとに出やすい特性と固有効果があります。遺物の説明と図鑑に家系が表示されます。 / **Families**: bases belong to 10 families that favour certain affixes and powers; shown in tooltips and the codex.
- **銘品（360種）**：アンコモン・レア・エピックに、固有の名前・一言・決まった固有効果を持つ遺物が加わりました。特性は通常どおり抽選されます。 / **Named relics (360)**: uncommon, rare and epic relics with their own name, lore line and fixed powers; affixes still roll normally.
- **小さな組（30組）**：一部の銘品は2〜3点の組になっていて、そろえると小さなボーナスが付きます。 / **Mini sets (30)**: some named relics form 2–3 piece sets with small bonuses.
- 図鑑に銘品と組の分類が加わりました。 / The codex lists named relics and mini sets.

### この MOD について / About this mod

遠征ごとに持ち帰れる遺物、ゾーンごとの「確保するか潜るか」の選択、旅人ごとの大きな星図（700星以上）、鍛冶と工房、長く続く目標を Shape of Dreams に加えます。協力プレイに対応しています。過去の変更の詳細は [CHANGELOG](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/claude/dreamforge-playable-v0.1/CHANGELOG.md) にあります。 / Adds keepable relics, a secure-or-delve choice per zone, large per-traveler star maps (700+ stars), a forge and workshop, and long-term goals. Co-op ready.

## v1.31.0 — 星図を10倍に（2026-10-04・Pre）

9人の旅人すべての星図が、約70個から**700〜860個の星**へ広がりました。記憶ごとの星団、2つの記憶をつなぐ橋、外縁の星団、旅人ごとに8〜10個の刻印から、遊び方に合わせて伸ばし方を選べます。 / Every traveler's star map grows from about 70 to **700–860 stars**: memory clusters, bridges between two memories, outer clusters and 8–10 keystones per traveler.

### 更新前に確認 / Before updating

- **協力プレイは全員を同じ版に**してください（通信の版13）。版が違うとホストと参加者の画面に警告が出ます。 / Everyone in co-op must use the same version (protocol 13); a mismatch shows a warning.
- **保存データはそのまま引き継げます**（保存形式4。リセットはありません）。振っていた星・ポイント・刻印はそのまま残ります。効果が変わった既存の星は、読み込み時に一度だけ外され、使ったポイントが全額戻ります（戻した星は画面で知らせます）。 / Saves carry over (format 4, no reset). Stars, points and keystones are kept; existing stars whose effect changed are cleared once on load with a full refund, and you are told which.
- 更新後はゲームを再起動してください。 / Restart the game after updating.

### 星図 / Star map

- **星が10倍に**：Vesper 838・Cetus 856・Husk 835・Lacerta 831・Mist 842・Yubar 833・Aurena 714・Bismuth 701・Nachia 698（共有の外縁160を含む）。記憶の仕掛けの効果量・持続・範囲・対象数を伸ばす星、記憶から別の記憶へクールダウンを渡す星、橋の合わせ技を強める星、味方を守る星などがあります。 / **10× stars** with memory-gimmick, recharge, bridge-combo and ally-ward stars.
- **ポイント上限を500に**（従来150、図鑑のボーナスで最大504）。経験の曲線はそのまま延長し、初期ポイントの配布はありません。夢の圧は振った星の数に応じて従来どおり増えます。 / **Star point cap 500** (was 150; up to 504 with codex). The XP curve continues unchanged; no free starting points; dream pressure keeps scaling with spent stars.
- **2択の星**：2つの効果を並べて見比べ、遠征の外なら無料で切り替えられます。 / **Choice stars**: compare both effects side by side; switch for free outside expeditions.
- **刻印**：説明は「利点」と「代償」に分かれました。代償で既に振った星が効かなくなるときは、外れる星とポイントを一覧で示し、承認すると払い戻してから刻印を選びます。 / **Keystones** show benefit and drawback separately; if a drawback disables stars you own, they are listed and refunded after you approve.
- **見やすさ**：星図はほぼ全画面になり、星団の一覧から移動・始まりに戻る・検索（名前や効果で光らせ「次の星へ」）・凡例に対応しました。取得した効果の一覧は同じ効果をまとめて表示します。 / Near full-screen map, cluster list, back-to-start, search, legend, and an acquired-effects list that merges identical effects.
- **軽さ**：800星超の星図でも描画は軽く、500ポイント振った状態でも星を買う操作はすぐに反映されます。 / Large maps stay smooth and buying stays responsive even at 500 points.
- 星の名前は、効果が想像できる自然な名前にしました。 / Stars have natural, distinct names.

### 装備と鍛冶 / Gear and forge

- **セットが6部位に**：48のセットすべてに3部位を加え、6つそろえたときのセット効果を追加しました。 / **Six-piece sets**: all 48 sets gain 3 pieces and a 6-piece bonus.
- **特性の洗い直し**（鍛冶タブ）：遺物の特性をまとめて引き直します。強化値や固有効果などはそのままです。 / **Affix reroll** keeps enhancement, unique powers and the rest.
- **強化に失敗の可能性**：失敗すると強化値が+0に戻ります（遺物は残ります）。確定前に確率を表示し、危険なときは2回押しで確定します。 / **Enhancement can fail** (resets to +0; the relic is kept); the chance is shown first.
- **まとめて分解のレア度を選択**：コモン／アンコモン／レア／エピックまで。レア以上を含むときは2回押しで確定します。 / **Bulk salvage rarity** selectable; rare+ needs two presses.
- **鞄と保管庫を大きく**（工房）：鞄は最大80個、保管庫は最大420個まで広げられます。 / **Satchel up to 80, stash up to 420** (Workshop).

### 遠征と調整 / Expeditions and balance

- **夢の深さで部屋が増える**：深さ1ごとに各ゾーンの部屋が2つ増えます（深さ5で+10）。 / Each depth step adds 2 rooms per zone.
- **悪夢化「棘皮」と棘を返す変種を弱めました**：跳ね返すダメージは与えたダメージの15%で、1回につき攻撃した旅人の最大HPの1.5%まで、同じ敵からは0.4秒に1回までです。 / **Thorns weakened**: 15% of damage dealt, capped at 1.5% of the attacker's max HP per hit, once per 0.4 s per enemy.

### 不具合の修正 / Fixes

- 協力プレイの再接続・途中参加・MOD再読み込みで、報酬や道標が正しく引き継がれるようにしました。 / Co-op reconnect, late join and mod reload keep rewards and waypoints correct.
- 他のMODと組み合わせたとき、精髄の枠が増え続けることがある不具合を直しました。 / Essence slots no longer keep growing alongside other mods.
- 日本語の画面に英語の警告や内部の名前が出ていた箇所を直しました。旅人の名前も日本語で表示します。 / No more English warnings or internal names in the Japanese UI; traveler names are shown in Japanese.
- アイコン：土台360種すべて、新しい夢の出来事、星の記号に絵が付きました。 / Icons for all 360 bases, new dream events and star symbols.

### 確認したこと / Verification

- Coreの試験2,851件が成功。9人それぞれ、星図の登録・全星への到達・最大ポイントまでの購入と協力通信の送受信を試験しています。 / 2,851 Core tests pass, including per-traveler registration, reachability, max-point purchases and co-op build transfer.
- ゲーム内で9人の星図の表示・ツールチップ・購入の反応（450ポイント以上でも約55ms）・遠征を確認しました。新しい星の効果を戦闘ですべて確かめたわけではありません。気づいた点は Issue で教えてください。 / Checked in game: all 9 maps, tooltips, purchase latency (~55 ms at 450+ points) and an expedition. Not every new star effect has been verified in combat; please report issues.

## v1.30.3 — 協力プレイで参加者に報酬が入らない不具合の修正（2026-10-03・Pre）

- **協力プレイの参加者（ゲスト）に、確保地点が出ず、遺物も星の経験も入らない不具合を直しました。** 参加者の PC ではゲーム本体のゾーン情報がホストより少し遅れて届くことがあり、その間に遠征が始まるとゾーン番号が「不明」のまま記録され、ホストから届く遠征の決まりを適用できずにいました。ゾーン番号が分かってから遠征を始め、すでに始まっていた場合も正しい番号に直します。 / **Fixed co-op participants getting no secure point, relics or star XP.** A participant could start the run before the game's zone info arrived, recording an unknown zone and never applying the host's run rules.
- **星図のポイントが301〜304点になると、装備画面が開かず装備の効果も送れなくなる不具合を直しました**（図鑑のボーナスを含めた上限は304点）。 / Fixed the gear tab and build sending failing when star points reached 301–304 (codex bonus included).
- 協力プレイは v1.30.2 のホストとも遊べます（通信の版12のまま）。直るのは参加者側なので、**参加する人は v1.30.3 にしてください**。保存形式は3のままです。 / Compatible with v1.30.2 hosts (protocol 12); participants should update. Save format 3 unchanged.
- 実機での協力プレイの確認はまだです。Core の試験で、ゾーン番号が遅れて分かった場合に報酬が払われることを確かめています。 / Not yet verified in live co-op; covered by a Core regression test.

## v1.30.2 — 装備の画像・詳しい図鑑・見やすい星図・不具合の修正（2026-10-03・Pre）

- **装備の画像が足りなかった不具合を直しました**：v1.30.1 までは、v1.29 で増えた装備の土台約180種に画像が付いていませんでした。土台360種すべてに画像が付きます。夢の出来事13種と星図の星の記号30種の画像も追加しました。 / **Fixed missing gear images**: the ~180 bases added in v1.29 had no icon in v1.30.1; all 360 bases now have one, plus 13 dream-event illustrations and 30 star-map symbols.
- **図鑑が詳しくなりました**（記録タブ→「図鑑を開く」）：固有品・土台・セット・固有効果を、枠・系統・発見状態で絞り込み、名前や効果で検索できます。右に詳細（効果・特性・セットの部位）を出します。未発見の固有品・セット・固有効果は「？？？」で伏せます。 / **Detailed codex**: filter by slot, line and found state, search by name or effect, with a detail pane; unfound legendaries, sets and powers stay hidden.
- **夢の深さで部屋が増えます**：深さ1段につき各ゾーンの部屋が2つ増えます（深さ5で+10）。深さの表示に「部屋 +N」を出します。 / **Dream depth adds rooms**: +2 rooms per zone per depth level.
- **「レア度の幸運」の表記を「良い遺物の出やすさ +X%」に直しました**（抽選の中身は同じです）。 / The "rarity luck" label is now "better relics +X%" (same underlying roll).
- **星のポイントの上限が500になりました**（旅人ごと）。夢の圧の星の係数は据え置きで、500振るとそのぶん圧が増えます。 / Star points per traveler now cap at 500; the pressure coefficient per star is unchanged.
- **協力は全員この版へ更新してください**：通信の版が12になりました（ビルドの集約と分割送信）。保存形式は3のままで、プロフィールの初期化はありません。 / **All co-op players must update**: protocol 12; save format 3 unchanged, no reset.
- **星図が見やすくなりました**：開くと木の全体が収まります（「全体を表示」で戻せます）。拡大縮小は0.15〜3倍。星は丸く、種類ごとの記号と段の数を表示します。刻印は紫の大きな星で、上部の「刻印」の一覧から探して選べます。合わせ技の橋は水色です。星図タブではメニューを画面いっぱいに広げます。星をつなぐ線が、画面の拡大率によって別の場所に描かれていた不具合も直しました。 / **Clearer star map**: the whole tree fits on open ("Show all" restores it), zoom 0.15–3x, round stars with type symbols and ranks, keystones as large purple stars plus a keystone bar, combo bridges in cyan, a full-screen window on the star map tab, and connection lines no longer drawn in the wrong place at some UI scales.
- **アイコンの追加**：v1.29で増えた装備の土台と、新しい夢の出来事に絵を付けました（順次）。 / **New icons** for the bases and dream events added in v1.29.
- **#20：確保地点の確定が遅れたままゾーンを移ると**、撃破の報酬と遠征の精算が止まることがありました。前のゾーンの報酬をそのゾーンの決まりで払ってから、次の確保地点を開きます。 / **#20**: moving zones while a secure-point confirmation was delayed could stall kill rewards and run settlement. The previous zone is now paid under its own rules before the next secure point opens.
- **#21：ホストだけMODを読み直すと**、参加者が新しい道標を受け取れなくなることがありました。ホストごとの世代番号を付け、新しいホストの選択をすぐ反映し、古い通知では戻らないようにしました（通信の版10）。 / **#21**: after a host-only reload, participants could ignore new waypoint choices. Snapshots now carry a host generation (protocol 10).
- **#22：連携印の脆さ**が通常ダメージへ反映されます。4秒間の2/3/4%で、同じ旅人から同じ敵への連携印・仕掛け・属性反応のExposeは最大値だけを使います。追加生成ダメージは増幅せず、装備解除やゾーン移動で印を消します。 / **#22: Combo-mark vulnerability** now affects normal damage for four seconds at 2/3/4%. Only the strongest Expose from that hero's combo marks, gimmicks, or elemental reactions applies to each enemy. Generated damage is excluded; unequipping either memory or changing zones clears the marks.
- **#23：ナキアの「輪舞の呼吸」「狼の見守り」**は、本人が基本攻撃を撃ったときに働きます。空振りでも発動し、多段命中で回数は増えません。召喚獣の存在、狼の見守りの4秒間の発動窓と1秒間隔は維持します。 / **#23: Nachia's Round Dance Breath and Wolf's Watch** now trigger when she fires a basic attack, including misses, without extra triggers from multiple hits. The summon requirement, Wolf's Watch's four-second window, and its one-second interval remain unchanged.
- **#24：「夢の圧」の依頼**を引き直した後も、現在の圧が条件を満たしていれば次の報告で達成します。同じ圧の報告が続いても報酬は一度だけで、新しい遠征へ前回の圧は持ち越しません。 / **#24: Dream Pressure bounties** rerolled during an expedition now complete on the next report if the current pressure meets their target. Repeated reports grant rewards only once, and a new expedition does not inherit the previous pressure.
- 試験1,699件すべて成功、Releaseビルド成功。実機での戦闘・協力通信は未検証です。 / All 1,699 tests and the Release build pass. Live combat and co-op are not yet verified.

## v1.30.1 — 不撓の心：スタンから立て直す（2026-10-03・Pre）

- **不撓の心**を「敵からスタンを受けると、最大HPの4〜8%の障壁とアンストッパブルを各4秒間得る（8秒に1回）」へ変更して有効化。装備を重ねた通常の合計上限は12%。障壁が壊れてもアンストッパブルは4秒間持続します。
- スロウ・ノックバック・自分や味方によるスタンでは発動しません。すでに妨害無効中の付与でも再発動しません。本体のアンストッパブルが既に受けているスタンも抑制します。
- 保留していた固有品16個とセット1組（3部位）を追加。固有品は計1,190個（一般1,046＋セット品144）、セット48組、連携付き357個。
- 保存形式3・Protocol 9は継続。v1.30.0からの更新で追加のプロフィール初期化はありません。形式3より前からの更新は、下記v1.30.0の移行説明を確認してください。協力では全員同じ版を使ってください。
- 全1,615件の試験と本体参照のReleaseビルドが成功。検証結果は [実装記録](docs/specs/v1.30.1-unbowed-mind.md) に記載。実機での戦闘と協力通信は未検証です。

## v1.30.0 — 記憶の合わせ技と、遠征の選択肢（2026-10-03・Pre）

v1.28〜v1.30の変更をまとめたプレリリースです。自動試験・ビルドは通っていますが、今回の追加分の実機戦闘・協力通信・画面表示は未検証です。

### 更新前に確認

- **保存形式は3です。形式3より前のDreamforgeプロフィールは、旧ファイルとバックアップの写しを残して、新しいプロフィールで開始します。** v1.27.1からの更新はこの対象です。写しの作成に失敗すると保存を止めます。更新前にも `QuickSave/Mods/DreamforgeRPG` を別の場所へバックアップしてください。
- **協力プレイは全員この版へ更新してください。通信はProtocol 9です。**
- ゲームを終了し、ZIP内の `DreamforgeRPG` フォルダをゲームの `Mods` に配置してから起動してください。ライブリロードでの更新は避けてください。

### 追加・変更

- **記憶の合わせ技62個**：星図の橋へ両側の記憶を組み合わせる効果を追加。枝を違う枠どうしが隣り合う順へ変更し、両側の星と記憶の装着を条件にします。印・発動待ち・記憶の再使用短縮などを、レビュー後の定義で実装しました。
- **道標24種**：確保地点で3枚から1枚を選び、次のゾーンだけの決まりを変えます。協力ではホストが選択します。
- **夢の深さ0〜5**：遠征開始時に難度と報酬の段を選べます。
- **属性の反応4種**：蒸気・蝕・燃え殻・氷晶。元の属性スタックは消費せず、反応の追加ダメージから連鎖しません。
- **新しい固有効果43個を有効化**：障壁、立ち位置、撃破、回復、召喚獣などを使った効果。仕掛けの効果11種と、近くの味方の旅人を条件にする絆も追加しました。
- **装備と遠征の内容を拡張**：土台360、固有品1,171（一般1,030＋セット部位141）、セット47種、絆の固有品18個。契約40、今日の夢60、実績90、工房6、夢の出来事26、依頼の種類44。
- **敵側を拡張**：変種30種、悪夢化の性質20種。弱点・耐性と区画の知らせを追加。耐性は30%までに制限します。
- **星図と装備の調整**：つながりで開く星図、パン・ズーム表示、装備の攻撃力・魔力の固定値を追加。条件つき攻撃力・魔力の装備固有効果に覚醒前120%の共通上限を設けました。次の通常攻撃への上乗せは最大の1つだけを消費します。
- 合わせ技の発動IDと初回・終端爆発の判別を接続し、ゾーン移動で古い印や発動待ちを破棄。燃え殻の設計値を火1スタックへ揃え、Husk専用効果の一般装備への混入も修正しました。

### 検証と既知の制限

- Core試験 **1,611件成功、失敗0、スキップ0**。
- Releaseビルド成功。既存のUnity API非推奨警告2件。
- 300人×30遠征の育成・報酬シミュレーション完走。戦闘バランスや協力通信の実測ではありません。
- 「不撓の心」は敵由来ノックバックを確実に判定できないため無効。その効果を使う固有品16個とセット1種は配布に含めていません。
- [Issue #18](https://github.com/Kling0012/shape-of-dreams-rpg-plan/issues/18) を修正：ホスト処理の解除時に星図由来の追加エッセンス枠だけを戻し、他の効果による枠を保持します。満杯・部分失敗・繰り返し解除を、製品の接続層を直接使う試験で検証しました。旧版の後片付けは旧コードで実行されるため、旧版からの初回更新はゲームを終了・再起動してください。
- 実機での戦闘・画面表示・協力時の遅延・W/Eロードアウトは未検証です。

詳細な統合記録は [docs/specs/v1.30-handoff-integration.md](https://github.com/Kling0012/shape-of-dreams-rpg-plan/blob/v1.30.0/docs/specs/v1.30-handoff-integration.md) を参照してください。

---

## v1.27.1 — 回復量・シールド量・召喚獣の力・HP を捧げる量（2026-10-03）

Nachia・Aurena・Cetus など、回復・シールド・召喚獣・HP を捧げる戦い方を支える能力値を足しました。

**新しい能力値**
- 回復量：その旅人が与える回復が増えます（味方への回復も。上限60%）。
- シールド量：その旅人が与えるシールドが増えます（味方へのシールドも。上限60%）。
- 召喚獣の力：その旅人の召喚獣が与えるダメージが増えます（上限80%）。
- 捧げる量の軽減：HP を捧げる技（ゴールデンバースト・還元など）の消費が減ります（上限40%）。本体の固有星座の軽減とも重なります。
- MOD の吸収・魂喰い・護りの灯・溢れる命なども、回復量・シールド量で増えます。

**どこで手に入るか**
- 星図：Nachia（回復・シールド・召喚獣）、Aurena（回復・捧げる量の軽減）、Cetus（シールド）、Vesper と Bismuth（シールド）の記憶ルートで、合っていなかった星を置き換えました。それぞれの記憶が実際に回復・シールド・召喚・HP を捧げる場面に合わせています。
- 装備：アクセサリー・鎧・頭の特性に、回復量とシールド量が付くようになりました。
- 固有品：回復・シールド・仲間・低HP を題材にした固有品を12種足しました（全559種）。

**試験**：全920件。

**実機**：未確認です（Pre）。特に、召喚獣への効き方と、HP を捧げる量が実際に減るかは、ゲームの中で確かめていません。

**実装**：GPT 6 Astra（OMP 経由）。仕様・レビュー・取り込みは Claude。

---

## v1.27.0 — 旅人ごとの大きな星図、夢の圧、限界突破、覚醒の3段（2026-10-03）

大きな更新です。v1.26（記憶・エッセンス・旅人に連携する固有品）も、この版にまとめて入っています（下の v1.26.0 の項目）。

**プロフィールは新しく始まります**
- 大きな変更のため、この版を入れて最初に起動すると、プロフィール（夢のレベル・所持品・素材・星図・図鑑・実績）が新しく始まります。
- 前のデータは消えません。同じフォルダに `profile.v1-archive-日時.json` として残ります。
- 協力プレイでは、全員がこの版を入れてください（通信の版は5です）。

**旅人ごとの大きな星図**
- 星図のポイントは**旅人ごと**になりました。その旅人で敵を倒すと「星の経験」が溜まります（通常1・エリート5・ボス20、悪夢化は2倍、確保20・踏破100）。最大150ポイントで、最後まで取るには長く遊べます。図鑑のボーナスは全員に足されます。
- 星図は「核」（いままでの刻印）と、**記憶ごとのルート**、「夢の輪」からなります。
  - ルートは、各旅人の**アイデンティティ記憶2つ・旅人記憶5つ**それぞれに1本ずつ（Bismuth は固有のアイデンティティが1つなので、共通のアイデンティティ「華麗なる芸術家」のルートを入れて7本）。
  - 1本は星6つ＋頂点の星。前の星に振ると次が開きます。半分ほどは**その記憶を装着しているときだけ**効く星です。
  - 夢の輪：旅人ごとに8つ、各5段の汎用の星です。
  - 全部で63ルート・540星。1人の容量は約220ポイントで、150ポイントでは全部取れません。どのルートを伸ばすかを選んでください。
- ルートは、本体のデータ（アイデンティティ記憶・旅人記憶・旅人ごとの星座）と、コミュニティで強いとされる戦い方を調べて組みました。伸びる値（攻撃力・魔力・最大HP）と発動条件は、その記憶に合わせています。エッセンスや本体の星座で手に入りやすい効果（記憶加速・会心だけ・移動速度など）は減らしました。
- **エッセンスの枠**：アイデンティティ記憶のルートの頂点の先に「記憶の器を広げる」、移動の記憶のルートの頂点の先に「回避の器を広げる」があります（1段・5ポイント）。アイデンティティ記憶と回避に、エッセンスをもう1つはめられます（アイデンティティは2本取っても +1 まで）。
- いままで振っていた刻印は、新しいプロフィールで振り直しになります。

**夢の圧（難易度）**
- 星図が大きくなった分、敵も強くなります。ホストが、ゲームにいる全員の夢のレベルと振った星の数の**平均**から、敵のHPと与ダメージを上げます。
  - 敵のHP：1 + 0.025 ×（夢のレベル − 5、0未満は0）+ 0.005 × 振った星
  - 敵の与ダメージ：1 + 0.012 ×（夢のレベル − 5、0未満は0）+ 0.0025 × 振った星
  - 最大で HP 約2.4倍・与ダメージ約1.7倍。
- 協力プレイでは平均を使うので、人数で圧が増えることはありません。レベルの低い人が入ると圧は下がります。
- 上部の表示に、いまの倍率が出ます。

**限界突破（強化の上限を広げる）**
- 強化が上限に達した遺物は、同じ枠・同じレア度以上の遺物を1つと、調律石・欠片を使って「限界突破」できます。1回ごとに上限が +5 広がります。
  - レア1回（+10）、エピック2回（+15）、伝説3回（+20）。
  - 費用：調律石 5・10・20、欠片 200・400・800。+6 以降の強化は欠片 180〜440（+11〜+15 は1.5倍、+16〜+20 は2倍）。
- +6 以降は、基礎能力と特性が +1 ごとに4%、固有効果が3%伸びます。
- 節目を足しました：+10 と +15 で特性が1行増え、+20（伝説）で固有効果1つが1.2倍になります。
- 鍛冶タブに「限界突破」のボタンと、素材にする遺物の一覧があります。

**覚醒を3段に**
- 覚醒の力 2000・6000・15000 で覚醒Ⅰ・Ⅱ・Ⅲ（固有効果 1.25・1.5・1.8倍、特性 1.1・1.2・1.3倍）。いままでの 500 は1回の遠征で届いてしまうため、長く使い込む目標にしました。
- 連携の値も覚醒で伸びます。表示とホストの値はそろえてあります（記憶加速は覚醒しても90%まで）。

**攻撃力と魔力**（本体の旅人は9人中7人が魔力で伸びるため）
- 烈火・雷鎖・爆砕・旋風・四元の共鳴・回避の残響は、攻撃力か魔力の**高い方**で計算します。爆砕と旋風は、魔力の方が高ければ魔法ダメージになります。
- 処刑・先制は通常攻撃への上乗せなので、攻撃力のままです。
- 逆襲・万全・過負荷は、攻撃力と魔力の両方を上げます。
- 足の装備にも攻撃力・魔力の特性が付き、手の装備のエピックにも技で発動する効果（過負荷・終幕）が付くようになりました。

**属性の重ね方**（本体の上限：闇と光は5、火は上限なし、冷気は重ならない）
- 火種・輝き・影は、値が100を超えると毎回確実に付くようにしました。例：60なら「60%の確率で1つ」、160なら「毎回1つ、さらに60%の確率でもう1つ」。影は会心で当たるともう1つ重なります。霜は確率のままです。
- 装備と刻印の値も、それに合わせて上げました（Husk の「虚ろの刃」は 35 → 100）。

**回避の効果**
- 回避の残響：記憶のクールダウン短縮をやめ、回避した後3秒以内の次の通常攻撃に、攻撃力か魔力の高い方の X% を上乗せします。
- 疾駆：回避した後の3秒間、移動速度に加えて攻撃速度も上がります。見切り：3秒 → 4秒。
- Lacerta の「連装」と Vesper の「四の型」（4発目を早める）は、奥の星・1段まで・4ポイントにしました。

**説明文**
- 重なる効果には「N回まで」「上限なし」「重ならず時間を延長」のどれかを書きました。内部の間隔（吸収0.15秒など）と、障壁の持続時間も書きました。
- 共鳴の説明を実際の動きに合わせました（味方への分は半分）。

**直したこと**
- 烈火：4発目が外れたとき、次の別の攻撃で発動していました。
- 覚醒Ⅱ・Ⅲになったとき、ホストへの能力の送り直しが遅れていました（issue #15）。
- 覚醒後の連携の値が、表示とホストで食い違っていました（issue #14）。

**試験**：全896件。

**実機**：未確認です（Pre）。特に、星図の画面、エッセンスの枠が実際に増えるか、協力プレイでの夢の圧は、ゲームの中で確かめていません。

**実装**：大きな星図と夢の圧は GPT 6 Astra（OMP 経由）、攻撃力と魔力の計算・属性の量・回避の効果・限界突破・エッセンスの枠の仕組みは GLM-5.3（OMP 経由）、調査、星図の見直し、覚醒、装備、説明文、試験、バランス調整は Claude。

---

## v1.26.0 — 記憶・エッセンス・旅人に連携する固有品（2026-10-03、単独では出さず v1.27.0 に含めた）

特定の記憶・エッセンス・旅人と一緒に使うと、追加の効果が出る固有品を121種追加しました（固有品は全547種）。

**連携の条件**（すべて満たしているときに効きます）
- 1つ：記憶、エッセンス、旅人のどれか（記憶26・エッセンス19・旅人9）
- 2つの組み合わせ：記憶×記憶、記憶×エッセンス、エッセンス×エッセンス、旅人×記憶、旅人×エッセンス
- 3つの組み合わせ：旅人×記憶×エッセンス、旅人×旅人記憶×旅人記憶、記憶×記憶×エッセンス、記憶×エッセンス×エッセンス
- レジェンダリー以上の記憶17種・エッセンス19種は、どれも「1つの連携」と「組み合わせの連携」の両方に出てきます。9人の旅人には、それぞれ旅人記憶との組み合わせがあります。

**連携の効果**
- 同調：条件を満たしている間、攻撃力・魔力が上がる
- 守り：条件を満たしている間、最大HPと防御が上がる
- 記憶加速：条件の記憶を使うと、そのクールダウンが早く戻る
- 記憶の余韻：条件の記憶を使った後の5秒間、攻撃力・魔力が上がる（重ならず、時間だけ延びる）
- 条件が多いほど値の上限が大きくなります（2つで1.6倍、3つで2.2倍）。強化では伸びず、覚醒で1.5倍になります。

**画面**：遺物の詳細に、連携の条件と効果を出します。条件ごとに ✓（満たしている）か ・（まだ）が付きます。装備の「現在の強さ」には、いま効いている連携を並べます。

**通信**：装備の情報に連携を加えたので、通信の版を4に上げました。協力プレイでは全員がこの版を入れてください。

**試験**：連携の判定・上限・説明文・送受信・覚醒、全種の網羅を確かめる試験を追加しました（全726件）。

**実機**：未確認です。記憶とエッセンスの判定はゲームの型名で行います。

**実装**：仕組みは GLM-5.3（OMP 経由）、固有品のデータ・説明文・試験の調整は Claude。

---

## v1.25.2 — 実際に遊んだ人の声から（2026-10-02）

v1.20 を遊んだ方のフィードバックをもとに直しました。ありがとうございます。

**直したこと**
- **確保地点で装備を変えられない**：「確保する」を押すと付け替えの時間が終わってしまい、持ち帰ったばかりの遺物を着けられませんでした。確保地点に着いてから、次の戦闘で敵を倒すまでは付け替えられるようにしました（「深く潜る」を選んだときも同じです）。
- **ロビーで「まとめて分解」が押せない**：前の遠征の記録が残っていると、ロビーでも遠征中として扱われていました。工房と狙い系統も同じでした。ゲームに入っていなければ使えるようにしました。
- **潜行で付いた呪いが、確保しても消えない**：悪夢の契約が解けても、その代償の本体の呪いが残っていました（仕様ではありません）。確保して契約が解けたとき、遠征が終わったときに、ホストが契約の呪いを外すようにしました。

**見やすさ**
- 確保などの知らせを、画面の右側（本体の呪いの表示と重なる場所）から、左側の上部表示の下に移しました。文字も大きくし（15→18）、一度に出すのは新しい6件までにしました。
- 上部の表示（HUD）を、Discord のオーバーレイと重ならないよう画面の左上に移しました。

**通信**：契約の呪いを外す知らせを増やしたので、通信の版を3に上げました。協力プレイでは全員がこの版を入れてください。

**試験**：確保地点の付け替えの時間（確保・潜行の後も使える、次の撃破で終わる、保存と読み込み）を確かめる試験を追加しました（全703件）。

**実装**：ルールとホストは GLM-5.3（OMP 経由）、画面の配置は Claude。実装の途中で消えていた「総撃破数を数える処理」は、試験で見つけて戻しました。

---

## v1.25.1 — issue #12・#13 の表示の修正（2026-10-02）

GitHub の issue で報告された、表示の食い違いを直しました。

**直したこと**
- **#12 まとめて分解**：再調律の候補を選んでいる途中の遺物も対象に数えていたため、実際には分解されないのに「欠片を得ました」と多く表示していました。その遺物を対象から外し、表示と実行で同じ判定を使うようにしました。知らせは、実際に分解できた数と、実際に増えた欠片・調律石で出します。
- **#13 遺物一覧の印**：鍵をかけたり外したり、覚醒・強化したりしても、一覧の行（「鍵」や ✦）がすぐに変わらないことがありました。遺物の状態が変わったら、次の描画で一覧を作り直すようにしました。

**試験**：候補選択中の遺物だけのとき・ほかの遺物と混ざっているとき・候補を選び終えた後で、知らせの数と実際の変化が一致することを確かめる試験を追加しました（全698件）。

**実装**：GLM-5.3（OMP 経由）。確認と取り込みは Claude。

---

## v1.25.0 — モンスター側のつり合い・夢の変種・装備をさらに増やす（2026-10-02）

プレイヤー側（6枠・固有品・星図・覚醒・新しい固有効果）が大きく伸びたので、モンスター側も合わせて強く、面白くしました。あわせて装備をさらに増やしています（計画では v1.24 と v1.25 に分けていた内容を、まとめて出します）。

**敵が深度に合わせて強くなる**
- 悪夢化していない敵も、深度1ごとに最大HP +8%・攻撃力 +4% になります（深度3から防御 +10）。ボスは深度4から最大HP +10%/深度です。深く潜るほど、遺物だけでなく手応えも増えます。
- パーティの中でいちばん強い装備に合わせて、悪夢化の確率が最大1.5倍になります。強い装備で潜るほど、悪夢の敵が増えます。

**新しい悪夢の性質（4つ）**
- **結界**：出現時に最大HPの25%の障壁をまとう。
- **棘皮**：受けたダメージの20%を、攻撃した旅人に返す。
- **飢渇**：与えたダメージの15%だけ回復する。
- **破甲**：攻撃が当たると、相手の防御を4秒間 20 下げる。

**夢の変種（13種）**
- 深度2以降、本体の敵がまれに「夢の変種」になって現れます（部屋に1体まで。深度が深いほど出やすい）。名札・色・大きさで見分けられます。
- 例：蝕む猟犬（速く、防御を削る）、鏡殻のスカラベ（強すぎる一撃を受け流す）、焔の殉教者（倒れると爆ぜる）、吸命の学徒（斬るたびに命を吸う）、欠片喰らい（倒すと欠片がたくさん）など。
- 倒すと悪夢と同じく一段上の戦利品が出ます。記録タブに「夢の変種」の一覧と、倒した数を出します。

**新しい固有効果（4種、全42種）**
- **止水**：自分の技で敵をスタンさせると、障壁を得る。
- **散財の護り**：ゴールドを100使うごとに、障壁を得る。
- **見切り**：回避の無敵で攻撃を実際にかわしたとき、攻撃速度が上がる。
- **明晰**：有効な明晰夢（悪い夢）の数だけ、攻撃力・魔力が上がる。

**装備をさらに増やす**
- 土台を各枠 +5（全180種、すべてに絵）。嵐・鴉・墨・氷・陽などの新しい意匠です。
- 固有品を60種追加しました（全426種、一般354・セット品72）。v1.23 の新しい固有効果を多めに使っています。

**試験**：深度の上乗せ・装備の強さの係数・新しい悪夢の性質・夢の変種・新しい固有効果・装備の数を確かめる試験を追加しました。言語の切り替えが並行する試験どうしで干渉していたので、試験は順番に走らせるようにしました（全695件）。

**実機での確認**：深度5の戦闘で、夢の変種（鏡殻のスカラベ）の出現、悪夢の「結界」「棘皮」、固有効果「足枷」「会心の余韻」の発動を確かめました。MOD の処理は1フレームあたり約0.1ミリ秒（157fps）のままです。飢渇・破甲・新しい固有効果4種・変種の色と大きさは、まだ確かめていません（発動すると Player.log に一度だけ出ます）。

**通信**：夢の変種の知らせを増やしたので、通信の版を2に上げました。協力プレイでは、全員がこの版を入れてください。

---

## v1.23.0 — 本体の効果と噛み合う、新しい固有効果（2026-10-02）

本体の仕組みを調べ、これまでの30種がまだ触れていなかった本体の要素（Q・W・Eの使い分け、会心、状態異常、超過回復、エッセンスの品質、ハンター、聖堂、火の燃え広がり）に反応する固有効果を8種追加しました。

**新しい固有効果**
- **終曲**：Q・W・Eを8秒以内にすべて使うと、Ultimate の残りクールダウンが縮む（10秒に1回）。
- **会心の余韻**：通常攻撃が会心で当たると、Q・W・Eのクールダウンが縮む。
- **足枷**：スタン・スロウ・冷気のどれかが乗った敵へのダメージが上がる。本体のスタンやスロウを付ける記憶・エッセンスと組み合わせて。
- **結晶共鳴**：装着中のエッセンスの品質の合計100%ごとに、攻撃力・魔力が上がる（8段まで）。エッセンスの合成が、そのまま強さになります。
- **獲物の誇り**：ハンターの追跡度1ごとに、攻撃力・魔力が上がる（3まで）。追われるほど強くなります。
- **溢れる命**：最大HPを超えた回復の一部が、3秒の障壁になる。
- **祈願**：聖堂を使うたび、そのゾーンの間 攻撃力・魔力が上がる（5回まで）。寄り道が戦いの力になります。
- **飛び火**：火が3つ以上重なった敵に火を付けると、近くの敵にも火が1つ移ることがある。

**アイテム**
- 新しい固有効果を使う固有品を32種（各4種）追加しました。固有品は全366種（一般294・セット品72）です。
- 新しい固有効果は、エピック・レアの候補（枠ごと）にも入り、銘も付きます（終曲の・余韻の・枷の・結晶の・誇り高き・満ちる・祈りの・飛び火の）。

**実機での確認**
- 会心の余韻・溢れる命は、実際の戦闘で発動を確かめました。
- 終曲・足枷・結晶共鳴・獲物の誇り・祈願・飛び火は、発動の条件（Q・W・Eがそろった旅人、スタン、エッセンス、ハンター、聖堂、火の重ね付け）がそろわなかったため、まだ確かめていません。発動すると記録（Player.log）に一度だけ出るようにしてあります。

**試験**：新しい固有効果の時間・回数・上限、能力の送受信、説明文と銘を確かめる試験を追加しました（全647件）。

---

## v1.22.0 — レア度にふさわしい固有効果とセット24種（2026-10-02）

レア度が高い遺物ほど、手に入れたときに「当たり」と分かるようにしました。あわせて、新しい枠を使うセットを増やしました。

**レア度ごとの固有効果**
- **レア**：その枠の固有効果を1つ持つようになりました（範囲の下半分の、弱めの値）。
- **エピック**：固有効果が2つになりました（1つは範囲どおり、もう1つは弱め。同じ固有効果は重なりません）。
- **エピックの銘**：1つ目の固有効果から決まる銘が、名前の前に付きます（例：「猛火の 連なりの剣」「燻る 炎の握り」）。30種の固有効果それぞれに銘があります。これまでに手に入れたエピックにも付きます。
- 伝説（固有品）は、決まった2つの固有効果と言い伝え・覚醒で、いちばん特別なままです。
- 強化+5の節目：レアも固有効果を持つようになったので、+5では特性が1行増えます。
- すでに持っている遺物の固有効果は変わりません（新しく手に入れた物から）。

**セットを24種に**
- 頭・手・足を使うセットを6種、新旧の枠をまたぐセットを6種、計12種（36部位）追加しました。
  - 頭・手・足：凍土の装い（棘・霜）、修羅道の装い（血の渇き・吸命）、風舞の装い（旋風・疾駆）、天穹の誓い（終の昂り・星の加護）、闇討ちの装い（影・先制）、灰走りの装い（火種・追い風）
  - 枠をまたぐ：施療の誓い（護りの灯・共鳴の環）、鉄騎の誓い（棘・守護霊）、月蝕の装い（影・吸魂）、迅雷の武装（雷鎖・乱戦）、万象の装い（四元・過負荷）、払暁の誓い（輝き・万全）
- 固有品は全334種（一般262・セット品72）になりました。
- セットが増えても集めやすいよう、すでに持っているセットの未所持部位の重みを 40→60、セット品の重みを 5→4 にしました。模擬（30回の遠征）で、セットを1つもそろえられない人の割合は 38%（v1.21）→ 34% です。

**試験**：レアとエピックの固有効果の数と範囲、銘、レアの+5の節目、セット24種と新しい枠のセットの完成を確かめる試験を追加しました（全600件）。実機でも、銘の表示とセット品を確かめました。

---

## v1.21.0 — 各枠の装備を大幅に増やす（2026-10-02）

枠が6つに増えたので、どの枠でも選ぶ楽しみがあるよう、装備の種類を大きく増やしました。

**土台**
- すべての枠に土台を10種ずつ足し、各枠25種、全150種にしました（すべてに絵を付けています）。
- 火・冷気・光・闇の属性を伸ばす土台や、系統の偏りが少なくなるように選んでいます。

**固有品**
- 固有品を114種足しました。どの枠にも、セット品を除いて40種以上の固有品があります（全298種、うち一般262・セット品36）。
  - 頭・手・足：各40種（+28）
  - 主装備：51種、防具：47種、装飾品：44種（各+10）
- 新しい土台には、それぞれ1つ以上の固有品があります。
- 同じ枠の中で、固有効果の組み合わせが重ならないようにしました。これまで重なっていた12種は、固有効果を1つ入れ替えています（例：疾風の弓は「先制」と「追い風」、月喰いの鎌は「影」と「血の渇き」）。すでに持っている遺物は変わりません。

**特性（接辞）**
- 枠ごとの特性の種類を、どの枠も13種にそろえました（これまでは防具が8種など、枠によって少なめでした）。足した能力値は、その枠らしくないものなので出にくくしてあります。

**落ち方の調整**
- 固有品が増えてもセットがそろいやすいよう、すでに持っているセットの未所持部位が出る重みを上げました（25→40）。セット品そのものの重みも 3→5 にしました。
- 模擬（30回の遠征）で、セットを1つもそろえられない人の割合は 51%（v1.19）→ 38% になりました。

**画面**
- 遺物の詳細で、固有効果を特性より先に出すようにしました（固有品の個性がすぐ分かるように）。鍛冶の詳細欄も高くして、固有効果まで見えるようにしました。

**試験**：全ての枠が土台25種・固有品40種以上であること、全ての土台に固有品があること、名前・IDの重複がないこと、同じ枠で固有効果の組み合わせが重ならないこと、上限を超えないことを確かめる試験を追加しました（全594件）。

---

## v1.20.0 — 鍛冶を数値だけにしない（2026-10-02）

鍛冶と強化が「数値が少し上がるだけ」にならないよう、それぞれの操作に節目と選択を入れました。

**強化の節目**
- **+3** で、特性が1行増えます（その枠の候補から、まだ持っていない能力値）。
- **+5** で、固有効果を持たない遺物（コモン〜レア）は、その枠の固有効果が1つ宿ります。すでに固有効果を持つ遺物（エピック・固有品）は、特性がもう1行増えます。
- コモンでも+5まで育てれば固有効果を持つので、気に入った土台を使い続けられます。
- 鍛冶の画面に「次の節目」を出し、節目を越えたときは知らせで何が増えたかを伝えます。祈りの泉・鍛冶の祠で強化されたときも同じです。
- これまでに+3・+5にしていた遺物には、起動したときに一度だけ節目を付けます。

**再調律は3つから選ぶ**
- 調律石を払うと、特性の候補が3つ出ます（できるだけ別の能力値）。1つ選ぶか、元のままにできます。いまの値と同じ能力値の候補には ▲▼ を付けます。
- 払った調律石と回数は、選ばなくても戻りません。候補は保存されるので、読み直しても変わりません。
- 候補を選んでいる途中の遺物は、強化・分解・合成の材料にはなりません。

**合成の結果の枠を選べる**
- 鍛冶の絞り込みで枠を選んでいるときは、合成の結果をその枠にできます（欠片1.5倍）。「すべて」のときは、これまでどおり枠は運で、費用も同じです。

**画面**：6枠になって製作の行が増えたため、鍛冶の右の欄をスクロールできるようにしました。

**試験**：強化の節目、以前に強化した遺物への補完、再調律の候補と選択・保存、合成の枠の指定を確かめる試験を追加しました（全590件）。実機でも、次の節目の表示、再調律の3択と選択を確かめました。

---

## v1.19.0 — 装備の枠と種類を増やす（2026-10-02）

装備の枠が3つでは、遺物を集めて組み合わせる楽しみの土台として少なかったので、6枠にしました。

**新しい枠：頭・手・足**
- 装備の枠が「主装備・頭・防具・手・足・装飾品」の6つになりました。
- 新しい枠にそれぞれ15種の土台（計45種）を足し、すべてに絵を付けました。遺物の土台は全90種です。
- 新しい枠の固有品を各12種（計36種）足しました。固有品は全184種（一般148・セット品36）です。これまで付いている固有品が少なかった固有効果（星の加護・過負荷・旋風・乱戦・万全・先制・疾駆・吸魂）を多めに使っています。
- 枠ごとに性格を変えています。頭は魔力・スキル加速・Ultimate・守り、手は攻撃・会心・属性・処刑、足は移動・回避・守りの特性と固有効果が出やすくなります。
- すでに遊んでいる人には、新しい3枠のアンコモンを1つずつ保管庫に入れます（一度だけ）。

**系統のボーナス**
- 同じ系統の遺物を4個・6個そろえたときのボーナスを追加しました（2個・3個はこれまでどおり）。
  - 破壊：4個で会心率+4%、6個で攻撃力・魔力+8%
  - 生命：4個で行動妨害耐性+12、6個で防御+12・最大HP+6%
  - 想像：4個でスキル加速+8、6個で魔力+8%・移動速度+4%

**画面**
- 装備タブの左に6つの枠を並べ、真ん中の枠の切り替えを2段にしました。ボタンには、その枠の遺物の数を添えています。
- 鍛冶の絞り込みも2段になり、製作は6つの枠から選べます。

**落ちる量**
- 枠が倍になると、1つの枠あたりの更新が遅くなりすぎるので、通常の敵・エリートが遺物を落とす確率を約3割上げました（ボスは変わらず）。1回の遠征で見つかる遺物は、模擬で約11.5個から約13.5個になりました。
- 6つの枠すべてを伝説でそろえるのは、これまでより長い目標になります（模擬：30回の遠征で、装備の平均レア度は3枠のとき3.96、6枠で3.59）。

**保存**：これまでの保存はそのまま使えます（主装備・防具・装飾品の装着はそのまま）。

**試験**：6枠の保存と古い保存（3枠）の読み込み、新しい枠の土台・固有品・候補、系統の段、既存の人への一度だけの配布、ドロップが6枠に散ることを確かめる試験を追加しました（全582件）。

---


## v1.18.0 — 星図を広げる（2026-10-02）

既存の要素を見直したところ、旅人の星図が狭すぎました。星図ポイントは夢のレベル30と図鑑で最大33あるのに、旅人の星図には17しか振れず、夢のレベル18あたりで埋まっていました。新しい仕組みは足さずに、星図そのものを広げました。

**奥の星**
- 旅人9人それぞれに、新しい星を6つ追加しました（計54）。手前の星に6ポイント振ると開きます。
- 6つのうち3つは能力値、3つは**固有効果を1段ごとに少しずつ伸ばす星**です。固有品や到達刻印と同じ固有効果を、星図からも育てられます。
  - 例：Mist は旋風・追い風・乱戦、Lacerta は烈火・火種・先制、Bismuth は火種・霜・影（四つの本の属性をそろえる遊び方を後押しします）。
- 旅人の星図は最大35ポイントになり、夢のレベル30まで振り先が残ります。合計の上限はこれまでどおりです。

**画面**
- 星図タブを「手前の星｜奥の星｜到達刻印」の3列にしました。奥の星は、開くまであと何ポイントかを出します。
- 星の段は ●○○ で表し、効果は「（1段ごと）」の値で書きます。固有効果の星は紫で区別します。
- 到達刻印のボタンは、足りない物を短く出します（例：あと：6ポイント・熟練度3（いま0））。

**試験**：奥の星の開く条件、1段ごとの固有効果、上限、保存と読み込み、全旅人が33ポイントを使い切れる広さを確かめる試験を追加しました（全552件）。

**次**：装備の枠と種類を増やします（いまの3枠は土台として少ないため）。

---

## v1.17.0 — 遺物の覚醒（2026-10-02）

固有品（伝説）を使い込むと、目覚めて強くなるようにしました。手に入れて終わりではなく、気に入った1本を育てる楽しみと、次の遠征へ出る理由になります。

**遺物の覚醒**
- 固有品（セット品を含む）を装着した旅人で敵を倒すと、その遺物に「覚醒の力」が溜まります。通常の敵は1、エリートは5、ボスは20で、悪夢化した敵は2倍です。
- 500 溜まると覚醒し、その遺物の固有効果が1.5倍、特性が1.2倍になります（基礎能力はそのまま。合計の上限はこれまでどおり）。目安は遠征2回ほどです。
- 覚醒した遺物は名前の前に「✦」が付きます。遺物の詳細には、覚醒までの進み具合を棒で出します。
- 覚醒の力が溜まっている遺物や覚醒済みの遺物を分解しようとすると、確認の文で「覚醒が失われます」と知らせます。
- 偉業を2つ追加しました：「目覚めの刻」（1つ覚醒させる）、「覚醒の主」（5つ覚醒させる）。偉業は全45個になりました。

**遊びやすさ**
- ロビーでメニューを開くと、ロビーで選んでいる旅人の装備を表示するようにしました（これまでは先頭の旅人が出ていました）。

**試験**：覚醒の力の量、覚醒する瞬間、装着していない物や伝説以外が対象外であること、倍率、保存と読み込み、偉業を確かめる試験を追加しました（全529件）。実機でも、ボスを倒して覚醒し、表示と知らせが出ることを確かめました。

---

## v1.16.1 — 実機で見つけた細部（2026-10-02）

v1.16 を実機の遠征で確かめ、見つけた点を直しました。保管庫に入りきらないときの警告と、出来事の2回押しの確認が正しく動くことも確かめました。

**画面**
- 確保地点の窓の下に大きな余白が出ていたので、中身の高さに合わせて窓の大きさを変えるようにしました。
- 上部の表示の「まだ持ち帰っていない物：…」が幅に収まらず途中で折り返していたので、短い「未確保：」にしました。

**文章**
- 本体の用語を、本体の日本語版に合わせました。Memory→記憶、Essence→エッセンス、Chaos→混沌、Icy Veins→氷の血脈、Exotic Matter→エキゾチック物質、祭壇→聖堂。
- 悪夢の契約の説明に、代償の呪いは本体の呪いと同じもので、契約した人の旅人にだけ付くことを書きました。

**試験**：全504件が通過。

---

## v1.16.0 — 仕上げ その2「押す前に分かる」（2026-10-02）

実機で各タブを見直し、「押してから失敗に気づく」「押したら取り返せない」「何のための数字か分からない」所を直しました。

**直したこと**
- 装備タブで遺物を装着しても、一覧の印が「▲」のまま「★」に変わらなかったのを直しました。
- 遠征中、確保地点でなくても鍛冶で装着中の遺物を分解できてしまったのを直しました。装着中の遺物は、確保地点か遠征の外でだけ分解できます。

**押す前に分かるように**
- 分解：調律石ももらえるときはボタンに出します。鍵をかけた物は「鍵を外すと分解できます」と表示して押せません。装着中の物とエピック以上は、確認の文に名前が入ります。
- 再調律：ボタンに調律石の数を出し、足りないときは押せません（所持数も表示）。特性を選ぶ前は「上の特性を1つ選んでください」、使い切ったら「使い切りました（3/3）」と出ます。
- 確保地点：保管庫に入りきらないときは、何個が欠片になるかを押す前に赤で出します。上の表示は「遺物 n/上限」になりました。
- 遺失物があるときは、上の表示に「遺失物の回収：戦闘部屋 1/3」と進み具合を出します。
- 夢の出来事のボタンを「捧げる」「賭ける」「溶かす」「獏に食べさせる」など、何が起きるか分かる言葉にしました。遺物や欠片を失う出来事（祈りの泉・賭けの杯・錬金の釜・夢喰いの獏）は2回押しで確定します。
- 記録タブの、鞄の遺物をドリームダストに分解するボタンも2回押しで確定します。行にはアイコンとレア度を出します。
- 工房：いまの値と解放後の値を並べます（例：鞄の上限 30 → 35）。説明も文にしました。
- 星図の到達刻印：何が足りないかを書きます（例：このツリーにあと4ポイント・熟練度を3に）。別の到達刻印を選んでいるときは「こちらに付け替える（無料）」と出ます。
- 熟練度の横に、熟練度3で到達刻印を選べることを書きました。
- 偉業：まだ達成していない偉業にも報酬を出します。
- 合成：鍵をかけた物・装着中の物は使わず、弱い順に3つを使うこと、エピック3つからは固有品が生まれることを書きました。製作はアイテムレベルを表示します。

**文章と数値**
- 取引に失敗したとき、「gold」などの内部の言葉ではなく「ゴールドが足りません」などの文で出します。
- 記録タブの「ボスがエピック以上を落とす確率」は、実際には救済（エピックが確定する確率）だけの数字だったので、そのとおりに書き直しました。
- 悪夢化の説明を「通常の敵の○%、エリートの○%（ボスは対象外）」にしました。潜行が上限のときは「これ以上は深くなりません」と出します。
- 今日の夢が切り替わる時刻を、遊んでいる人の時計で出します。
- 「戦闘部屋を3つ」「6ポイント」「+5まで」「25%」などの数字を、ルールの定数から作るようにしました。
- 「買った（未確保）」などを「買いました（まだ持ち帰っていません）」にそろえました。「星見の契約」は「星読みの契約」にしました。
- 遊び方に、鞄に入る数の上限と、あふれたときのことを書きました。
- ようこそと遠征の結果の窓を、文章の量に合わせた高さにしました。

**試験**：遠征中・確保地点・遠征の外での、装着中の遺物の分解を確かめる試験を5件追加しました（全504件）。

**次**：引き続き仕上げ（実機で確保地点・出来事の確認ボタンを確かめる）と、遊びの幅を広げる新しい要素。

---

## v1.15.1 — 説明と効果のずれの修正（2026-10-02）

GitHub の issue #9・#10・#11 で報告された点を直しました（どれも v1.15 の作業の途中で入ったものです）。

**直したこと**
- **#10 夢喰いの獏**：鞄にエピック以上の遺物しかないときにも選べてしまい、何も食べずに出来事だけ消えていたのを直しました。食べられる遺物（エピック未満）があるときだけ選べます。エピック以上を食べない・欠片を保管庫に直接入れる動きは v1.15.0 で入っています。
- **#11 星読みの塔・幸運の星**：説明の数値と効果の数値を、同じ定数から取るようにしました。今後、片方だけ変わることはありません。
- **#9 画像の後片付け**：画像の読み込みに失敗したとき、作りかけの画像を捨てるようにしました（MODを終える・読み直すときに画像を捨てる処理は v1.15.0 で入っています）。

**試験**：夢喰いの獏（対象0〜3個）と星読みの塔の説明と効果の一致を確かめる試験を5件追加しました（全499件）。

---

## v1.15.0 — 仕上げ（2026-10-02）

細かいところを詰めて、遊んでいて引っかかる所を減らしました。

**遊びやすさ**
- 遺物の一覧に印を付けました。「▲」はいま装着している物より強い遺物、「NEW」は新しく手に入れた遺物です（一度選ぶと消えます）。
- 鍛冶タブに「コモンとアンコモンをまとめて分解」を加えました（2回押して確定。鍵をかけた物と装着中の物は使いません）。
- 強化・製作は、素材が足りないときは押せないようにしました。

**画面の不具合**
- メニューを開いている間に、下に隠れた確保地点のボタン（確保する・深く潜る）が押せてしまうことがあったので、メニューを開いている間は確保地点と遠征結果を隠すようにしました。ヒントの下にあるボタンも押せないようにしました。
- ボタンを押した瞬間に表示が変わると、メニューが閉じてしまうことがあったのを直しました。
- 鍛冶・装備の右の欄が画面からはみ出す場合があったので、スクロールできるようにしました。確保地点は画面の中に収まるようにし、遠征結果は内容に合わせて高さを変えます。
- アイコンが無いときや空の装着枠にも枠を描くようにしました。

**表示の誤り**
- 確保地点で、潜行のボーナス（「呪われた財宝」「守銭奴」の2倍）や、今日の夢で増える悪夢化の確率が正しく表示されていなかったのを直しました。記録タブの依頼の報酬も、今日の夢の倍率を反映します。
- 「確保すると潜行は遠征を始めたときの深さに戻ります」は誤りだったので「0に戻ります」に直しました。
- 今日の夢と悪夢の契約の説明は、数値から自動で作るようにしました。説明と実際の効果が食い違うことがなくなります。
- 到達刻印は、固有効果と同じ文で効果を示し、その下に向いている戦い方を書くようにしました。

**ルールの修正**
- 夢喰いの獏：エピック以上は食べず、欠片は分解と同じ量（すぐ保管庫へ、潜行ボーナスの対象外）、調律石は3つごとに1つにしました（稼ぎすぎを防ぐため）。
- 遺物の良し悪しはレア度を最優先で比べるようにしました（アイテムレベルが高いだけのコモンを、レアより良いと扱わない）。
- 錬金の釜で作る遺物のアイテムレベルを、溶かした遺物に合わせました。
- 依頼「レア以上を見つける」が、夢の出来事で手に入れた遺物でも進むようにしました。依頼「固有品を見つける」「セット品を見つける」は出にくくしました（低い潜行では達成が難しいため）。
- 偉業は、合成・製作などの後や、記録タブを開いたときにも達成を判定するようにしました。
- 先制の判定に、攻撃が当たる前のHPを使うようにしました。
- 勇気の門で潜行が変わったら、すぐにホストへ伝えるようにしました。

**数値の調整**
- 星読みの塔は遺物+30%（50%から）、幸運の星は少しだけ（0.6から0.4）にしました。無料で手に入る効果が、代償のある契約より強くなっていたためです。
- 星見の契約に遺物+15%を足し、見えざる重荷のレア度の上がり方を強めました（ほかの契約の下位互換になっていたため）。
- 依頼の夢を、静かな夢と同じ内容から「依頼の報酬×1.5、欠片×1.25」に変えました。
- 中身が同じだった固有品6組の効果を作り分けました。燎原の軍装の2点効果を残火の誓約と違うものにしました。先制の効果の値を下げました。
- 名前の重複を直しました（到達刻印「終わらない舞」→「舞い続ける者」、固有効果「不屈」→「万全」、熟練度の称号「旅人」→「旅慣れ」）。Bismuth の2つ目の到達刻印を、Yubar と同じ効果から「雷鳴の章」（雷鎖）に変えました。

**軽さ**
- メニューの描画で毎回していた並べ替えや計算を、使い回すようにしました。ホストの戦闘処理の無駄なメモリ確保も減らしました。アイコンは起動時に先読みします。

**文章**：通知・ヒント・設定・Workshop の説明文を、自然な文に整えました。

**試験**：仕上げで直したルールの試験を5件追加し、獏と星読みの塔の試験を新しい値に合わせました（全494件）。

---

## v1.14.1 — 分解の返事待ちに遠征が終わったとき（2026-10-02）

**直したこと**
- **#8 分解の返事を待っている間に遠征が終わると、遺物が残ったままダストも付く**
  返事を待っている遺物は、遠征の精算（確保・遺失物への移動・あふれた分の欠片化）から外して「預かり」に入れるようにしました。成功の返事が来たら預かりから取り除き、失敗したときや返事が来ないときは、勝ったなら保管庫、負けたなら遺失物へ戻します。ゲームを再起動したときに預かりに残っている遺物も、同じように戻します。

**確認**：1人プレイで遠征の終了（踏破）が今まで通り動くことを、ゲーム画面で確かめました。協力プレイ（2台）での確認はまだです。

**試験**：遠征の終了と分解の返事が前後する場合の試験を7件追加しました（全489件）。

---

## v1.14.0 — 見た目（2026-10-02）

画面に絵を入れて、遊んでいて楽しい見た目にしました。

**追加**
- **遺物のアイコン 45種**：装備の土台すべてに、手描き風のアイコンを用意しました。系統ごとに差し色が違います（破壊は赤い残り火、生命は緑の光、想像は紫の星屑）。遺物の一覧の各行、装着中の3枠、遺物の詳細（大きく表示）に出ます。
- **夢の出来事の挿絵 12種**：確保地点で出来事が出たとき、説明の左に丸い挿絵が出ます。
- **確保地点に、持ち帰れる遺物を並べて表示**：まだ持ち帰っていない遺物を、良い物から順にアイコンで並べます。何を持ち帰れるのか一目で分かります。

**変更**
- アイコンの枠の色でレア度が分かるようにしました（コモン灰・アンコモン緑・レア青・エピック紫・伝説金）。セット品は橙にして、ほかの伝説と見分けられるようにしました。
- 遺物の一覧の行を少し高くし、アイコンを見やすくしました。

**画像について**：アイコンは Codex の画像生成、挿絵はセルフホストの画像生成（Qwen Image）で、このMODのために新しく作った物です。128×128 に縮めて同梱しています（合計約1.9MB）。アイコンは必要なときに一度だけ読み込むので、動作の重さは変わりません。

**確認**：ゲーム画面で、装備タブの一覧・詳細のアイコン、確保地点の挿絵と持ち帰れる遺物の並びを確かめました。

---

## v1.13.1 — 取引と同期の不具合修正（2026-10-02）

GitHub の issue で報告された不具合を直しました。

**直したこと**
- **#5 取引の返事より先に確保・潜行すると、通貨だけ減る**
  ドリームダストの交換や夢の商人の購入で、ホストの返事を待っている間は、確保・潜行・契約を選べないようにしました（ボタンもキー操作も）。さらに、支払いが確定したら、その間に確保地点や遠征が終わっていても、欠片や遺物を必ず受け取れるようにしました（遠征が終わっていれば遺物は保管庫に入ります）。
- **#6 分解が失敗しても遺物が消える**
  まだ持ち帰っていない遺物を分解するとき、遺物はホストから成功の返事が来たときに初めて取り除くようにしました。失敗したときや送れなかったときは、遺物はそのまま残ります。返事を待っている遺物は、ほかの操作に使えません。30秒たっても返事がなければ、遺物は元に戻ります。
- **#7 ホストだけMODを読み直すと、参加者の装備の効果が戻らない**
  参加者は30秒ごとに装備の内容をホストへ送り直し、ホストは同じ内容なら付け直さずに確認だけ返すようにしました。ホストを読み直しても、30秒以内に全員の効果が戻ります。

**変更**
- MOD名を「Dreamforge」に統一しました。日本語の画面でも「夢鍛」は使わず、「Dreamforge」と書きます。

**確認**：1人プレイで確保・潜行が今まで通り動くことを、ゲーム画面で確かめました。協力プレイ（2台）での確認はまだです。

**試験**：取引の修正を確かめる試験を5件追加し、取引の失敗時の扱いを確かめる既存の試験を新しい動きに合わせました（全482件）。

---

## v1.13.0 — 刻印の選択肢と依頼（2026-10-02）

**旅人の刻印：45 → 63**
9人の旅人それぞれに、小さな強化を1つと、**2つ目の到達刻印**を加えました。到達刻印は今まで通り1つしか有効にできないので、戦い方に合わせてどちらかを選びます（遠征に出ていないときなら、いつでも付け替えられます）。

| 旅人 | これまでの到達刻印 | 新しい到達刻印 |
| --- | --- | --- |
| Vesper | 審問の構え（被弾後に攻撃力アップ） | 鉄壁の審問（周りの敵が多いほど攻撃速度アップ） |
| Lacerta | サラマンダーの火種（火を付ける） | 一番手の銃声（HPの多い敵に上乗せダメージ） |
| Cetus | 凍てつく潮（冷気を付ける） | 渦潮（回避で周りにダメージ） |
| Yubar | 収束する星（Ultimate後に攻撃力・魔力アップ） | 星の盾（Ultimateで障壁） |
| Husk | 虚ろの刃（闇を付ける） | 魂喰い（敵を倒すと回復） |
| Mist | 残像の決闘（回避でクールダウン短縮） | 疾風の歩法（回避後に移動速度アップ） |
| Nachia | 共に在る（味方と共鳴） | 祈りの過負荷（スキル後に魔力アップ） |
| Aurena | 血の献身（HPが減ると攻撃速度アップ） | 満ちた聖杯（HPが多い間は攻撃力アップ） |
| Bismuth | 四つの物語（四元の共鳴） | 終章（Ultimate後に攻撃力・魔力アップ） |

星図タブでは、小さな強化を先に、到達刻印2つを後にまとめて並べ、「どちらか1つを選ぶ」ことを説明に書きました。

**依頼：15種 → 22種**
固有品を見つける・エピック以上を見つける・夢の出来事を選ぶ・確保する・「深く潜る」を選ぶ・セット品を見つける・悪夢の契約を結んだまま確保する、の7種を加えました。

**確認**：ゲーム画面で、星図タブの新しい並びと説明を確かめました。

**試験**：旅人のツリーの構成を確かめる試験を新しい形に合わせ、2つ目の到達刻印と新しい依頼の試験を5件追加しました（全477件）。

---

## v1.12.0 — 新しい固有効果（2026-10-02）

戦闘中の動き方で強くなる固有効果を8種加えました（22種 → 30種）。どれも今ある効果と同じきっかけ（命中・撃破・被弾・スキル・回避）で働きます。

**新しい固有効果**
- **吸魂**：敵を倒すと、最大HPの一部を回復します。
- **旋風**：回避すると、周りの敵にダメージを与えます（2秒に1回）。
- **乱戦**：周りの敵が多いほど、攻撃速度が上がります（5体まで）。
- **先制**：HPが90%以上の敵への通常攻撃のダメージが上がります。
- **星の加護**：Ultimateを使うと、障壁を張ります。
- **疾駆**：回避した後の3秒間、移動速度が上がります。
- **不屈**：HPが80%以上の間、攻撃力が上がります。
- **過負荷**：スキルを使った後の4秒間、魔力が上がります。

エピックの遺物にも、これらの効果が付くことがあります。

**新しい固有品 12種**：魂の灯籠、竜巻の外套、乱闘者の籠手、一番星の弓、星守りの外套、白兎の舞衣、屈せぬ冠、過負荷の核、刈り入れの鎌、嵐舞の太鼓、狂戦士の斧、暁の伝令（一般の固有品は112種、セット部位を含めると148種）。

**今日の夢**：灯の日・疾風の日・血の日・雷の日で、新しい効果も+50%されるようにしました。

**確認**：ゲーム画面で「白兎の舞衣」（疾駆）を装着し、回避すると移動速度+25%が付いて3秒後に外れることを、ホストの処理の記録で確かめました。ほかの7種は、同じ仕組みの試験で動きを確かめています。

**試験**：新しい固有効果の試験を7件追加しました（全472件）。

---

## v1.11.0 — 遊びの幅を広げる（2026-10-02）

遠征中の選択と、長く遊ぶ目標を増やしました。

**夢の出来事：4種 → 12種**
確保地点で出ることがある出来事に、8種を加えました。その場で使える物だけが候補になります。
- **鍛冶の祠**：まだ持ち帰っていない欠片を20払うと、まだ持ち帰っていない遺物のうち一番強い物が+1強化されます。
- **双子の鏡**：欠片を30払うと、一番強い遺物と同じ種類・同じレア度の遺物がもう1つ手に入ります（伝説はエピックになります）。
- **星読みの塔**：次に確保するまで、遺物が50%多く落ちます。
- **錬金の釜**：コモンかアンコモンの遺物を3つ溶かし、1つ上のレア度の遺物を1つ作ります。
- **夢喰いの獏**：まだ持ち帰っていない遺物をすべて食べさせ、欠片と調律石に換えます。
- **勇気の門**：潜行が1段深くなる代わりに、欠片が40増えます。
- **記憶の書庫**：夢のレベルの経験値がもらえます（深く潜っているほど多い）。
- **幸運の星**：次に確保するまで、レア度の高い遺物が出やすくなります。

**偉業（新しい仕組み）：43個**
- 敵を倒した数、遠征・踏破の回数、見つけた固有品、図鑑の埋まり具合、確保した潜行の深さ、悪夢化した敵、夢のレベル、契約・出来事・依頼の回数、そろえたセットの数で、段階的な目標を用意しました。
- 達成すると通知が出て、記録タブの「偉業」で報酬（欠片・調律石）を受け取れます。受け取れる物があるときは、左の夢鍛パネルに件数が出ます。
- 記録タブには、まだの偉業を種類ごとに次の1段だけ、進み具合つきで表示します（例：「敵を合計500体倒す 120/500」）。

**直したこと**
- 夢の商人で伝説を買ったとき、伝説の発見数に数えられていなかったのを直しました。
- 夢の商人はゴールドで支払うので、欠片の量で出来事の候補から外れないようにしました。
- 確保地点の契約のボタンを2行にして、長い説明も最後まで読めるようにしました。

**確認**：ゲーム画面で、確保地点の新しい出来事（幸運の星）、出来事を選んだときの通知、偉業の達成通知、記録タブの偉業の欄、報酬の受け取り（欠片0→30、左のパネルの件数が3→2）、2行になった契約のボタンを確かめました。

**試験**：新しい出来事と偉業の試験を10件追加しました（全465件）。

---

## v1.10.0 — コンテンツを100以上へ（2026-10-02）

固有品を100種（セット部位を含めると136種）まで増やしました。ほかの種類も大きく増やしています。

| 種類 | v1.8 | v1.9 | v1.10 |
| --- | ---: | ---: | ---: |
| 装備の土台 | 18 | 27 | **45** |
| 固有品（セット品を除く） | 22 | 34 | **100** |
| セット（部位） | 4（12） | 6（18） | **12（36）** |
| 固有品の合計 | 34 | 52 | **136** |
| 悪夢の契約 | 8 | 12 | **20** |
| 今日の夢 | 10 | 14 | **30** |

**追加**
- **装備の土台 18種**：狩人の短弓・戦斧・城壁の槍・誓いの戦棍・夢見の杖・星の竪琴（主装備）、狩人の革鎧・燠火の鎧・根の鎖帷子・甲羅の鎧・霧の法衣・祈りの肩掛け（防具）、牙の首飾り・戦太鼓・守護の封印・石の心臓・夢見の水晶・影の仮面（装飾品）。各部位とも、破壊・生命・想像が2種ずつ増えます。
- **固有品 66種**：疾風の弓、首狩りの斧、鳳凰の大剣、星座を奏でる竪琴、霧を歩む者、亀王の甲羅、狼王の牙、虹の水晶、悪夢の仮面など。どれも2つの固有効果の組み合わせで、強さはこれまでの固有品と同じ範囲に収めています。
- **セット 6つ**：《狩猟団の装備》（会心・処刑）、《不落城の装い》（防御・守護霊）、《燎原の軍装》（火・爆砕）、《古森の守り》（回復・灯守）、《夢想の楽団》（共鳴・輝き）、《幻影の一座》（闇・回避の残響）。
- **悪夢の契約 8つ**：血の代価、鉄の誓約、虚ろな王冠、盗人の取引、星見の契約、放浪者の契約、守銭奴、血月の契約。
- **今日の夢 16種**：疾風・血・星・棘・火・月・雷・守り・響きの日と、豊作・悪夢の祭り・宝・修練・職人・静寂の森・混沌の夢。

**調整**
- 固有品が大きく増えて、伝説の候補の中でセット品が出る割合が薄まるので、セット品そのものの重みを2倍から3倍に、持っているセットの足りない部位の重みを10倍から25倍に上げました。シミュレーターでは、30回遊んだときにどれか1つのセットがそろう人は約61%です（v1.9は約52%）。
- 記録タブの図鑑は、見つけた固有品だけを並べ、まだ見つけていない物は件数を1行で表示するようにしました（136行も並ぶと読みにくいため）。

**確認**：新しいセット《幻影の一座》の部位、新しい固有品「虚無の杖」、新しい今日の夢「悪夢の祭り」、図鑑の新しい表示を、ゲーム画面で確認しました。名前・言い伝え・数値は、仕様書の表と自動で照合して全件一致しています。

**試験**：全455件（土台の数を確かめていた試験を新しい数に合わせ、内容量の下限を引き上げ）

---

## v1.9.0 — コンテンツを増やす（2026-10-02）

遊べる内容を大きく増やしました。新しい仕組みは足さず、今ある仕組みの上に装備や契約を足しています。

| 種類 | これまで | v1.9 |
| --- | ---: | ---: |
| 装備の土台 | 18 | 27 |
| 固有品（セット品を除く） | 22 | 34 |
| セット | 4 | 6 |
| 接辞 | 26 | 32 |
| 悪夢の契約 | 8 | 12 |
| 今日の夢 | 10 | 14 |

**追加**
- **装備の土台 9種**：霜穂の槍・黄昏の大鎌・灯火の杖（主装備）、霜の上衣・舞手の衣・星読みの外套（防具）、残り火のロケット・月の鈴・鉄の羽根（装飾品）。破壊・生命・想像の3系統が9種ずつになるようにそろえました。
- **固有品 12種**：氷河の槍、月喰いの鎌、夜明けの灯台、冬の誓い、渦巻く舞衣、天球の外套、不死鳥のロケット、月夜の鈴、鉄翼、嵐を呼ぶ剣、飢える闇、最後の砦。
  効果の強さは、これまでの固有品と同じ範囲に収めています。
- **セット 2つ**
  - 《冬枯れの誓約》（冷気・守り）：2つで冷気の効果と防御が上がり、3つそろうと霜と鉄の輪が付きます。
  - 《星詠みの装束》（スキル・Ultimate）：2つでスキル加速と魔力が上がり、3つそろうと終の昂りと回避の残響が付きます。
- **接辞 6つ**：主装備に「通常攻撃の射程」「冷気の効果」、防具に「スキル加速」「光の効果」、装飾品に「行動妨害耐性」「会心ダメージ」。
- **悪夢の契約 4つ**：暴食、夜の学徒、賭け師の誓い、深淵の眼。
- **今日の夢 4つ**：灯の日、嵐の日、学びの夢、依頼の夢。

**調整**
- 固有品が増えて伝説の候補が薄まるので、持っているセットの足りない部位が選ばれやすくなる倍率を、6倍から10倍に上げました。シミュレーターで30回遊んだ場合、どれか1つのセットがそろう人の割合はほぼ前と同じ（約52%）です。

**確認**：新しいセット部位と固有品が、装備画面で正しい名前・系統・効果の説明つきで表示されることを、ゲーム画面で確認しました。

**試験**：全455件（土台の数を確かめていた試験2件を新しい数に合わせ、内容量の下限を引き上げ）

---

## v1.8.1 — 軽量化の最上段「最大」（2026-10-02）

**追加**
- 軽量化に「最大（Max）」を追加しました。「強め」の内容に加えて、描画解像度を0.75倍に下げます。画面は少しぼやけますが、描画の重さ（GPUの負荷）が大きく減ります。見た目より軽さを優先したいときに選んでください。

**実機での計測**（2026-10-02、1920x1080・垂直同期オフ、戦闘部屋の中でスキルと攻撃を連打）
| 状態 | 1フレームの時間（平均） |
| --- | --- |
| 軽量化なし | 約6.1ms |
| 強め | 約5.6ms（約8%改善） |
| 最大 | 約5.6ms（約8%改善） |
| 軽量化なし・描画解像度2倍（低性能PCの模擬） | 約8.0ms |
| 最大・描画解像度0.75倍 | 約5.6ms（上の行より約30%軽い） |

このPCでは描画より処理（CPU）が先に限界になるため、「強め」と「最大」の差はほとんど出ませんでした。描画解像度を2倍にして描画の負荷を重くすると、フレーム時間は約6.1msから約8.0msまで伸びます。描画が重いPCほど、「最大」で解像度を下げる効果は大きくなります。

**確認用**：`dreamforge_lightweight 0〜3`、`dreamforge_renderscale <倍率>`（描画解像度の変更）を追加しました。どれも開発者向けのコンソールコマンドです。

**実戦での計測**（ゲームが使えるCPUを2コアに絞って弱いPCを模擬し、描画解像度は1倍。戦闘部屋で敵と戦って倒されるまで）
| 状態 | 1フレームの時間（平均） | 引っかかり（最大） |
| --- | --- | --- |
| 軽量化なし（敵9体と戦闘） | 約9.6〜11.4ms | 40〜150ms |
| 強め（敵3体と戦闘、別の部屋） | 約11.4〜13.6ms | 32〜140ms |
| 最大（敵のいない部屋） | 約8.7〜9.1ms | 27〜38ms |

戦闘部屋の中身は毎回ランダムで、敵の数や種類が違います。そのため実戦どうしの比較では、軽量化の段階による差より、部屋ごとの差の方が大きく出ました。実戦での効果は、この計測からは言えません。どの段階でも、弱いCPUの模擬では戦闘中に40ms以上の引っかかりが出ており、本体側の処理（敵の数）が重さの主な原因と考えられます。

---

## v1.8.0 — ゲーム全体を軽くする（2026-10-02）

このMODの処理だけでなく、ゲーム全体を軽くする設定を加えました。どちらもゲームのMOD設定から変更できます。

**追加**
- **ゲームが裏にあるときのFPS上限**（既定は20。0にすると無効）
  Alt+Tab などでゲームが裏に回っている間は、フレーム数を抑えて電力と発熱を減らします。ゲームに戻ると、本体の設定どおりのフレーム数に戻ります。協力プレイのホストの処理が粗くならないよう、20未満にはできません。
- **軽量化**（なし／軽め／強め。既定は「なし」）
  本体の設定画面に無い描画の調整をまとめたものです。
  - 軽め：影の届く距離を6割にし、影の段階を2つまで、追加ライトを2つまでに減らします。遠くの物を早めに粗く描き、キャラのメッシュの焼き込み間隔を0.15秒にします。
  - 強め：さらに影を4割・1段階にし、エフェクトの品質を本体の「低」相当に下げ、アンチエイリアス（MSAA）をオフにし、メッシュの焼き込み間隔を0.2秒にします。
  - 見た目と描画だけを変え、ゲームの進行や協力プレイの同期には触れません。「なし」に戻すと、変える前の値に戻ります。

**実機での計測**（2026-10-02、1920x1080・垂直同期オフ・最初のゾーンで戦闘なし）
| 状態 | 1フレームの時間 |
| --- | --- |
| 軽量化なし | 約5.6ms（約179fps） |
| 軽め | 約5.5ms（約182fps） |
| 強め | 約5.4ms（約184fps） |
| ゲームが裏にあるとき | 50ms（20fps） |

スキル（Q/W/E/R）と通常攻撃を連打してエフェクトを出し続けた状態でも計りました（同じ遠征の中で「なし」と「強め」を交互に4回ずつ、各20〜30秒）。
| 状態 | 1フレームの時間（平均） | 引っかかり（10秒ごとの最大） |
| --- | --- | --- |
| 軽量化なし | 約5.5〜5.9ms | 10〜40ms |
| 強め | 約5.3〜5.6ms | 9〜17ms |

平均は4〜6%速くなり、エフェクトが重なったときの引っかかりが小さくなりました。
性能の低いPCに近づけるため、描画解像度を2倍（画素数4倍）にして描画の負荷を重くした状態でも計りました（スキル連打中）。「なし」約7.0ms → 「強め」約6.7msで、約4〜5%の改善でした。描画の重さが増えても改善の割合はあまり変わらず、このMODの軽量化は「数%軽くし、エフェクトが重なったときの引っかかりを抑える」程度の効果だと分かりました。大きく軽くしたい場合は、本体の設定（影・エフェクト・霧の品質、動的解像度）と組み合わせてください。
高性能なPCで、戦闘をしていない場面だと差は3〜4%ほどでした。エフェクトが多い戦闘中や、性能の低いPCではもっと差が出る見込みですが、まだ確かめていません。ゲームが裏にあるときの上限は、確実に効いています（約180fps→20fps、戻すと元に戻る）。

**調査で分かったこと**：本体はすでに最適化が進んでいて、毎フレームの重い探索はほとんどありません。そのため、本体の処理をキャッシュして軽くする方法は効果が小さいと判断し、見送りました。

**確認用**：コンソールコマンド `dreamforge_lightweight 0|1|2` で軽量化の段階を切り替えられます。

**次**：テストプレイの結果を受けて調整

---

## v1.7.0 — 分かりやすさとバランス（2026-10-02）

**画面と文章**
- 固有効果の説明を、何が・いつ・どれだけ起きるのかが分かる文章にしました。
  例：「火種 30」→「通常攻撃が当たると30%の確率で、敵に火を付ける」
- セットの説明を「2つ装着」「3つ装着」に分けて書き、装着画面には、いま何点そろっていて、あと1つで何が起きるのかを表示するようにしました。
- メニューの各タブの先頭に「ここでできること」を1文で表示するようにしました。
- 確保地点では、「確保する」と「深く潜る」のそれぞれを選ぶと何が起き、何を失う危険があるのかを1文ずつ表示するようにしました。
- 「遊び方」を、このMODの目的 → 1回の遠征の流れ → 確保と潜行の考え方 → 装備の育て方、の順に書き直しました。
- 悪夢の契約は「代償：…。見返り：…。」の形で説明するようにしました。ようこその案内・ヒント・夢の出来事・依頼の報酬の文章も、自然な日本語に整えました。
- メニューを少し高くし、「装着する」ボタンが枠の外に出ないようにしました。図鑑の未発見の項目も読める色にしました。

**バランス**（バランス用シミュレーター `tools/BalanceSim` で、300人が30回ずつ遊んだ場合を模擬して決めました）
- エリートとボスが落とす遺物のレア度の上振れを抑え、伝説の基本確率も少し下げました。初めて伝説を手に入れるまでの回数が、中央値で2回から3回になります。
- 夢のレベルの上がり方を、後半ほどゆっくりにしました。レベル20に届くまでが6回から14回になり、15回ほどで上限の30に届いていたのが、30回遊んでもまだ届かなくなります。
- 鍛冶の強化費用を上げました（+1〜+5の合計が230から335へ）。
- セット品をそろえやすくしました。伝説の抽選ではセット品が2倍選ばれやすくなり、すでに持っているセットのまだ持っていない部位は、さらに6倍選ばれやすくなります。
  合成を使わない場合、30回までにどれか1つのセットがそろう人の割合は4%から55%に上がります。実際には合成でも伝説が手に入るので、もっと早くそろう見込みです。

**ツール**
- バランス用シミュレーター `tools/BalanceSim` を追加しました（遠征の中身は仮定です。詳しくは同じフォルダの README を見てください）。

**試験**：全455件（強化費用の変更に合わせて期待値を1件更新）

**次**：v1.8「ゲーム全体を軽くする」

---

## v1.6.0 — 四元のセット（2026-10-02）

**追加**
- **セットを2つ追加し、本体の4属性すべてに**（合計4セット・各3部位）
  - 《残火の誓約》（火）：2点で火の増幅+10・攻撃力+5%、3点で火種30%・烈火（4発目に攻撃力50%の追加ダメージ）。
  - 《黄昏の狩装》（闇）：2点で闇の増幅+10・会心率+4%、3点で影30%・処刑人（HP30%未満の敵へ攻撃力40%の追加ダメージ）。
  - 既存の《潮鳴りの装い》（冷気）《灯守の誓い》（光）と合わせ、四元の共鳴へ寄せる組み合わせが増えた。

**確認**（2026-10-02 実機）
- 初回起動の流れ：タイトルに「ようこそ、夢鍛へ」→「メニューを開く」で保管庫に初期遺物3つ→遠征開始で空き枠へ自動装備し、ヒントを表示。
- 遠征中の負荷：MOD の Update 約0.012ms・OnGUI 約0.07ms/回、ゲーム全体 110〜135fps。

**内容量**（試験で下限を記録）：基本装備18・固有品34（うちセット12）・セット4・星図15（到達刻印3）・旅人の刻印45・接辞26・固有効果の枠22・悪夢の契約8・悪夢化の接頭6・今日の夢10・夢の出来事5・依頼15種・ヒント12・工房3

**試験**：2件増（全455件）

**次**：テストプレイの結果を受けて調整

---

## v1.5.1 — 重さの根本原因を修正（2026-10-02）

**修正**
- **ロード後に非常に重くなる問題の根本原因**：本体のマネージャー参照 `NetworkedManagerBase<T>.instance` / `ManagerBase<T>.instance` は、そのマネージャーが存在しない場面（タイトル・ロビーなど）で毎回 `FindObjectOfType` を2回実行する。MODが毎フレーム（Update と OnGUI の両方）でこれを呼んでいたため、フレームごとに全オブジェクト走査が発生していた。全箇所を `softInstance`（走査しない参照）に置き換えた。

**実機計測**（`perf.flag` 有効、サブディスプレイ 1920x1080）
| 場面 | 修正前 | 修正後 |
| --- | --- | --- |
| MODの Update 平均 | 約38ms | 約0.004ms |
| MODの OnGUI 平均（1回） | 約16ms | 約0.006ms（タイトルでも最大0.64ms） |
| ゲーム全体 | 約13fps | 約131〜137fps（約7.4ms/フレーム） |

**次**：テストプレイの結果を受けて調整

---

## v1.5.0 — 経済の橋渡しと仕上げ（2026-10-02）

**追加**
- **本体の通貨での取引**：通貨を動かすのはホストだけ。クライアントが依頼し、ホストが残高を確かめて支払い・受け取りを行い、成功したときだけ遺物側を確定する（応答の重複や失敗では確定しない）。
  - 夢の商人は**ゴールド払い**（本体の難易度補正 `GetAdjustedGoldAmount_Cost` を通した価格）。
  - 確保地点で**ドリームダスト100→欠片10**（1回に最大1000まで）。
  - 遠征中、**未確保の遺物を分解するとドリームダスト**（記録タブ）。
- **HUD の表示量**：MOD設定で「詳しく／簡潔／なし」。

**変更**
- **夢の工房の縮小**：遺失物の灯・残響の増幅・契約の目利きを廃止。解放済みなら読み込み時に費用（欠片・調律石）を返す。大きな鞄・広い保管庫・依頼の引き直しは残す。
- `dreamforge_perf` にゲーム全体のフレーム時間（FPS）を追加。

**試験**：4件増（全453件）

**次**：テストプレイの結果を受けて調整

---

## v1.4.0 — 実際に遊べる形へ（2026-10-02）

目標：実際に遊ぶことを考えた最適化・初めての動線・ボリューム。

**最適化**（「ロードすると重い」への対処）
- HUD：毎フレーム GUILayout で十数個の文字列を組み立てていたのをやめ、0.25秒ごとに作り直した1枚のラベルとして描く（毎フレームの文字列生成とGCを削減）。
- メニュー・確保地点・結果・ヒントのパネルが無いときは、IMGUI のレイアウト処理（`useGUILayout`）自体を止める。ラベルは描画イベントでだけ描く。
- 通知と悪夢の名札は、大きさの計測を1回だけにしてキャッシュ。メニューの強さの集計は1フレームに1回。
- 保存を別スレッドへ：JSON化だけメインで行い、ディスクへの書き込み・読み戻し・置換は作業スレッドで最新の1件だけ。定期保存の間隔を10秒→30秒。
- 拾得ごとのログ出力をやめた。ホスト：出現待ち行列に寿命、能力の再計算は値が変わったときだけ、呪いの候補をキャッシュ。
- `dreamforge_perf`：このMODの Update / OnGUI の平均・最大時間を表示。
- 性能の試験：撃破1回 0.1ms 未満、強さの集計 0.2ms 未満、保存のJSON化 15ms 未満を自動試験で確認。

**初めての動線**
- 初回起動で「ようこそ、夢鍛へ」の案内（メニューを開くボタン付き）と、初期装備（各枠1つずつアンコモン）。最初の遠征で、何も装備していない旅人に自動で装備する。
- 場面別ヒント12種（初めての拾得・確保地点・確保・潜行・全滅・刻印ポイント・到達刻印・悪夢・依頼・鍛冶など）。一度だけ表示、「今後ヒントを出さない」、記録タブから再表示。

**ボリューム**
- 本体の旅人9人それぞれの専用固有品（固有品は全22種＋セット部位6）。
- 今日の夢「影の日」「霜の日」（全10種）、依頼「悪夢の契約を結ぶ」（全15種）。

**試験**：14件追加（全449件）

**次**：v1.5 経済の橋渡しと仕上げ

---

## v1.3.0 — 呪いの契約と系統の統一（2026-10-01）

[シナジー再評価](docs/dreamforge-synergy-review.md) の第3弾。

**変更**
- **悪夢の契約の代償＝本体の呪い**：契約を結ぶと、本体の呪い（Hatred の祭壇と同じ `CurseStatusEffect`）がランダムに1つ自分のキャラに付く。強度は契約に応じて弱・中・強。呪いの効果と解除の依頼は本体のものがそのまま動く。協力時は契約した人のキャラだけ。
  - MOD独自の能力値の代償（最大HP減・防御減など）は廃止。見返りは今までどおり次の確保まで。
- **系統を本体の星座系統に統一**：攻勢・守勢・共鳴 → 破壊・生命・想像（内部IDは同じなので保存はそのまま）。
- **セット遺物のテーマ**：《潮鳴りの装い》＝冷気・回避（2点：冷気増幅＋移動、3点：霜＋回避の残響）、《灯守の誓い》＝光・回復（2点：光増幅＋最大HP、3点：輝き＋灯守）。

**試験**：契約・セットの試験を更新（全435件）

**次**：v1.4 実際に遊べる形へ

---

## v1.2.0 — 旅人の刻印（2026-10-01）

[シナジー再評価](docs/dreamforge-synergy-review.md) の第2弾。本体の星座と重なっていた汎用の星図を、旅人ごとの刻印へ置き換えた。

**変更**
- **旅人の刻印**：本体の旅人9人それぞれに、キットに効く刻印ツリー（小ノード4つ×3段＋到達刻印1つ）。
  - Vesper（四の型・炎・不動・光／審問の構え）、Lacerta（連装・火薬・長銃身・速射／サラマンダーの火種）、Cetus（冷気・氷の殻・潮の呼吸・不屈／凍てつく潮）、Yubar（星の加速・星明かり・宇宙の理・遠い星／収束する星）、空殻（深い影・急所・影走り・暗殺術／虚ろの刃）、Mist（剣舞・見切り・一閃・決闘者／残像の決闘）、Nachia（祝福の光・絆の力・呼び声・守護の誓い／共に在る）、Aurena（生命の器・黄金の光・再生・献身／血の献身）、Bismuth（炎・光・闇の章・物語の続き／四つの物語）
  - 本体以外の旅人（他MODのキャラ）は従来の汎用ツリー。
- **新しい能力値**：通常攻撃の射程（本体の `attackRangePercentage`）、4発目の位置（本体の `everyFourAttackStartIndexFlat`、上限2）。
- **熟練度**：汎用の能力%（攻撃力・魔力・最大HP+1%/段）を廃止。熟練度3で到達刻印を解放する条件に変更。
- **保存の移行**：本体の旅人に振っていた汎用ノードのポイントは読み込み時に自動で戻る。

**試験**：6件追加（全435件）

**次**：v1.3 呪いの契約と系統の統一

---

## v1.1.0 — 本体のエリートと Limbo（2026-10-01）

[シナジー再評価](docs/dreamforge-synergy-review.md) の第1弾。本体と並走していた要素を本体の仕組みへ乗せ替えた。

**変更**
- **悪夢化＝本体のエリート**：悪夢化した敵に本体のエリート効果（MirageSkin：見た目と専用攻撃）を付ける。接頭が2つ以上の深い悪夢は上位の tier も候補。本体がすでにエリート化した敵には重ねない。MOD側の体力上乗せは +80% → +40% に減らした。
- **開始深度を廃止し、本体の Limbo に統合**：Limbo 深度1ごとに遺物ドロップ率+10%・レア度上昇。古い保存の開始深度は無視する。
- **「夢の深度」を「潜行」に改名**（本体の Limbo 深度と区別）。代償を防御・最大HPの減少から、本体の処理（`takenDamageProcessor`）による**被ダメージ増加（+6%/段）**へ。

**試験**：開始深度の試験を Limbo・潜行の試験に置き換え（全429件）

**次**：v1.2 旅人の刻印

---

## v1.0.0 — 本体とのシナジー（2026-10-01）

方針の改定：Dreamforge は Shape of Dreams の **MOD** であり、本体の仕組みに乗ってそれを強めるものを優先する。

**追加**
- **属性の遺物**：特性に「火・冷気・光・闇の効果増幅」（本体の `ApplyElemental` が増幅値として使う）。
- **属性を付与する固有効果**：火種・霜・輝き・影（通常攻撃の命中時に確率で本体の属性を1スタック）。Lacerta（火）・Cetus（冷気）・Yubar/Aurena/Nachia（光）・空殻（闇）のキットと噛み合う。光3重の確定会心も本体どおり働く。
- **四元の共鳴**：敵に火・冷気・光・闇が揃った瞬間に爆発（同じ敵へ6秒に1回）。
- **回避の残響**：回避（Movement）のたびに Q/W/E のクールダウン短縮。**終の昂り**：Ultimate（R）の後5秒 攻撃力・魔力上昇。
- **本体の行動を依頼に**：Chaos の祭壇、商人での購入、Memory/Essence の強化・合成・分解、ハンターの領域へ踏み込む。
- 固有品：四元の時計・残像の外套・暁を呼ぶ杖。

**変更**
- **烈火**を本体の4発目（`isThisAttackFourthAttack`）で発動するよう変更。Vesper・Lacerta の4発目キットや 4発目の位置を進める効果と連動する。
- 今日の夢「烈火の日」「共鳴の日」が属性の固有効果も強化。

**試験**：18件追加（全430件）

**次**：v1.1 本体のエリートと Limbo

---

## v0.9.0 — 夢の出来事とセット遺物（2026-10-01）

**追加**
- **夢の出来事**（Slay the Spire の「？」部屋に近い）：確保地点に50%の確率で出来事が1つ現れ、使うかどうかを選べる。
  - 夢の商人：欠片で正体不明の遺物を買う（アンコモン以上、深いほど良い）
  - 祈りの泉：未確保の最も弱い遺物を捧げ、最も強い未確保の遺物を+1強化
  - 賭けの杯：未確保の欠片を賭ける（50%で2倍、外れで失う）
  - 迷い人の灯：遺失物を1つその場で取り戻す（遺失物があるときだけ）
- **セット遺物**（Diablo のセット装備）：《潮鳴りの装い》（攻撃速度・移動、3点で連撃＋追い風）と《灯守の誓い》（最大HP・スキル加速、3点で護りの灯＋共鳴）。各3部位。固有品として落ち、装備画面に進み具合を表示。

**試験**：11件追加（全412件）

**次**：v1.0 本体とのシナジー

---

## v0.8.0 — 夢の工房（2026-10-01）

**追加**
- **夢の工房**（Hades の「鏡」に近い）：メニューの「工房」タブで、欠片と調律石を払ってアカウント共通の恒久強化を解放する。強さそのものは上げず、遊びやすさと選択肢を増やす。
  - 大きな鞄（未確保の鞄 +5、3段階）／広い保管庫（+20、3段階）／依頼の引き直し（遠征ごと +1回、2段階）
  - 遺失物の灯（回収に必要な戦闘部屋 3→2）／残響の増幅（全滅時に持ち帰る欠片 25%→45%、2段階）／契約の目利き（契約の選択肢 3→4）
- **依頼の引き直し**：記録タブから、未達成の依頼を別の種類に引き直せる（工房の段階に応じた回数）。

**試験**：9件追加（全401件）

**次**：v0.9 夢の出来事とセット遺物

---

## v0.7.0 — 深淵と新たな力（2026-10-01）

**追加**
- **開始深度（深淵の段階）**：これまでに確保できた最高深度から遠征を始められる。確保すると深度0ではなく開始深度へ戻る。踏破したときの最も深い開始深度を記録。
- **新しい固有効果 4種**：雷鎖（命中時25%で近くの敵2体へ魔法ダメージ）・爆砕（撃破時に周囲へダメージ）・守護霊（大きな一撃で障壁、20秒に1回）・血の渇き（HP50%未満で攻撃速度上昇）。エピックの抽選先にも追加。
- **新しい固有品 4種**：雷鳴の双牙・砕けた星核・守護霊の衣・血塗れの大槌（固有品は全10種）。

**修正**（Codexレビュー3回目）
- 悪夢化の確率を、遠征開始日の「今日の夢」で計算するよう変更（日付をまたいでも変わらない）。
- 悪夢化した敵を5秒ごとに全員へ送り直す（途中参加・取りこぼし対策）。まだ出現していない敵の情報を10秒保持。
- 誰の能力情報も届いていない間に出現した敵は、悪夢化の抽選を待つ。
- 熟練度が上がったときの能力の再送をキャラごとに判定。
- 依頼達成の通知に「静かな夢」の倍率後の報酬を表示。合成ボタンは欠片の不足も確認。

**試験**：11件追加（全392件）

**次**：v0.8 夢の工房

---

## v0.6.0 — 今日の夢と熟練度（2026-10-01）

**追加**
- **今日の夢**：日付で決まる8種の変化（烈火の日・鋼の日・共鳴の日・黄金の夢・豊穣の夢・悪夢の夜・星降る夢・静かな夢）。遠征開始日の夢がその遠征中ずっと効く。HUD と記録に表示。
- **旅人の熟練度**：キャラごとの撃破数で熟練度0〜10と称号（新参〜伝説）。レベルごとに攻撃力・魔力・最大HP+1%。

**試験**：17件追加（全381件）

---

## v0.5.0 — 悪夢化エリート（2026-10-01）

**追加**
- **悪夢化エリート**（Diablo のチャンピオンに相当）：パーティの最大深度に応じて、ホストが敵の一部を悪夢化する。HP+80%に加え、鋼殻・狂暴・巨躯・疾風・再生・魔力から1〜3つ（深度3で2つ、5で3つ）。頭上に名札が出て、倒すと1段上の格の戦利品（通常→エリート相当、エリート→ボス相当）。ボスは対象外。
- 依頼「悪夢化した敵を倒す」、記録に討伐数。

**修正**（Codexレビュー2回目）
- 結果の開始レベルをラン開始時の値に。保管庫あふれで欠片化した遺物は確保数・収集依頼に数えない。確保中に得た依頼の欠片を確保欠片に含める。契約の説明を「撃破で得る」に限定。中断したランの精算結果も表示。狙い系統ボタンの有効条件をルールと一致。

**試験**：19件追加（全364件）

---

## v0.4.0 — 悪夢の契約（2026-10-01）

**追加**
- **悪夢の契約**（Hades の「罰の契約」に近い）：深く潜るとき、提示された3つから1つを結べる。硝子の心臓・鈍き刃・無防備・重い足・狂乱・呪われた財宝・乾いた夢・見えざる重荷の8種。デメリットと見返りが次の確保まで重なって効く。
- **合成**：鍵なし・未装着の同じレア度3つから1つ上のレア度を1つ（エピック3つ→固有品）。
- **図鑑の節目**：遺物の種類を6種集めるごとに星図ポイント+1（最大+4）。

**試験**：15件追加（全345件）

---

## v0.3.0 — 依頼（2026-10-01）

**追加**
- **依頼**：遠征ごとに小目標が3つ（撃破数・エリート・ボス・確保数・深度・レア発見・部屋突破）。達成すると欠片・調律石（未確保として鞄へ）と経験値。HUD・記録・結果パネルに表示。

**試験**：10件追加（全330件）

---

## v0.2.0 — 狙い系統とセット効果（2026-10-01）

**追加**
- **狙い系統**：攻勢・守勢・共鳴のどれかを選ぶと、その系統の遺物が2倍出やすい。
- **系統セット**：同じ系統の遺物2つ・3つでボーナス。
- **遠征結果パネル**：撃破・発見・確保・遺失・最高深度・レベル変化。

**修正**（Codexレビュー1回目・実機確認）
- 「未知の結末」エンディングを勝利扱いに。報酬なしの敵（演出・召喚・ハンターの追加敵）を除外。最初に突破した戦闘部屋を数え損ねる不具合。遺失物の回収で鞄の上限を超える不具合。ライブリロード時にUIのフォント・画像が残る不具合。ホストでプロトコル版を検証。
- メニューのクリックがゲームのUIへ貫通する不具合、開始直後の最初のゾーンでも確保地点が出る不具合、通知の文字切れ。

**試験**：12件追加（全320件）

---

## v0.1.0 — 最初に遊べる版（2026-10-01）

**追加**
- **遺物**：敵を倒すと各プレイヤーに個別の装備が落ちる。主装備・防具・装飾品の3枠、レア度5段階、基礎18種、固有品6種、固有効果11種。
- **確保と夢の深度**：拾った物はまず未確保。新しいゾーンで「確保する」か「深く潜る」か（ドロップ率・レア度が上がり、守りが下がる）。
- **遺失物**：全滅すると未確保の遺物は遺失物へ、欠片は25%だけ持ち帰る。次の遠征で戦闘部屋3つ突破で1つ取り戻す。
- **夢のレベルと星図**：キャラごとに攻勢・守勢・共鳴へポイントを配分、6ptで刻印。
- **鍛冶**：強化（+5まで）・再調律・分解・製作。
- **協力プレイ**：能力の反映はホスト、抽選と保存は各自。
- 日本語/英語のUI（F6 メニュー、F7 確保、F8 深く潜る）、Workshop 登録用の about 一式。

**試験**：58件追加（全308件）。実機（r.1.4.0.13_s）で読み込み・UI・能力反映・ライブリロードを確認。
