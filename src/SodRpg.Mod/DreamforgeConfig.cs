using System;
using SodRpg.Core.Game;
using UnityEngine.InputSystem;

namespace SodRpg.Mod
{
    /// <summary>ゲーム内のMOD設定画面に出る設定。保存はゲーム側（QuickSave/Mods）が行う。</summary>
    public class DreamforgeConfig : ModConfig
    {
        [LabelText("Menu key / メニューを開くキー")]
        public Key menuKey = Key.F6;

        [LabelText("Secure key / 確保するキー")]
        public Key secureKey = Key.F7;

        [LabelText("Delve key / 深く潜るキー")]
        public Key delveKey = Key.F8;

        [LabelText("Secure point panel key / 確保地点の画面を隠す・出すキー")]
        public Key securePanelKey = Key.F9;

        // 外部言語が未指定・無効なら、chinese=true → 中国語 / japanese=true → 日本語 / 両方false → 英語。
        // chinese が japanese より優先する。旧版のMODはこの項目を無視して
        // japanese（中国語選択時は false ＝ 英語）へ戻るため、旧版でも安全に既定の言語で動く。
        // 壊れた保存では両方既定値（true/false ＝ 日本語）に戻り、未知の言語値は生じない。
        [LabelText("UI language: Japanese / 表示言語：日本語")]
        [Description("Used when no loaded external language is selected. Chinese (Simplified) takes priority over Japanese; uncheck both for English. / "
            + "読み込めた外部言語が選ばれていないときに使います。簡体中文は日本語より優先され、両方外すと英語になります。 / "
            + "未选择已加载的外部语言时使用。简体中文优先于日语；两项都不选则使用英语。")]
        public bool japanese = true;

        [LabelText("UI language: Chinese, Simplified / 表示言語：简体中文（优先于日语 / 日本語より優先）")]
        [Description("Check to show Simplified Chinese (untranslated text falls back to English). Takes priority over Japanese. / "
            + "チェックすると簡体字中国語で表示します（未訳の部分は英語）。日本語より優先されます。")]
        public bool chinese = false;

        [LabelText("External language code / 外部言語のコード / 外部语言代码")]
        public string language = "";

        [LabelText("Write missing translations / 未訳の文を書き出す / 导出未翻译文本")]
        public bool translationDump = false;

        /// <summary>読み込めた外部言語を優先し、見つからなければ従来の設定を使う。</summary>
        public LocLanguage ResolveLanguage(Func<string, bool> selectExternal = null) =>
            selectExternal?.Invoke(language) == true ? LocLanguage.External
                : chinese ? LocLanguage.Chinese : japanese ? LocLanguage.Japanese : LocLanguage.English;

        /// <summary>自動分解の設定から分解してよいレア度の境界（この値未満）を返す。
        /// 「しない」と壊れた保存値（範囲外の整数）では null に戻す（ResolveNameplates と同じ防御）。</summary>
        public Rarity? AutoSalvageBelow() =>
            autoSalvageRarity >= AutoSalvageRarity.BelowUncommon && autoSalvageRarity <= AutoSalvageRarity.BelowLegendary
                ? (Rarity)autoSalvageRarity
                : (Rarity?)null;

        [LabelText("UI scale / 表示の大きさ")]
        public float uiScale = 1f;

        [LabelText("Show drops below Rare / レア未満の拾得通知")]
        public bool showToasts = true;

        // #1010: 拾った時点で自動分解するレア度。列挙の値は Rarity と同じ番号で、「そのレア度未満」を意味する。
        // 固有品（セット装備を含む）はレア度に関わらず対象外。既定は「しない」。
        [LabelText("Auto salvage drops below a rarity / 自動分解するレア度")]
        [Description("Relics below the chosen rarity are salvaged for Dream Dust when picked up. Uniques and set gear are never auto-salvaged. / "
            + "選んだレア度未満の遺物を拾った時点でドリームダストに分解します。固有品とセット装備は自動分解しません。")]
        public AutoSalvageRarity autoSalvageRarity = AutoSalvageRarity.None;

        // #1010: 画面左の通知の最大行数。0のときはドロップと通常の案内を消し、レベルアップ・確保・警告など大事な通知だけ最新の1行を出す。
        [LabelText("Toast lines on the left (0-7) / 画面左の通知の行数（0〜7）")]
        [UnityEngine.Range(0, 7)]
        public int toastMaxLines = 7;

