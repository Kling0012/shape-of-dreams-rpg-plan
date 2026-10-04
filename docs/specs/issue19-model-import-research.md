# Issue #19 調査：作成済み3DモデルをMobとして追加する（第1段：環境・API互換性表）

2026-10-03 調査。読み取りのみ（ゲーム本体・セーブ・Mods 配下は変更していない）。実装の開始指示ではない。

## 1. 環境・API互換性表

| 項目 | 確認値 | 根拠 | 未確認・次の確認 |
| --- | --- | --- | --- |
| ゲームの版 | r.1.4.0.13_s | ゲームフォルダの `version.txt` | — |
| Unity | **6000.0.77f1**（UnityPlayer.dll 6000.0.77.8976541） | `globalgamemanagers` の先頭、`UnityPlayer.dll` のファイル版 | パッチ番号まで同じ Editor の入手可否 |
| 描画 | **URP**（Universal.Runtime / Shaders / 2D、GPUDriven、BRGInstancedRenderer.URP） | `Shape of Dreams_Data/Managed` の DLL 一覧 | URP パッケージの正確な版（DLL にパッケージ版が埋め込まれていない。`Packages` 情報は同梱されない） |
| グラフィックスAPI | D3D12 フォルダあり、`gfx-threading-mode=6` | ゲームフォルダ、`boot.config` | Vulkan/Metal の有無（Windows 版のみ確認） |
| 通信 | Mirror（Mirror / Components / Transports / Authenticators） | Managed の DLL | — |
| 資源の読み込み | Addressables（`StreamingAssets/aa`）、`UnityEngine.AssetBundleModule` あり | Managed、StreamingAssets | MOD から独自の AssetBundle を読む前例の有無 |
| アニメーション | `UnityEngine.AnimationModule`、本体の `EntityAnimation` | Managed、`.ref/dump/EntityAnimation.txt` | 本体が Animator と Playables のどちらで再生するか |
| MOD の仕組み | `DewMod`（`loadedInstances`、`RegisterJsonOverride`）、`ModBehaviour`。テンプレートは `Mods/ModTemplate`（TestMod）。公式文書はゲーム同梱の `Mods/Documentation.txt` が案内する ShapeOfDreamsModDocs | `.ref/dump/DewMod.txt`・`ModBehaviour.txt` | MOD フォルダ内の任意ファイル読み込み（本MODは icons/*.png で実績あり） |

### モデルの差し替えに関わる本体API

- `EntityModel : MonoBehaviour`：`bodyRenderers`、`healthBarPosition`、`deathBehavior`、`dissolveDelay/Duration`、`fxLoop/fxDeath/fxTakeDamage`、`idle`、`locomotion`、`walkAnimationSpeed`、8方向の走りクリップ、`stagger`、`death`、`abilityAnimationReplacements`、`weapon`、`customMappings`、`isInitialized`（`.ref/dump/EntityModel.txt`）。
- `EntityVisual.LoadModelLocal(EntityModel m)`：**初期化済みのインスタンスを渡すと例外**（`Provided model cannot be an already initialized model instance`）。fxPathTable を消し、既存の EntityModel の子を `DestroyImmediate` してから読み込む。ローカルだけの変更（`sod-decomp/Dew.Core/EntityVisual.cs` 699行〜）。`LoadModelDefault/LoadModelDefaultLocal` で元に戻せる。`ClientEvent_OnModelLoaded` がある。
- つまり EntityModel を使う方式は、**Prefab（未初期化のアセット）を各クライアントで用意する**必要があり、それには Unity Editor で作った AssetBundle が要る。

## 2. 同じゲームの MOD の前例（ワークショップ）

| MOD | 方式 | 参考になる点 |
| --- | --- | --- |
| Shape of Dreams VRM（0.4.0、r.1.4.0.13_s 対応） | **実行時に VRM（glTF）を読み込み**、元のキャラの Animator・技の位置・当たり判定・通信はそのままで、見た目の renderer だけを差し替える。骨格を本体のアニメーションに追従させる | AssetBundle を作らずに済む。協力では**ファイル名と SHA-256 の指紋だけ**をホスト経由で送り、各PCが自分のファイルから読む。持っていないPCでは元の見た目のまま。読み込み失敗時も元の見た目を残す。MOD 停止時に renderer・材質・Harmony を戻す |
| Nasus / Smolder / Riven など（独自の旅人） | assets は音声のみ確認（モデルは本体のものを流用している可能性。要確認） | — |

## 3. 導入方式の比較（判断材料）

| | A. 実行時に GLB を読み込む（VRM MOD と同じ考え方） | B. Unity 6000.0.77f1 で AssetBundle を作り、EntityModel Prefab を `LoadModelLocal` |
| --- | --- | --- |
| 制作環境 | Unity Editor 不要。glTF の読み込みライブラリを MOD に同梱 | 同じ版の Unity Editor＋URP が必要。OS ごとに BuildTarget 別のバンドル |
| アニメーション | GLB 内の Idle/Walk/Attack を MOD 側で再生（敵の移動速度・攻撃の開始に合わせる） | EntityModel の idle/run/abilityAnimationReplacements に割り当て、本体が再生 |
| 見た目の品質 | 材質は URP Lit/Unlit への変換。本体の溶解・被弾の点滅との相性は要調整 | 本体と同じ仕組みに乗る（溶解・死亡演出・体力バー位置） |
| 協力プレイ | 敵の生成・判定はホストの本体のまま。見た目のモデルIDと指紋だけ送る（既存の `DreamforgeVariantMsg` を拡張） | 同じ。バンドルの版の一致を確認する必要 |
| 危険 | glTF ライブラリと本体の DLL の衝突、材質の変換 | Editor の版違い・シェーダーの不一致でピンク表示、OS ごとのビルド |

**所見**：同じゲームで協力プレイまで動いている前例があるのは A。B は本体の演出に最もよく乗るが、制作環境の条件が重い。第2段（Import と Prefab の契約）に進む前に、利用者に A/B のどちらを主にするか決めてもらう。

## 4. 次に確かめること（第2段へ）

1. `EntityAnimation` の再生方式（Animator か Playables か）と、`abilityAnimationReplacements` の使われ方（逆コンパイル）。
2. A の場合：VRM MOD の使う glTF ライブラリの版と、本MODに同梱したときの DLL 名の衝突。
3. 敵の見た目差し替えの単位（Monster ごと）と、プールで再利用されるときに元へ戻す地点（既存の変種処理 `ApplyVariantVisual` と同じ地点が使えるか）。
4. 作成済み11体のうち1体（dream_eater）で、ソロで表示 → 協力で両方のPCに表示、の順で PoC。PoC はゲームの起動とMOD配置を伴うので、実施の前に許可を取る。
