# Issue #19 統合仕様：作成済み3DモデルをMobとして導入する（M0〜M3の成果物）

2026-10-04。Issue #19 の調査チェックリスト（1〜6章）に対する**最終成果物**。第1段（[環境・API互換性表](issue19-model-import-research.md)）、第2段（[方式A調査](issue19-stage2-approach-a.md)）、方式Bの実装ドラフト（[導入・検証ガイド](mob-model-integration.md)、[API根拠](mob-api-compatibility.md)、[Unity制作手順](../../tools/MobAssetBuilder/README.md)）を一本にまとめ、判断と作業分解を確定させる。

**この文書は計画であり、実装・ゲーム起動・本番Modsへの配置・セーブ変更・Workshop公開の指示ではない。** Unity実機・2台以上のマルチプレイは**未確認のまま**で、各項目に区分を付けた。

調査起点は PR #4 の `5945286`（v2.0.0 取り込み後は PR #25 の `feat/mob-model-integration`）。main とは分けて扱う。

## 0. 証拠の区分

| 記号 | 意味 |
| --- | --- |
| ◎ 実物確認 | インストール済みゲームのファイル・逆コンパイル・既存MODの実物から行単位で確認（第1段・第2段の記録） |
| ○ 文書確認 | 公式APIドキュメント・Unityマニュアル・既存MODの文書で確認。ゲーム内では未実行 |
| △ 設計判断 | 本書での決定。実測で覆り得る。変更条件を併記 |
| × 未確認 | 実機・Unity Editor でしか確かめられない。確認手順を併記 |

クラウド環境にゲームDLL・Unity Editor・ライセンスは無い。純C#テストと代替エンジン（`tests/MobRuntime.Tests`）の成功は、実機やマルチプレイの成功へ読み替えない。

## 1. 決定サマリ

利用者から「全部自主的に解決して」と委任されているため、未決事項は根拠付きで決める（変更条件を満たせば覆す）。

| # | 決定 | 区分 | 根拠 | 変更条件 |
| --- | --- | --- | --- | --- |
| D1 | **Mob方式は「見た目の差し替え」（M-A）から始める。固有AI・固有ドロップを持つ新規Mob登録（M-B）は後続** | △ | M-B は `NetworkIdentity`/Mirror登録・本体リソースDB・プール・部屋保存・Weaverへの割り込みが要り、すべて未検証（[v1.26 §2d](v1.26-variants-and-models.md)）。M-A は本体が「モデルはローカル読込」と設計している経路（`EntityVisual.LoadModelLocal`）に乗る | M-A の実機・マルチ検証が通り、固有行動の要望が出たとき M-B のスパイクを別Issueで起こす |
| D2 | **ローダーは方式B（Unity 6000.0.77f1 の AssetBundle ＋ EntityModel Prefab）を本線**とし、方式A（実行時GLB）は代替策として設計だけ残す | △ | 溶解・被弾ハイライト・体力バー・死亡演出が本体の仕組みにそのまま乗る（第2段4章：方式AではURP Litに溶解プロパティが届かず自前再実装が要る）。B は実装・単体試験済み。10体はFBX/GLBとも揃い、Unity Editor 6000.0.77f1 は入手可能と思われる | G1（下記）で Editor・URP版・Attack結線が確保できない場合は方式Aへ切替（第2段 S1〜S10 をそのまま使える） |
| D3 | **全参加者が同一コンテンツ（manifestの`contentVersion`＋各bundleの大きさ＋DLL同一性＋Unity/ゲーム版＋BuildTarget）を持つことを必須**とし、不一致は「全員が元モデルへ戻す」（黙って続行しない・見えない敵を許さない） | ◎ 実装済み（ドラフト）/ × 実機未確認 | [導入ガイド](mob-model-integration.md)「ローダーと同期」。見た目だけの差でも判定・見え方が参加者で食い違う遊びを避ける | 将来、安全な代替表示を設計する場合のみ緩和 |
| D4 | **最初の検証モデルは `frostjaw_prowler`（霜牙の追跡者）→ 基底敵 `Mon_Forest_Hound`** | △ | 四足の地上近接で移動方式が単純（Simple locomotion で足りる可能性が高い）。浮遊・多脚・遠隔と違い、判定・攻撃点の乖離が小さい。既存変種「蝕む猟犬」で戦闘・同期の既存試験も使える | G2（基底敵の実物確認）で Hound の攻撃/移動が想定と違えば、次点 `obsidian_scorpion`→`Mon_Forest_Scarab` |
| D5 | **対応ゲーム版・Unity版は `r.1.4.0.13_s` / 6000.0.77f1 の完全一致、OS・BuildTargetは Windows（StandaloneWindows64）のみで開始**。macOS/Linux は未対応と明記 | ◎/△ | 第1段で Windows 版のみ確認。BundleはBuildTargetごとに別物 | 利用者が他OSで協力する必要を示したら、ターゲットごとの生成・検証を別作業に起こす |
| D6 | **欠けた動作（Spawn/Stagger/Death/後退・横移動）は初回は作らない**。Walk を前方ランへ割り当て、Death は本体の溶解演出に任せ、Stagger は無し（モデルは被弾で硬直しない）。Attack は結線方式が確定するまで**出荷しない** | △ | 3クリップしか無い（Idle/Walk/Attack）。ビルダーも欠落クリップの黙った代入を拒否する | 品質上必要なら Stagger/Death の追加モーション制作を別作業にする |
| D7 | **利用者判断が要るのは次の2点だけ**：①Unity Editor 6000.0.77f1 の用意（PoC実施者の端末）、②アセットの配布ライセンス（[manifest](../../assets/dreamborne/manifest.json) に「明示ライセンスなし」とある） | × | クラウドでは実行できない | — |
| D8 | **性能の合格値はPoC実測後に決める**。それまでの暫定ゲートは「同数の元モンスターに対する差分」で判定（§7） | △ | Issue本文「許容値は実測後に数値で合意」 | — |