        [LabelText("Overflow relics: shards + Dream Dust /\n鞄からあふれた遺物で、欠片に加えてドリームダストも受け取る")]
        public bool overflowDreamDust = false;

        [LabelText("Room Omens: a blessing or trial in each combat room (the host's setting applies) /\n部屋の兆し：戦闘の部屋ごとに恵みか試練を付ける（ホストの設定が使われる）")]
        public bool roomOmens = true;

        [LabelText("Co-op-only Room Omens (host) / マルチ専用の兆し（ホスト）")]
        public bool coopRoomOmens = true;

        [LabelText("Melee: convert equipment attack range to attack speed (host) / 近接：装備の射程を攻撃速度へ変換（ホスト）")]
        [Description("1% equipment basic attack range gives " + CoopBalance.RangeToSpeedMultiplierText + "% attack speed instead; total attack speed is capped at " + TravelerCombat.AttackSpeedCapText + "%. / "
            + "装備の通常攻撃射程1%につき攻撃速度が" + CoopBalance.RangeToSpeedMultiplierText + "%上がり、攻撃速度は合計" + TravelerCombat.AttackSpeedCapText + "%まで。")]
        public bool meleeRangeToSpeed = true;

        [LabelText("Enemy ownership (coop, host) / 担当制（マルチ・ホスト）")]
        public bool coopEnemyOwnership = true;

        [LabelText("Difficulty follows clear speed (host) / 殲滅速度に合わせた難易度（ホスト）")]
        public bool dynamicDifficulty = true;

        [LabelText("Also use in solo (host) / ソロでも使う（ホスト）")]
        public bool dynamicDifficultySolo = false;

        [LabelText("Show enemy ownership marks (client) / 担当の印を表示（クライアント）")]
        public bool showEnemyOwnershipMarks = true;

        // 敵の名札の表示方法（#1009）。各プレイヤーの見た目だけの設定で、通信には乗せない。
        // 担当の印（showEnemyOwnershipMarks）と協力プレイの守りの印は、この設定にかかわらずそれぞれの設定に従う。
        [LabelText("Enemy nameplates: names / danger marks / hidden (client) /\n敵の名札：名前・印だけ・表示しない（クライアント）")]
        [Description("Names shows the current nameplates. Marks only replaces them with * / ** / *** by danger (variant, nightmare, 3+ affixes or Nemesis). Hidden removes them. Ownership and co-op ward marks keep their own settings. / "
            + "名前は今の名札を表示します。印だけは危険度で *・**・***（変種、悪夢、性質3個以上か宿敵）を出します。表示しないは消します。担当の印と守りの印はそれぞれの設定に従います。")]
        public EnemyNameplateMode enemyNameplates = EnemyNameplateMode.Name;

        /// <summary>名札の表示方法。壊れた保存値は既定（名前）に戻す。</summary>
        public EnemyNameplateMode ResolveNameplates() =>
            enemyNameplates == EnemyNameplateMode.Marks || enemyNameplates == EnemyNameplateMode.Hidden
                ? enemyNameplates : EnemyNameplateMode.Name;

        [LabelText("Coop: distance falloff and grace (host) / 味方距離効果の減衰と猶予（ホスト）")]
        [Description("Falloff from " + CoopBalance.FullRangeText + " to " + CoopBalance.OuterRangeText + "m with " + CoopBalance.GraceSecondsText + "s grace; when off, the effect ends beyond " + CoopBalance.FullRangeText + "m. / "
            + CoopBalance.FullRangeText + "～" + CoopBalance.OuterRangeText + "mで減衰し、猶予は" + CoopBalance.GraceSecondsText + "秒。オフでは" + CoopBalance.FullRangeText + "mを超えると切れる。")]
        public bool coopDistanceFalloff = true;

        [LabelText("Melee: Break for allied damage (host) / 近接：崩しで味方の与ダメージを増加（ホスト）")]
        [Description("Other allies deal +" + CoopBalance.BreakDamagePercentText + "% damage to an enemy hit by the melee traveler for " + CoopBalance.BreakSecondsText + "s. / "
            + "近接の旅人が当てた敵へ" + CoopBalance.BreakSecondsText + "秒間、ほかの味方の与ダメージ+" + CoopBalance.BreakDamagePercentText + "%。")]
        public bool meleeBreak = true;

