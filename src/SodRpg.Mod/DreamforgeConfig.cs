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

        // 表示言語は3択（#877）: chinese=true → 中国語 / japanese=true → 日本語 / 両方false → 英語。
        // chinese が japanese より優先する。旧版のMODはこの項目を無視して
        // japanese（中国語選択時は false ＝ 英語）へ戻るため、旧版でも安全に既定の言語で動く。
        // 壊れた保存では両方既定値（true/false ＝ 日本語）に戻り、未知の言語値は生じない。
        [LabelText("UI language: Japanese / 表示言語：日本語")]
        [Description("Language is a three-way choice. Chinese (Simplified) takes priority over Japanese; uncheck both for English. / "
            + "表示言語は3択です。簡体中文（中国語）をチェックすると日本語より優先され、両方外すと英語になります。")]
        public bool japanese = true;

        [LabelText("UI language: Chinese, Simplified / 表示言語：简体中文（优先于日语 / 日本語より優先）")]
        [Description("Check to show Simplified Chinese (untranslated text falls back to English). Takes priority over Japanese. / "
            + "チェックすると簡体字中国語で表示します（未訳の部分は英語）。日本語より優先されます。")]
        public bool chinese = false;

        /// <summary>2つの真偽値から表示言語を決める。壊れた値もこの組み合わせに収まる。</summary>
        public LocLanguage ResolveLanguage() =>
            chinese ? LocLanguage.Chinese : japanese ? LocLanguage.Japanese : LocLanguage.English;

        [LabelText("UI scale / 表示の大きさ")]
        public float uiScale = 1f;

        [LabelText("Show drops below Rare / レア未満の拾得通知")]
        public bool showToasts = true;

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

        [LabelText("Skip Chaos Shrine hit handling while no max-stack bonus is taken (host) / 混沌の神殿で光・闇の最大スタック強化を取っていない間、被弾ごとの処理を省く（ホスト）")]
        public bool nativeChaosShrineHitScan = true;

        [LabelText("Reuse the selected chaos-shrine reward across edit-mode UI frames / 混沌の神殿で選んだ報酬の解決を編集モードUIの毎フレームで使い回す")]
        public bool nativeChaosShrineChoiceCache = true;

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

    public enum LightweightMode
    {
        Off = 0,
        Light = 1,
        Strong = 2,
        Max = 3,
    }
}