## 2. 環境・API互換性表（チェックリスト1）

| 項目 | 確認値 | 区分 | 根拠 | 未確認・次の確認 |
| --- | --- | --- | --- | --- |
| ゲームの版 | `r.1.4.0.13_s` | ◎ | ゲーム `version.txt`（第1段） | `Application.version` と一致するかは **実行時の値を採取して照合**（READMEの表示ラベルと同一視しない） |
| Unity | **6000.0.77f1**（UnityPlayer.dll 6000.0.77.8976541） | ◎ | `globalgamemanagers`、UnityPlayer.dll | パッチ番号まで同じ Editor の入手 |
| レンダーパイプライン | URP（Universal.Runtime/Shaders、GPUDriven） | ◎ | `Managed` のDLL一覧 | **URPパッケージの正確な版**（DLLに版は埋まらない）。Editor側で解決される版との一致が必要 |
| シェーダー | 常駐に `Universal Render Pipeline/Lit`・`Simple Lit`・`Unlit`。本体のキャラ用プロパティは `_DissolveStrength` `_CMBaseColor` `_CMEmission` `_CMOpacity` `_CMDissolveColor` ほか | ◎ | `globalgamemanagers`、`EntityVisual.cs:2451-2462`（第2段） | キャラ用カスタムシェーダーの表示名は抽出できていない。×：パレット素材（URP Lit）が本体の溶解・変種色を受けるか |
| 色空間・Graphics API | D3D12フォルダあり、`gfx-threading-mode=6` | ◎（Windowsのみ） | ゲームフォルダ、`boot.config` | Vulkan/Metal は未確認（D5でWindows限定） |
| 通信 | Mirror（Transports/Authenticators含む） | ◎ | `Managed` | ゲーム内Mirrorの正確な版、host側ローカルClientでspawn/unspawn handlerが呼ばれない経路（Mirror公式Custom Spawn Functions）が本体で同じか → ×（§6.4） |
| リソース | Addressables（`StreamingAssets/aa`）、`UnityEngine.AssetBundleModule` あり | ◎ | `Managed`、`StreamingAssets` | MODから独自 AssetBundle を読む前例は既存Workshop MODに無い（VRM MODは実行時glTF）。×：本MODの Bundle 読込（PoC） |
| アニメーション | 再生は **Animator＋AnimatorOverrideController**（Playables不使用）。Idle/8方向ラン/Stagger/Death は「クリップ名＝enum名のステートを上書き」、攻撃はレイヤー1で `ReplaceAnimationLocal(Ability, rawClip)` | ◎ | `EntityAnimation.cs:90,360-375,390-409,433-467`（第2段1章） | モンスター prefab ごとの Avatar 種別（humanoid/generic）、Animator の cullingMode → ×（基底敵の実物確認 G2） |
| モデル差し替えAPI | `EntityVisual.LoadModelLocal(EntityModel)`：**初期化済みインスタンスは例外**。元へ戻すのは `LoadModelDefaultLocal()`。ローカル専用、ネットワーク同期なし。`EntityModel` の主要メンバーは [API根拠](mob-api-compatibility.md) の表 | ◎/○ | `EntityVisual.cs:699〜`（第1段）、公式API | 例外後に元の見た目が残る保証は文書に無い。×：失敗時の復元をPoCで確認 |
| 呼出順・副作用 | `ClientEvent_OnModelLoaded` は `LoadModelLocal` 末尾のみで発火。**プール再利用では発火しない**（modelが既に載っているため）。モンスターは `usePooling=false ＆ reuseInRoom=false` で購読の自動清算が走らず、購読が溜まる | ◎ | `EntityVisual.cs:856,1046-1047`、`DewPool.cs:50-76`（第2段2章） | 本実装は `ClientEvent_OnModelLoaded` に依存せず、ポーリングで所有状態を追う（導入ガイド） |
| スポーン・破棄・プール | 部屋開始時に prewarm（既定 `true`）、despawn は SetActive(false) で park。**同じGameObjectが新しい netId で再利用され得る** | ◎ | `SpawnManager.cs:13,258,281-300,518-526`（第2段2章） | 本MODは `netId＋spawn世代` で個体を識別（§6） |
| MOD仕組み | `DewMod`・`ModBehaviour`、DLLは `metadata.json` の `assemblies` のみ読み込み（読込時にアセンブリ名へ tick を付与）。MOD相対パスは `ModItem.path` | ◎/○ | `DewMod.cs:497-513,704-709`、公式API | 同名ライブラリの衝突は方式A時のみ問題（B は管理DLLのみ） |
| 再配布 | ゲーム由来DLL/シェーダー/素材は同梱しない。モデルの利用条件は未指定 | ○ | 導入ガイド、`assets/dreamborne/manifest.json` | D7②（ライセンス） |
| `supportedGameVer: ["*"]` | **実証済み範囲とみなさない**。D5 の範囲だけを対応とする | △ | metadata.json（Issue指摘どおり） | 版ごとの再ビルド・再検証（§4.5） |