        [LabelText("Melee: damage reduction near enemies (host) / 近接：敵の近くで被ダメージ軽減（ホスト）")]
        [Description("Take " + CoopBalance.MeleeGuardReductionPercentText + "% less damage while an enemy is within " + CoopBalance.MeleeGuardRangeText + "m. / "
            + CoopBalance.MeleeGuardRangeText + "m以内に敵がいる間、被ダメージ" + CoopBalance.MeleeGuardReductionPercentText + "%軽減。")]
        public bool meleeProximityGuard = true;

        [LabelText("Auto casting: native speed in multiplayer (host, default ON) / 自動詠唱：マルチでは本体と同じ速さ（ホスト、既定ON）")]
        public bool autoCastCoopNativeCadence = true;

        [LabelText("HUD / 左のパネルの表示")]
        public HudMode hudMode = HudMode.Full;

        [LabelText("Background FPS cap (0 = off, 20-60) / ゲームが裏にあるときのFPS上限（0で無効、20〜60）")]
        [UnityEngine.Range(0, 60)]
        public int backgroundFps = 20;

        [LabelText("Lighter rendering / 描画の軽量化（Off：なし／Light：軽め／Strong：強め／Max：最大）")]
        public LightweightMode lightweight = LightweightMode.Off;

        [LabelText("Skip unchanged network state / 変わっていない通信を省く")]
        public bool netSkipUnchanged = true;

        [LabelText("Batch frequent network updates / 頻繁な通信をまとめる")]
        public bool netCoalesce = true;

        [LabelText("Send personal replies only to their owner / 個人への返事は本人にだけ送る")]
        public bool netOwnerOnly = true;

        [LabelText("Status effect duration sync: 0.5 seconds (host) / 状態効果の残り時間を0.5秒間隔で同期（ホスト）")]
        public bool nativeStatusDurationSync = true;

        [LabelText("Gem cooldown sync: 0.5 seconds (host) / 宝石のクールダウンを0.5秒間隔で同期（ホスト）")]
        public bool nativeGemCooldownSync = true;

        [LabelText("Skip unused weapon FX work / 結果に影響しない武器FXの処理を省く")]
        public bool nativeWeaponFxScan = true;

        [LabelText("Skip unchanged obstacle avoidance settings / 変わっていない回避設定を省く")]
        public bool nativeObstacleAvoidance = true;

        [LabelText("Reduce aggro propagation allocations / 敵への標的の伝播でメモリ確保を減らす")]
        public bool nativeAggroPropagation = true;

        [LabelText("Reuse enemy counts during group spawning / 群れの生成中に担当する敵の数を再利用する")]
        public bool nativeOwnershipSpawnCounts = true;

        [LabelText("Coalesce same-frame duplicate proc projectiles / 同一フレームの同じ対象への同種反応弾を1本にまとめる")]
        public bool nativeProcCoalesce = true;

        [LabelText("Limit guest publication of same-frame duplicate actors (host) / 同じフレームに同じ形で作った飛び道具などの参加者への公開を減らす（ホスト）")]
        [Description("The server still creates and simulates every actor as in the base game; only the number published to remote guests is reduced. No type is enrolled yet, so nothing changes for now. / "
            + "サーバでは本体どおりすべて生成・処理し、参加者へ公開する個数だけを減らす。対象の型はまだ登録していないため、現時点では何も変わらない。")]
        public bool nativeGuestPublishLimit = true;

        [LabelText("Skip unchanged baked-mesh lookups for face attachments / 面に付ける装飾の変わっていない焼きメッシュの照会を省く")]
        public bool nativeAttachFaceIdle = true;

        [LabelText("Share baked-mesh vertex copies among face attachments / 面に付ける装飾の焼きメッシュの頂点コピーを1回にまとめる")]
        public bool nativeAttachFaceSharedVerts = true;

        [LabelText("Reuse embrace skill position reads / 取り込み中の記憶の座標読み出しを毎フレーム1回にまとめる")]
        public bool nativeEmbracePositionReuse = true;

        [LabelText("Reuse accessory attach offset lookups / 装飾品の取り付け位置の検索結果を使い回す")]
        public bool nativeAccessoryOffsetCache = true;

        [LabelText("Skip idle cooldown multiplier math / 待機中のクールダウンの倍率計算を省く")]
        public bool nativeCooldownIdleMultiplier = true;

        [LabelText("Recover stuck guest skill state / 参加者のスキル使用不可が続いたら再同期する")]
        public bool guestSkillLockRecovery = true;

        [LabelText("Skip transform sync of stationary actors (host) / 動いていないアクターの座標同期を省く（ホスト）")]
        public bool nativeTransformSyncIdle = true;

