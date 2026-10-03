# MOBモデル導入: 10体ソースと方式Bの実装（draft）

> この時点のdraftはアセット公開の追加承認待ちです。10体のmanifestと検査/展開ツールは含みますが、63個のモデル実データの分割アーカイブはまだこのPRへ公開していません。クリーンcheckoutからの素材展開・素材CIは公開完了まで実行できません。

関連: [Issue #19](https://github.com/Kling0012/shape-of-dreams-rpg-plan/issues/19)、[第1段の調査](issue19-model-import-research.md)、[API根拠](mob-api-compatibility.md)。土台は PR #4 の `beaa1ab5685fa248ba2d47c1fbaaebee3181a42e`。PR #4 の実装ブランチへ積む変更で、古い main を土台にしない。

## 返信を受けた位置づけ

Issueの返信は「調査を始めた」「Unity 6000.0.77f1」「A=実行時GLBとB=AssetBundleの方式判断が必要」としている。今回の追加依頼に合わせ、**B案のレビュー可能なコードと制作手順**を用意した。A/Bを利用者が最終決定したとの記録ではない。方式AのImporter導入や他MODへの依存は追加していない。

これは既存Monsterのモデル差し替えの実装であり、独自AI・新しいネットワークPrefab・固有ドロップを持つ新Mob登録ではない。既存の出現・AI・攻撃・当たり判定・死亡・報酬はゲームと既存Dreamforgeに任せる。10体すべての制作元実ファイルを同梱するが、完成したEntityModel prefab/bundleや実機導入済みの成果物ではない。

## 同梱内容と検査

`assets/dreamborne/manifest.json` は全10体（炉のゴーレム、茸獣、鴉の魔導師、鹿、狼、亀、蛾、蠍、灯の精、鐘クラゲ）のFBX/GLB/Blend/パレットを記録する。独立メッシュ10、三角形20,562、骨209、Idle/Walk/Attack各1つで30クリップ。最初の dream_eater は今回の10体には含めない。

実バイナリは小分けの再現可能な圧縮アーカイブで管理し、次のコマンドで展開する。展開時にアーカイブ・各ファイルのサイズ/SHA-256・パスを照合する。GitHub投稿APIの大容量リクエスト制限に対応する保存形式で、外部の個人保管先への参照ではない。

```sh
python tools/materialize_mob_assets.py
python tools/validate_mob_assets.py
python -m unittest discover -s tests/assets -v
python -m unittest discover -s tools/MobAssetBuilder/tests -v
dotnet test -c Release
dotnet test tests/MobRuntime.Tests/MobRuntime.Tests.csproj -c Release
```

FBX/Blendの内部にあった制作端末のパスだけを取り除き、元版と配布版のハッシュを両方記録した。GLB/PNGは元のバイト列と同じ。Blenderで20個のFBX/Blendを独立して開き、元データとの形状・骨・ウェイト・アニメーションの比較に成功した。Unity Importや本体戦闘の証明とは区別する。利用条件は同梱READMEを参照し、未指定のOSSライセンスを追加しない。

## ローダーと同期

- MOD直下の `models/manifest.json` と指定された単一bundleのみをローカルで読み込む。ネットワークからモデルは取得しない
- 明示的schema、内容版、Unity版、Application.version、BuildTarget、bundle SHA-256、exactな本体Monster型→modelId→prefabパスを検査。ファイル欠落・破損・不明な型・初期化済みPrefab・不正コンポーネント・イベント入りクリップは拒否
- 参加者間ではmanifestの正確なバイト列のSHA-256と配布DLLのSHA-256も照合。同じモデルIDでも中身やコードが違えば有効化しない
- ホストが人間の全参加者と自身の準備応答を確認。誰かが未導入・無効・版違い・タイムアウトなら、全員へ既存モデルへの復元を送る。認識できない曖昧な再利用IDも全員の停止へ伝える
- 人間の識別はゲームから渡される送信者オブジェクトを使い、パケット内の自己申告IDを信用しない。クライアントnonceへの応答でホストsession/roomを合意し、古いrevision・重複・遅延・古い準備応答を除外
- 小さな完全スナップショットを1秒ごとに再送。6秒の通信期限、room世代とspawn世代、300個体/4096履歴の上限を設ける。途中参加・出現前に届く通知は同じ状態から再試行
- Listen hostはローカル経路も明示的に処理。未初期化のPrefabを `LoadModelLocal` に渡し、実際に生成されたモデルだけを所有状態として追跡
- 復元は `LoadModelDefaultLocal()`。元の初期化済みmodelを再投入しない。他MODが後から変えたモデルは上書きしない。復元失敗時は使用中アセットを破壊せず保持し、以後は停止とログを優先

互換性が同じOS/BuildTargetまで要求される保守的な初版であり、Windows/macOS/Linuxをまたぐ協力を検証済みとは扱わない。任意のAssetBundleを安全にする仕組みではない。自分が検査・ビルドした信頼できるパックだけを使う。

## Unity制作と導入

[ビルダー手順](../../tools/MobAssetBuilder/README.md)を使用する。ソースImportだけを行う経路と、検証済みテンプレートを必要とする本番bundle経路を分けた。

1. 記録されたUnity 6000.0.77f1と、正確なURP版・ゲームSDKを用意する。ゲームDLL/SDK/元ゲーム素材は本リポジトリ・配布ZIPへ含めない
2. 各モデルに、検証した基底Monster型・同ビルドのEntityModelテンプレート・骨/体力バー/武器/FXの接続位置・Idle/Walk/Attackのserialized bindingを明示する
3. Generic FBX、既存palette、root motionなしでImport。足元、前方、skin/bounds、shader、アニメの全動作を確認する
4. 不足するSpawn/Stagger/Deathと攻撃アニメの接続を確認。`abilityAnimationReplacements`の存在は調査済みだが、個別Ability名・要素型・攻撃タイミングはまだ作業入力にない。Idleを死亡/攻撃として黙って代入しない
5. 単一BuildTargetのbundleを生成・再ロード確認し、hash付きruntime manifestを出力する。実機で品質・攻撃点・判定が一致するまで出荷しない
6. Modは `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release -p:GameDir=... -p:DeployModToGame=false` でビルドする。**通常ビルドから本番Modsへ自動コピーする既存動作は止めた**。配布は `tools/package_mob_mod.py` で隔離ZIPへまとめる

設定 `customMobModels` の初期値はfalse。正しいpackを配置し全員が明示的に有効化した場合だけ使用する。`dreamforge_mobstatus` で停止理由・Unity版・Application.version・ターゲットを確認できる。ゲーム起動・本番配置・セーブ変更・Workshop公開はこのPRの作業には含めていない。

## 未実施の出荷条件

このクラウド環境にはゲームDLL/Unity Editor/ライセンスがなく、次は未実施。純C#テストやengine doublesをその代わりにはしない。

- 実ゲームDLLでのMod全体コンパイルとUnity Editorコードのコンパイル
- 実UnityのImport/Prefab/AssetBundle生成。正しいURP版、各基底敵とattack bindingの確定
- 2台以上でホスト役を交換し、同じ個体・モデル・攻撃/死亡状態が一致する確認
- 遅延、途中参加、切断、ホスト/クライアントのみの再読み込み、部屋移動、プール再利用、他モデルMODとの競合
- 最大人数/同時個体数でCPU/GPU/GC/メモリ測定と回帰。ピンク表示/Tポーズ/足滑り/モデルと当たり判定の差、部屋クリア・報酬の整合

これらを満たすまでdraft扱いで、Issue #19を「完了」「実機マルチ対応済み」として閉じない。