Unity Editor は必須ではなく、モデルを使う側（PoC実施者）の端末にあればよい。Editor で作る生成物は**隔離プロジェクト**で作り、ゲームの `Mods` へ自動コピーしない（既存の自動コピーは既定で停止済み）。

## 3. Import・Prefab契約（チェックリスト2）

### 3.1 Importプリセット仕様（◎ 制作側確認＋△ 設計）

| 項目 | 仕様 | 根拠 |
| --- | --- | --- |
| 入力 | **FBX を本番入力**。GLB は参照用、Blend は編集元。Unityの標準ImporterでFBXを読むためglTF Importer導入は不要 | [assets README](../../assets/dreamborne/README.md) |
| 命名 | `modelId`（manifest）＝Prefab名＝bundle内パス `assets/.../<modelId>.prefab` の小文字・数字・`_`。manifest検証が正規化済みパスを要求 | `MobModelManifest` |
| 単位・軸 | メートル、Blender Z-up/-Y-forward、その場アニメ。**Unity側の前方向・足元高さ・スケールは Import後にPrefab上で確認**（×） | assets README |
| Rig | **Generic**（Humanoid前提にしない）。四足・多脚・浮遊を含むため。Root Motion無し、clipイベント削除 | ビルダー |
| 骨 | ボーン数16〜33。`obsidian_scorpion`(33)・`lantern_wisp`(29)・`abyssal_bell`(26) は多い。1頂点あたりの影響数は Unity 側の `skinWeights` 設定とBlender書き出しの上限を**Importで確認**（×） | manifest |
| 材質 | パレット1枚＋URP Lit（Base/Emission、`metallicRoughness` はチャンネル変換）。透明・溶解は使わない | ビルダー、assets README |
| 検査 | skinned renderer、UV、材質、bindpose、未初期化EntityModel、許可コンポーネント、clip パス、サンプルしたbounds | ビルダー |

### 3.2 Prefab構成（○ 公式API ＋ △ 契約）

- **「Prefabアセット」と「個体の初期化済みインスタンス」を分ける**。bundle内のPrefabは未初期化の `EntityModel` で、各クライアントが `LoadModelLocal` に渡す。ゲームが生成する個体のモデル（初期化済み）を再投入しない。
- Prefabは**基底敵と同じビルドの EntityModel テンプレートから作る**（モデル部分だけ差し替える）。`bodyRenderers`・`healthBarPosition`・`weapon`・`customMappings`・FXの参照を保ち、`replaceChildPath` の枝だけをFBXで置換する。ネットワークPrefab（Monster本体）は作らない。
- 許可コンポーネントは Transform/Animator/Renderer/MeshFilter/EntityModel のみ。ゲーム用スクリプト・物理・FX依存はビルダーが拒否する（緩めるには別途契約の確認が要る）。
- `abilityAnimationReplacements` はメンバーとして存在する（◎）が、要素の形・Ability名・キーは**未確認**（×）。

### 3.3 モデル別の不足データ（10体）

3クリップ（Idle/Walk/Attack）しか無い前提で、足りないものを列挙する。

| 動作 | 状況 | 初回の扱い（D6） |
| --- | --- | --- |
| Spawn | クリップ無し | 不要（本体の出現演出に任せる） |
| Run（4/8方向） | Walk のみ | `locomotion=Simple` ＋ `runForwardClip` へ Walk |
| Stagger | 無し | 設定しない（硬直演出なし）。作るなら追加制作 |
| Death | 無し | 本体の溶解（`deathBehavior`）に任せる。Idle を黙って代入しない |
| Attack | あり（1.33〜1.60秒、非ループ） | **結線方式が未確定のため出荷しない**。ゲートG3 |