        [LabelText("Read the checked entity position once per sleep check (host) / 眠り判定で読む敵の位置を英雄全員分で1回にまとめる（ホスト）")]
        public bool nativeSleepPositionReuse = true;

        [LabelText("Skip unchanged cast indicator circle updates / 変わっていない照準円の描画更新を省く")]
        public bool nativeCastIndicatorCircle = true;

        [LabelText("Skip unchanged cast indicator arrow updates / 変わっていない照準矢印の描画更新を省く")]
        public bool nativeCastIndicatorLine = true;

        [LabelText("Reuse the unchanged camera-angle rotation / 変わっていないカメラ角度回転を使い回す")]
        public bool nativeCameraZoomRotation = true;

        [LabelText("Skip unchanged occlusion see-through shader uploads / 変わっていない遮蔽透過のシェーダー値の再送を省く")]
        public bool nativeOcclusionIdle = true;

        [LabelText("Recompute character model bounds only when read / キャラ表示モデルの範囲を読まれるときだけ計算する")]
        public bool nativeCharacterModelDisplayBounds = true;

        [LabelText("Throttle kill-tracker cleanup scans to once per second (host) / 撃破追跡の掃除走査を1秒に1回に間引く（ホスト）")]
        public bool nativeKillTrackerCullThrottle = true;

        [LabelText("Skip repeated default-cursor resets (software cursor off) / ソフトウェアカーソル無効中の同じ既定カーソルの再設定を省く")]
        public bool nativeCursorIdle = true;

        [LabelText("Skip unchanged stats-recalc notifications (host) / 変わっていないステータス再計算の通知を省く（ホスト）")]
        public bool nativeStatsCalcRpcIdle = true;

        [LabelText("Skip recalc health/mana ratio-rewrite resends (host) / ステータス再計算の体力・マナの比率再書き込みノイズの再送を省く（ホスト）")]
        public bool nativeStatsRatioIdle = true;

        [LabelText("Skip no-op ultimate charge broadcasts (host) / 満チャージのウルトの無意味なチャージ再送を省く（ホスト）")]
        public bool nativeUltChargeRpcIdle = true;

        [LabelText("Skip the unchanged lobby accessory rescan / 変わっていないロビーのアクセサリ再走査を省く")]
        public bool nativeLobbyAccessoryIdle = true;

        [LabelText("Skip idle position-sync re-sends / 動いていない実体の位置同期の同じ再送を省く")]
        public bool nativePositionSyncIdle = true;

        [LabelText("Skip unchanged charge-count broadcasts (host) / 変わっていないチャージ数の全員への再送を省く（ホスト）")]
        public bool nativeChargeRpcIdle = true;

        [LabelText("Cache the skill world-model prefab load on every skill spawn (host and guests) / スキルの出現ごとのワールドモデルプレハブのロードをキャッシュに置き換える（ホストと参加者）")]
        public bool nativeSkillWorldModelCache = true;

        [LabelText("Skip Chaos Shrine hit handling while no max-stack bonus is taken (host) / 混沌の神殿で光・闇の最大スタック強化を取っていない間、被弾ごとの処理を省く（ホスト）")]
        public bool nativeChaosShrineHitScan = true;

        [LabelText("Reuse the selected chaos-shrine reward across edit-mode UI frames / 混沌の神殿で選んだ報酬の解決を編集モードUIの毎フレームで使い回す")]
        public bool nativeChaosShrineChoiceCache = true;

        [LabelText("Reuse the corrupted-chaos shrine's petal transforms across animation frames / 堕落した混沌の神殿（Shrine_CorruptedChaos）の花びらアニメで、毎フレームの子Transform探しをキャッシュに置き換える")]
        public bool nativeChaosPetalIdle = true;

        [LabelText("Allocate no LINQ enumerator for the boss-soul shrine's boss-presence scan (local) / ボスソウルの神殿のボス存在走査で呼び出しごとのLINQ列挙子を確保しない（各端末）")]
        public bool nativeBossSoulShrineScan = true;

        [LabelText("Skip projectile RPCs whose effect prefab is null (host) / 演出プレハブが無い弾の命中・完了・消散 RPC を省く（ホスト）")]
        public bool nativeProjectileIdleRpc = true;

        [LabelText("Resolve CustomRpc message types with a cached lookup / 受け取った CustomRpc の型探しをキャッシュで速くする")]
        public bool nativeCustomRpcDispatch = true;