| 注意が要るモデル | 理由 |
| --- | --- |
| `lantern_wisp`、`abyssal_bell`、`glasswing_moth` | 浮遊・飛行。地上基底敵だと影・足元位置・当たり判定がずれる。基底敵は浮遊型が望ましい |
| `obsidian_scorpion`（33骨）、`abyssal_bell`（触手） | 骨数が多く、Importでのウェイト・bounds確認が要る |
| `runestone_tortoise`、`ember_warden` | 大型・重量。攻撃の予備動作と本体Abilityの命中タイミングの調整が要る |
| `duskwing_oracle`、`mirecap_stomper` | 術師・踏みつけ。攻撃点・範囲FXの位置が基底敵と合うか確認 |

## 4. AssetBundle（チェックリスト3）

### 4.1 生成と配布（○ ガイド ＋ △）

- **モデル別（10個）のbundle**を BuildTarget ごとに生成する（D5：StandaloneWindows64）。各bundleは1モデル分のprefab・クリップ・材質・テクスチャを自己完結に持つ。圧縮はビルダーの既定（`ChunkBasedCompression`＝LZ4）を使い、読込時間の実測で見直す。
- 各bundleのファイルサイズと `contentVersion` を含む **runtime manifest（`models/manifest.json`）** を最後に出力する。`contentVersion` は制作ツールが再構築するたびに新しい値（UTCタイムスタンプ＋通番）になり、これと各bundleの大きさ・DLL同一性が参加者間の照合キー（§6）。ハッシュ照合は使わない。
- 配布物は隔離ZIP（`about/`、`DreamforgeRPG.dll`、`models/`）。`tools/package_mob_mod.py` で作り、ゲームの `Mods` へは自動コピーしない。MOD相対パスは `ModItem.path` から解決する（Workshopでも同じ）。
- ゲーム更新時：版が変われば manifest が一致せず**自動で無効化**される。再ビルド→再検証を行ってから版を上げる（§4.5）。

### 4.2 読込・解放ライフサイクル（○ ＋ △）

| 時点 | 誰が何をするか |
| --- | --- |
| MOD有効化 | manifest検証（版・SHA・型対応）。合格すれば bundle を同期 `LoadFromFile`。失敗は理由をログに出して無効のまま |
| 初回使用 | Prefabは bundle から1回だけ取得してキャッシュ。個体ごとには `LoadModelLocal` で生成されたものだけを所有状態として追跡 |
| 個体 despawn/死亡 | 追跡状態を解除。モデル自体は本体が破棄 |
| プール返却→再利用 | 復元は `LoadModelDefaultLocal()`。**新しい netId＋spawn世代の割当が届くまで**再適用しない（古い個体への通知で別個体を乗っ取らない） |
| 部屋移動・セッション終了 | 所有していた全個体を元へ戻し、room世代を進めて準備合意をやり直す |
| MOD無効化・ライブリロード | 全個体を元へ戻してから bundle を解放。全個体の復元に成功したときだけ `Unload(true)`、復元しきれていなければ `Unload(false)`（使用中参照を壊さない。`MobModelAssets.Release`） |
| 復元失敗 | 使用中のアセットを破壊せず保持し、以後は停止とログを優先 |

`Unload(true)` は使用中のオブジェクトを破壊するため、全個体の復元が済んだ場合に限る（`Unload(false)` は参照を残すので、再読込後の複製・メモリ残留を§7.3 の回帰で数える）。他MODが後から変えたモデルは上書きしない（所有マーカー方式）。

### 4.3 失敗時の挙動（◎ 実装済み / × 実機未確認）

| 状況 | 挙動 |
| --- | --- |
| bundle/manifest 欠落・破損・SHA不一致 | 無効のまま起動。元モデル。`dreamforge_mobstatus` に理由 |
| 旧版・別BuildTarget・別Unity/ゲーム版 | manifest検証で拒否 |
| 不明なMonster型・初期化済みPrefab・許可外コンポーネント | 拒否 |
| 参加者のうち1人でも未導入・無効・版違い・応答なし | **全員で元モデルへ戻す**（D3） |
| 設定 `customMobModels` | 初期値 false。全員が明示的に有効にした場合のみ使う |

### 4.4 Unity版の互換

Unityの資料の版（Unity 6000.3等）をゲームの 6000.0.77f1 と同一視しない。Unity 6000.0 の `AssetBundle.LoadFromFile`・`Unload` の挙動はマニュアルの該当版で確認済み（[API根拠](mob-api-compatibility.md)）。

### 4.5 公開・容量・ソース管理

- ソースモデル（FBX/GLB/Blend/PNG）は計5.7MB。既に88断片のアーカイブで管理（`assets/dreamborne/`）。LFS不要。
- **生成物（bundle・manifest・ZIP）はリポジトリへ入れない**（制作は隔離プロジェクトと外部の出力先で行う）。ビルド手順で再現する。
- ゲーム更新ごとの再検証ルール：Editor版・URP版・SDK DLL のSHAを `mob-build.json` に固定して再ビルド → §8 の回帰を実行。

## 5. Mob方式の比較と最初の1体（チェックリスト4）

### 5.1 M-A「見た目の差し替え」と M-B「新規Mob登録」

| | M-A 見た目の差し替え（D1：先に実施） | M-B 新規Mob登録 |
| --- | --- | --- |
| 内容 | 既存Monster・AI・攻撃・当たり判定・報酬・出現は本体のまま。各クライアントが `LoadModelLocal` で見た目だけ差す | 固有ID・定義・Prefabを登録し、Dew.SpawnEntityで出す |
| 使うAPI | `EntityVisual.LoadModelLocal/LoadModelDefaultLocal`（◎）、既存の出現・報酬 | `NetworkIdentity` assetId、Mirror `RegisterPrefab`、`DewResourceDatabase`、`DewResources.GetNetworkedPrefab`（○） |
| 未検証点 | 基底敵ごとのAttack結線、同期 | Monster派生型をMOD側に置けるか、**Weaverが走らない**ため SyncVar/Rpc が使えない可能性、プール・部屋保存との整合、人口・部屋クリア判定 |
| 追加制作 | 少（モデル＋Prefab） | 大（AI・攻撃・Ability・HP設計・報酬・出現表） |
| リスク | 中 | 高（旧調査の結論「新規型は避ける」を根拠として維持） |
| 判定 | **採用** | 後続（D1の変更条件を満たすまで着手しない） |

出現・確率・編成・人口・部屋クリア・ミニボス/ボス枠・HP/攻撃/防御・深度/悪夢/人数補正・ドロップ・経験値・依頼/実績の集計は、M-Aでは**一切変更しない**（基底敵のものがそのまま使われる）。したがって報酬の重複・欠落、部屋進行の停止は、モデル差し替えの範囲では起きない設計。見た目の大きさとホストの当たり判定は別物なので、**当たり判定は基底敵のまま**とし、モデルを基底敵の足元範囲（フットプリント）に合わせて縮尺する（△）。

### 5.2 基底敵への対応（現行ドラフト）

`MobModelManifest` は「基底Monster型 → modelId」を**型単位で1対1**に固定する（同じ型を複数モデルに割り当てない）。つまり差し替えは「その型の敵すべて」に及ぶ。変種IDごとの割当は未実装（作業W7）。

| モデル | 候補の基底敵（仮説） | 区分 | 理由 |
| --- | --- | --- | --- |
| `frostjaw_prowler`（狼） | `Mon_Forest_Hound` | △ 最初の1体 | 四足の地上近接 |
| `obsidian_scorpion`（蠍） | `Mon_Forest_Scarab` | △ 次点 | 地上の節足・殻 |
| `glasswing_moth`（蛾） | `Mon_DarkCave_CaveBat` | △ | 飛行 |
| `lantern_wisp`（灯の精） | `Mon_LavaLand_FireElemental` | △ | 浮遊・発光 |
| `duskwing_oracle`（鴉の術師） | `Mon_Ink_Archer` | △ | 遠隔 |
| `mirecap_stomper`（茸獣） | `Mon_Forest_Treant` | △ | 大型・遅い |
| `ember_warden`（炉のゴーレム） | `Mon_DarkCave_Oppressor` | △ | 重量・重い一撃 |
| `thorncrown_stag`（鹿）、`runestone_tortoise`（亀）、`abyssal_bell`（鈴クラゲ） | 未確定 | × | 適合する既知の型が無い。G2（実機での型一覧）で決める |

型名は本リポジトリの [Variants](../../src/SodRpg.Core/Game/Variants.cs) にある実在の型。**どの型が適合するかは実機で攻撃・移動を見ないと確定しない**ため「仮説」とする。

### 5.3 導入順の提案

1. `frostjaw_prowler`→`Mon_Forest_Hound`（D4）
2. 地上近接系：`obsidian_scorpion`、`mirecap_stomper`、`ember_warden`
3. 遠隔・飛行・浮遊系：`duskwing_oracle`、`glasswing_moth`、`lantern_wisp`
4. 適合型が未確定の3体（鹿・亀・鈴クラゲ）

1体が通ってから次へ進む。1体目の合格前に残りのPrefabを量産しない。

## 6. マルチプレイ仕様（チェックリスト5・必須）

### 6.1 責務表