        [LabelText("Skip unchanged cast-preview updates while aiming (client) / 狙いが変わっていないキャストプレビュー更新を省く（クライアント）")]
        public bool nativeSampleUpdateIdle = true;

        [LabelText("Skip dash sweeps' discarded final step write (host) / ダッシュ攻撃の掃引で捨てられる最後の1歩の範囲移動の書き込みを省く（ホスト）")]
        public bool nativeDashSweepStep = true;

        [LabelText("Skip idle weapon-scale writes on heroes / 英雄の武器スケールが止まっているときの同じ書き込みを省く")]
        public bool nativeHeroWeaponIdle = true;

        [LabelText("Skip unchanged on-screen timer re-sends (host) / 変わっていない画面内タイマーの毎フレーム再送を省く（ホスト）")]
        public bool nativePlayerTimerIdle = true;

        [LabelText("Skip settled entity rotation rewrites / 向きが止まった実体の同じ回転の書き込みを省く")]
        public bool nativeEntityRotationIdle = true;

        [LabelText("Cache room-start dependency attribute lookups across rooms / 部屋開始時の依存属性の解決を部屋をまたいでキャッシュする")]
        public bool nativeRoomStartDependency = true;

        [LabelText("Allocate no closure for the per-message chat mute check / チャット受信ごとのミュート判定でクロージャを確保しない")]
        public bool nativeChatMuteCheckAlloc = true;

        [LabelText("Skip unchanged physics-proxy position writes / 止まっている実体の物理プロキシ位置の同じ書き込みを省く")]
        public bool nativeProxyPositionIdle = true;

        [LabelText("Skip settled pickup orbs' per-frame ground raycasts / 止まったドロップ品の毎フレーム地面レイキャストを省く")]
        public bool nativePickupIdle = true;

        [LabelText("Reduce overlapping enemy hit effects (local) / 敵への命中演出の重なりを減らす（各端末）")]
        public bool nativeEnemyHitFxBudget = true;

        [LabelText("Skip unchanged timescale re-sends while slowed (host) / 時間が遅くなっている間の同じタイムスケールの再送を省く（ホスト）")]
        public bool nativeTimescaleIdle = true;

        [LabelText("Skip unchanged Red Giant damage-meter text rebuilds (every client) / レッドジャイアントの値が変わらない被ダメ計測テキストの組み立て直しを省く（各端末）")]
        public bool nativeRedGiantDamageText = true;

        [LabelText("Early-exit the stealth proximity-reveal scan at the first target (host) / ステルスの接近リビール走査を最初の1体で打ち切る（ホスト）")]
        public bool nativeStealthRevealOverlapScan = true;

        [LabelText("Skip merchants' same-pose prop rewrites while the merchant is still (host + guests) / 止まっている商人の装飾モデルへの同じ位置・向きの毎フレーム再書き込みを省く（ホスト・参加者）")]
        public bool nativeMerchantPropIdle = true;

        [LabelText("Skip unchanged transform writes on generic synced objects (guests) / 汎用同期オブジェクトの同じ位置・向きの毎フレーム再書き込みを省く（参加者）")]
        public bool nativeGenericTransformIdle = true;

        [LabelText("Drop the leftover \"HELLO\" debug log when the Obliviax abduction plays (every client) / オブリヴィアクスの連れ去り演出時に残るデバッグログ「HELLO」を省く（各端末）")]
        public bool nativeObliviaxKidnapLog = true;

        [LabelText("Mirage Skin delusion: skip post-process volume rewrites whose values did not change (host and guests) /\nミラージュスキンの「妄想」：値が変わったフレームだけポストプロセスのボリュームを書き直す（ホストと参加者） /\n幻象皮肤的“妄想”：仅在数值变化的帧更新后处理体积（主机和其他玩家）")]
        public bool nativeMirageVolumeIdle = true;

        [LabelText("Skip hit-shake preparation for hits the game discards immediately anyway (host and guests) /\n被弾シェイク：本体が即時破棄する弱い被弾のシェイク準備を省く（ホストと参加者）")]
        public bool nativeDamageShakeIdle = true;

        [LabelText("Skip unchanged audio re-setup on every game-sound play (host and guests) /\n戦闘音を鳴らすたびの同じ音源の初期設定を省く（ホストと参加者）")]
        public bool nativeAudioSetupIdle = true;

        [LabelText("Skip unchanged cast-telegraph position/rotation rewrites every frame (host and guests) /\n詠唱テレグラフの同じ位置・向きの毎フレーム再書き込みを省く（ホストと参加者）")]
        public bool nativeCastTelegraphIdle = true;