| 項目 | ホスト | クライアント |
| --- | --- | --- |
| Monsterの種類・出現・AI・ダメージ・当たり判定・死亡・報酬 | **決める**（本体のまま。M-Aでは変更しない） | 本体の同期に従う |
| どの個体にどのモデルを割り当てるか | **決める**（netId、spawn世代、基底型、modelId の割当を発行） | 受け取ったものだけ適用 |
| 見た目の適用 | 自分の画面にも適用（listen host含む） | 準備完了の合意後に適用 |
| 準備の確認 | 全人間参加者＋自分の準備応答を集計 | nonce付きで応答（manifest/DLL/Unity/ゲーム/BuildTarget） |
| 攻撃・報酬の発生 | ホストのみ。**見た目のAnimation Eventから攻撃や報酬を発生させない**（ビルダーが clip イベントを削除、許可コンポーネントにスクリプトなし） | しない |

### 6.2 参加・出現・途中参加・終了のシーケンス

1. セッション開始：ホストが `sessionNonce`・`roomEpoch` を決め、期待する互換情報（manifestハッシュ・DLLハッシュ・Unity/ゲーム版・BuildTarget）を配る。
2. 各クライアントが nonce に応答。**送信者はゲームが渡す送信者オブジェクトで識別**し、パケット内の自己申告IDは信用しない。
3. 全員（ホスト含む）が揃って初めて割当を有効化。未導入・版違い・タイムアウトの人がいれば**全員で元モデル**。
4. 出現：ホストが完全スナップショット（個体 netId＋spawn世代＋基底型＋modelId、上限300個体）を1秒ごとに再送。クライアントは `visual.model` が揃った個体から適用。
5. 途中参加・再接続：参加時に roster が変わる→準備合意をやり直し→最新スナップショットで追いつく。出現前に届いた通知は同じ状態から再試行。
6. 部屋移動・終了：room世代を進め、全員が元へ戻して合意をやり直す。despawn 後の通知・netId再利用は spawn世代で弾く（曖昧な再利用IDは全員停止）。
7. ホスト切断・MOD再読込（どちら側でも）：状態が不確かな間は元モデルへ戻し、再合意が取れるまで再開しない。

### 6.3 互換性・失敗時ポリシー

| 状況 | 方針 |
| --- | --- |
| 同じmodelIdで中身・DLLが違う | 有効化しない（SHA照合） |
| 通信期限（6秒）切れ・古いrevision・重複 | 無視/除外。期限切れは全員停止 |
| 追跡個体が4096履歴を超過 | 全員停止（黙って続行しない） |
| 認識できないモデルID・型 | 全員停止 |
| ゲーム/MOD/bundle版不一致 | 準備不成立→全員元モデル。理由を `dreamforge_mobstatus` に出す |

### 6.4 未確認（×・PoCで確かめる）

- listen host のローカルClient経路でspawn/unspawn handlerが呼ばれない（Mirror公式の説明）点が、**ゲームのMirror版・本体の `SpawnManager` で同じか**。コードはhostのローカル経路を明示処理するが、代替エンジンでの試験のみ。
- 攻撃状態・向き・速度・アニメ開始時点の同期は、本体の `EntityAnimation` 同期（Animatorパラメータ・`abilityAnimStatus`）を再利用できる想定。**途中参加時に攻撃中の個体が正しい位相で表示されるか**は未確認。
- 見た目モデルが違っても、攻撃の命中時点は本体Abilityのタイミングで決まる。Attackクリップの予備動作と命中点のずれ（クリップ長は1.33〜1.60秒）は、モデルごとに実機で調整。

## 7. クリーンな取り込みと性能（チェックリスト6）

### 7.1 アセット検査表（取り込み時・自動化済み）

`tools/validate_mob_assets.py`＋ビルダーの検査（◎ 純C#/Pythonで実行済み）：ファイルのサイズ、メッシュ数1・材質数1・骨数・クリップ3、三角形、`takeName`、非ゼロの変形カーブ、clipイベントなし、Root Motionなし。Unity上では実測を追加：

- bind pose・skinning、`SkinnedMeshRenderer.bounds`（巨大/小型・浮遊でも画面外カリングで体の一部が消えない）
- 前方向・足元高さ・スケール（基底敵の足元範囲に合わせる）
- パレットUV、発光、ピンク表示（材質欠落）の自動検出（読み込み後に材質の shader が `Hidden/InternalErrorShader` でないことを確認）

### 7.2 性能の測定手順（×・PoCで実施）

測る：CPU/GPUフレーム時間、GC割当、メモリ（Texture/Mesh/AnimationClip）、bundle読込時間、描画回数、スキニング負荷。**比較基準は、差し替え前の同数の元モンスター**。

| 条件 | 内容 |
| --- | --- |
| 検証機 | PoC実施者の実機1台（CPU/GPU/メモリを記録） |
| 同時出現数 | 1体/10体/ゲームの最大同時数（実測で確認）。人数は1人・最大人数（ゲームの上限）の2点 |
| 暫定ゲート（D8） | 同数の元モンスターに対して、フレーム時間の悪化が顕著でないこと、GC割当が新規に継続的に増えないこと、メモリが再生成を繰り返しても単調増加しないこと。**具体値は実測後にIssueで合意** |

モデルの規模は1体あたり880〜3,020三角形・16〜33骨で小さいが、「現行三角形数だけで十分軽いと判定しない」（Issue本文）。

### 7.3 回帰項目（繰返し spawn/despawn・プール・リロード）

元の敵へのモデル混入、補正の積み上げ、イベント多重登録、リソース残留がないこと。次を各回ごとに数える：`LoadModelLocal` 呼出数と所有個体数の一致、Prefab参照数、`Resources.FindObjectsOfTypeAll<Mesh>` の増減、購読数。

## 8. 最初の1体の推奨仕様、PoCの手順・影響・復旧（M2）

### 8.1 最初の1体

| 項目 | 内容 |
| --- | --- |
| モデル | `frostjaw_prowler`（2,610三角形、20骨、Idle 2.00秒/Walk 1.33秒/Attack 1.33秒） |
| 基底敵（仮説） | `Mon_Forest_Hound`（G2で確認） |
| 方式 | M-A × 方式B。locomotion=Simple、runForwardClip=Walk |
| 先に確認 | G2（基底型の実物）、G3（Attack結線） |

### 8.2 ゲート（先に決める順）

| ゲート | 確認すること | 担当 | 失敗したとき |
| --- | --- | --- | --- |
| G1 | Unity Editor 6000.0.77f1 の用意、URP版の特定、`Dew.Core.dll` 等のSDK参照でEditorコードがコンパイルできる | 利用者端末 | 方式Aへ切替（第2段の設計） |
| G2 | 実ゲームで基底敵（`Mon_Forest_Hound` 他）の型・EntityModel（Avatar種別、cullingMode、locomotion、`abilityAnimationReplacements` の中身）をダンプ | 利用者端末 | 候補型を入れ替える |
| G3 | Attack結線：`abilityAnimationReplacements` の要素形・Ability名を実物から確認し、Attackクリップを本体の攻撃アニメとして再生できるか | 利用者端末 | 本体Abilityの時間に合わせた別の結線（アニメ転送コンポーネント）か、Attackなしの見た目のみで先に出す |

### 8.3 PoC手順（実施前に許可を得る：ゲーム起動とMOD配置を伴うため）

1. 隔離したUnityプロジェクトを `prepare_project.py` で作り（`--sources-only` で import 検査）、`frostjaw_prowler` の見た目・軸・足元・skin・クリップ（ループ/長さ）を目視確認。
2. G2・G3 を満たしたら本ビルド → bundle生成 → 再ロード確認 → runtime manifest。
3. `tools/package_mob_mod.py` で隔離ZIPを作る。**本番 `Mods` には入れない**。専用のテスト用ゲーム環境（別セーブ/別プロファイル、または Workshop無効の別インストール）に手で配置。
4. ソロ：変種「蝕む猟犬」が出る部屋で、待機・移動・旋回・攻撃・被弾・死亡・出現/消滅を既存の猟犬と見比べる（Tポーズ、ピンク、足滑り、判定のずれ）。
5. 協力（2台）：ホスト/参加者を**役割入替**して同じ手順。(a)両方に同じbundle、(b)参加者側のbundle削除→全員元へ戻る、(c)戦闘中の途中参加と切断・再接続、(d)ホスト側のみ・参加者側のみMOD再読込、(e)ゲーム版/manifest不一致。
6. 性能：§7.2 の条件で測定し、元の猟犬と比較して記録。

### 8.4 影響と復旧

- 影響：テスト用環境のMOD配置とゲーム起動のみ。セーブ・設定・本番 `Mods` は変更しない（`customMobModels` は既定false）。
- 復旧：テスト用の配置フォルダを削除する、または `customMobModels=false` にする（`OnConfigChanged`→`Configure` で全個体を復元する実装。反映の確認はPoCで行う）。bundleが壊れていても元モデルのまま。

## 9. 未決事項への決定

| Issueの未決事項 | 決定 | 区分 |
| --- | --- | --- |
| 最初は見た目の差し替えか、新規Mobか | 見た目の差し替え（D1） | △ |
| 最初の検証モデルと本体敵、残りの優先順 | `frostjaw_prowler`→`Mon_Forest_Hound`。順序は§5.3 | △ |
| 対応ゲーム版・Unity版・OS・人数・不一致時方針 | `r.1.4.0.13_s`/6000.0.77f1、Windowsのみ、人数はゲームの最大。全員同一bundle必須、不一致は全員元モデル（D3・D5） | ◎/△ |
| Stagger/Death/Spawn・追加FX/音・当たり判定/バランスの担当 | 初回は作らない（D6）。当たり判定・バランスは基底敵のまま変更しない | △ |
| 公開可能なモデルの取り込み先・版・配布条件 | `assets/dreamborne/`（FBXが正）。ソース公開は利用者が許可済み（2026-10-04）。**独立配布のライセンスは未決**（D7②） | × |
| 調査用Unity環境と複数台の検証環境 | 利用者端末（D7①）。クラウドでは不可 | × |