        [LabelText("Skip attached-FX transform rewrites whose values did not change (host and guests) /\n付属エフェクトの値が変わらない位置・向き・サイズの毎フレーム再書き込みを省く（ホストと参加者）")]
        public bool nativeFxAttachIdle = true;

        [LabelText("Skip the per-frame gamepad rumble-off command while no shake is active (host and guests) /\n振動が動いていないフレームに毎フレーム送っているゲームパッド振動オフのコマンドを省く（ホストと参加者）")]
        public bool nativeGamepadRumbleIdle = true;

        [LabelText("Cache the Responsibility gem's hit-effect component scans on every RPC receive (host and guests) /\nGem_C_Responsibility の被弾演出RPCを受けるたびの子コンポーネント全走査をキャッシュに置き換える（ホストと参加者）")]
        public bool nativeGemResponsibilityHitCache = true;

        [LabelText("Skip unchanged music volume/pitch rewrites every logic tick (host and guests) /\nBGM の同じ音量・ピッチの毎ロジックtick再書き込みを省く（ホストと参加者）")]
        public bool nativeMusicSourceIdle = true;

        [LabelText("Skip the idle screen-fade bookkeeping every frame while no transition is active (host and guests) /\n場面転換が動いていないフレームの画面フェードの毎フレーム更新を省く（ホストと参加者）")]
        public bool nativeTransitionIdle = true;

        [LabelText("Skip the already-zero agent velocity rewrite in the movement integration (host and the local hero) /\n移動を自分で処理する実体の、すでにゼロのエージェント速度の毎フレーム再書き込みを省く（ホストと自分の英雄） /\n跳过移动整合中已为零的代理速度的每帧重写（主机与本地英雄）")]
        public bool nativeAgentVelocityIdle = true;

        [LabelText("Skip the per-frame empty-loadout re-sync while a player waits to rejoin (host) /\n途中参加の待機中に空のロードアウトを毎フレーム同期し直すのを省く（ホスト）")]
        public bool nativeMidJoinLoadoutIdle = true;

        [LabelText("Level essence slots: add essence slots as the hero levels up (the host's setting applies) /\nレベル連動のエッセンス枠：英雄のレベルに応じて記憶のエッセンス枠を増やす（ホストの設定が使われる）")]
        public bool levelEssenceSlots = true;

        [LabelText("Level essence slots - Q memory / レベル連動の枠：Qの記憶を対象にする")]
        public bool levelEssenceQ = true;

        [LabelText("Level essence slots - W memory / レベル連動の枠：Wの記憶を対象にする")]
        public bool levelEssenceW = true;

        [LabelText("Level essence slots - E memory / レベル連動の枠：Eの記憶を対象にする")]
        public bool levelEssenceE = true;

        [LabelText("Level essence slots - R memory / レベル連動の枠：Rの記憶を対象にする")]
        public bool levelEssenceSlotsR = true;

        [LabelText("Level essence slots - +1 every N hero levels /\nレベル連動の枠：英雄の何レベルごとに +1")]
        [UnityEngine.Range(1, 99)]
        public int levelEssenceEveryLevels = LevelEssenceSettings.DefaultEveryLevels;

        [LabelText("Essence slot cap per memory, including Corrupted Chaos /\n破損した混沌と合わせた1つの記憶のエッセンス枠の上限")]
        [UnityEngine.Range(4, 8)]
        public int levelEssenceCap = LevelEssenceSettings.MinCeiling;

        [LabelText("Smaller packets with supported peers / 対応した相手への通信を小さくする")]
        public bool netCompact = true;
    }

    public enum HudMode
    {
        Full = 0,
        Compact = 1,
        Off = 2,
    }

    /// <summary>#1010: 自動分解するレア度の選択。値は Rarity の番号に合わせ、「そのレア度未満」で分解する。</summary>
    public enum AutoSalvageRarity
    {
        None = 0,
        BelowUncommon = 1,
        BelowRare = 2,
        BelowEpic = 3,
        BelowLegendary = 4,
    }

    /// <summary>敵の名札（悪夢・変種）の表示方法（#1009）。既定は Name（今の見た目）。</summary>
    public enum EnemyNameplateMode
    {
        Name = 0,
        Marks = 1,
        Hidden = 2,
    }

    public enum LightweightMode
    {
        Off = 0,
        Light = 1,
        Strong = 2,
        Max = 3,
    }
}