## 10. 作業分解（M3）

「Unityアセット準備」「読込/寿命管理」「見た目適用/同期」「新規Mobのゲーム仕様」「実機回帰試験」に分ける。**担当**：☁ クラウドで可能 / 💻 利用者端末が必要。

| # | 作業 | 担当 | 前提 | 完了条件 |
| --- | --- | --- | --- | --- |
| W1 | Unity Editor・URP版・SDK参照の確定（G1） | 💻 | — | `mob-build.json` の `urpVersion`・`gameVersion`・SDK SHA が埋まり、EditorでSDKがコンパイルできる |
| W2 | 基底敵の実物ダンプ（G2）：型一覧・EntityModel・Avatar・cullingMode | 💻 | — | §5.2 の仮説表が確定表になる |
| W3 | Attack結線（G3）の確定 | 💻（調査）→ ☁（実装） | W2 | 結線方式が決まり、ビルダーの `Attack` バインディングが埋まる |
| W4 | 1体目（frostjaw）の import検査→Prefab/bundle生成 | 💻 | W1〜W3 | §7.1 の検査表が全て合格 |
| W5 | 実機ソロ検証（§8.3-4） | 💻 | W4 | 動作チェック表（待機〜死亡）が合格 |
| W6 | 実機マルチ検証（§8.3-5）、役割入替・途中参加・再読込 | 💻（2台） | W5 | §6 のシナリオが全て合格 |
| W7 | **変種ID→modelId の割当**（現在は基底型単位） | ☁ | W5後に必要性を判断 | `Variants` と manifest の対応が実装され、Coreテストが通る |
| W8 | 性能測定（§7.2）と許容値の合意 | 💻 | W5 | 暫定ゲートが数値に置き換わる |
| W9 | 残り9体のPrefab化（§5.3の順） | 💻 | W6・W8 | 各体が W5〜W6 を満たす |
| W10 | Stagger/Death の追加モーション（必要なら） | モデル制作 | 品質判断 | 追加クリップ＋結線の検証 |
| W11 | M-B（新規Mob登録）のスパイク | ☁/💻 | W6完了後に要望があれば | 別Issue |
| W12 | macOS/Linux 向けBundle（要望があれば） | 💻 | D5の変更 | ターゲット別の生成・検証 |

☁ で今できることは、W7 の設計と、W3 が決まった後の実装のみ。他は実機が要る。

## 11. 出荷条件と未確認一覧

次をすべて満たすまで「導入可能」「実機マルチ対応済み」とは言わない（Issue本文の導入判断条件）。

- [ ] 実ゲームDLLでMod全体と Unity Editor コードがコンパイルできる（W1）
- [ ] 実Unityで Import/Prefab/AssetBundle が生成できる。URP版・基底敵・Attack結線が確定（W1〜W4）
- [ ] 2台以上で、ホスト役を交換しても同じ個体・モデル・攻撃/死亡状態が一致する（W6）
- [ ] 遅延・途中参加・切断・片側再読込・部屋移動・プール再利用・他モデルMODとの競合で復元できる（W6）
- [ ] 最大人数/同時個体数の性能を実測し、元モデルと比較（W8）
- [ ] ピンク/Tポーズ/足滑り/判定のずれ・部屋クリア・報酬の整合（W5〜W6）

## 12. 文書間の食い違いの整理

| 点 | 内容 | 本書での扱い |
| --- | --- | --- |
| A/B の二重の意味 | Issueの「A=見た目の差し替え／B=新規Mob」と、第1段・第2段の「A=実行時GLB／B=AssetBundle」が同じ記号 | 本書は Mob方式を **M-A/M-B**、ローダーを**方式A/方式B**と呼び分ける |
| 利用者のローダー選択 | 第2段は「利用者が方式Aを選択した」と書く一方、[導入ガイド](mob-model-integration.md)・PR #25 は「A/Bの最終決定の記録はない」と書く。**プロジェクトのチャットにも選択の記録は無い**（2026-10-04確認） | 記録が無いものは決定として扱わない。D2 で方式Bを本線に決め、G1失敗時に方式Aへ切替える |
| 変種の数 | 旧仕様の25種は実装基準にしない。実装は13種（[Variants](../../src/SodRpg.Core/Game/Variants.cs)） | 13種を基準 |
| 「ホストだけMOD必須」 | v1.26 の記述 | 採用しない。D3：全員同一 |
| ロードマップの保留 | モデル技術検証は取りやめ・変種追加は保留（[roadmap](../dreamforge-roadmap.md)） | 本書は計画であり、保留中の実装を再開しない。実機PoCは §8.3 の許可を得て行う |
| 制作元の「dream_eater」 | Issue本文の元ゴーレムは今回のリポジトリの10体に含まれない | 本書は同梱の10体のみ対象 |
