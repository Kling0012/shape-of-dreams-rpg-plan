# MOBモデル導入: 10体ソースと方式Bの実装（v2.7.1向けの移植）


最終的な判断・作業分解は [Issue #19 統合仕様](issue19-integration-spec.md) に集約した。本書は旧ブランチ `feat/mob-model-integration` の資材を main v2.7.1 上に移植した現行版である。

関連: [Issue #19](https://github.com/Kling0012/shape-of-dreams-rpg-plan/issues/19)、[第1段の調査](issue19-model-import-research.md)、[API根拠](mob-api-compatibility.md)、[再構成計画](mob-visuals-v2-plan.md)。

## 返信を受けた位置づけ

Issueの返信は「調査を始めた」「Unity 6000.0.77f1」「A=実行時GLBとB=AssetBundleの方式判断が必要」としている。方式Bのレビュー可能なコードと制作手順を維持する。方式AのImporter導入や他MODへの依存は追加していない。

これは既存Monsterのモデル差し替えの実装であり、独自AI・新しいネットワークPrefab・固有ドロップを持つ新Mob登録ではない。既存の出現・AI・攻撃・当たり判定・死亡・報酬はゲームと既存Dreamforgeに任せる。10体すべての制作元実ファイルを同梱する。

## 同梱内容と検査

`assets/dreamborne/manifest.json` は全10体（炉のゴーレム、茸獣、鴉の魔導師、鹿、狼、亀、蛾、蠍、灯の精、鐘クラゲ）のFBX/GLB/Blend/パレットを記録する。独立メッシュ10、三角形20,562、骨209、Idle/Walk/Attack各1つで30クリップ。最初の dream_eater は今回の10体には含めない。

実バイナリは全88断片・合計5,745,387バイトの再現可能な圧縮アーカイブで管理し、次のコマンドで展開する。展開時にアーカイブ・各ファイルのサイズ・パス・完全なエントリ一致を照合する（ハッシュ照合は使わない。`manifest.json` は必須の `contentVersion` 文字列で内容版を識別し、各エントリは `bytes` を持つ）。大容量の一括転送がこの作業経路で完了しなかったため、小さな単位で転送・照合できる保存形式にした。

```sh
python tools/materialize_mob_assets.py
python tools/validate_mob_assets.py
python -m unittest discover -s tests/assets -v
python -m unittest discover -s tools/MobAssetBuilder/tests -v
```

FBX/Blendの内部にあった制作端末のパスだけを取り除いた。GLB/PNGは元のバイト列と同じ。Blenderで20個のFBX/Blendを独立して開き、元データとの形状・骨・ウェイト・アニメーションの比較に成功した。Unity Importや本体戦闘の証明とは区別する。利用条件は同梱READMEを参照し、未指定のOSSライセンスを追加しない。

## ローダーと同期

- MOD直下の `models/manifest.json` と、そこに列挙された10個のモデル別bundleのみをローカルで読み込む。ネットワークからモデルは取得しない
- 明示的schema、`contentVersion`（制作ツールが再構築するたびに新しい値になる）、各bundleのファイルサイズ、Unity版、Application.version、BuildTarget、exactな本体Monster型→modelId→prefabパスを検査。ファイル欠落・破損・不明な型・初期化済みPrefab・不正コンポーネント・イベント入りクリップは拒否
- 参加者間では `contentVersion`・各bundleの大きさ・Unity版・ゲーム版・BuildTarget・配布DLLの同一性で照合する。同じモデルIDでも中身やコードが違えば有効化しない
- ホストが人間の全参加者と自身の準備応答を確認。誰かが未導入・無効・版違い・タイムアウトなら、全員へ既存モデルへの復元を送る。認識できない曖昧な再利用IDも全員の停止へ伝える
- 人間の識別はゲームから渡される送信者オブジェクトを使い、パケット内の自己申告IDを信用しない。クライアントnonceへの応答でホストsession/roomを合意し、古いrevision・重複・遅延・古い準備応答を除外
- 小さな完全スナップショットを1秒ごとに再送。6秒の通信期限、room世代とspawn世代、300個体/4096履歴の上限を設ける。途中参加・出現前に届く通知は同じ状態から再試行
- Listen hostはローカル経路も明示的に処理。未初期化のPrefabを `LoadModelLocal` に渡し、実際に生成されたモデルだけを所有状態として追跡
- 復元は `LoadModelDefaultLocal()`。元の初期化済みmodelを再投入しない。他MODが後から変えたモデルは上書きしない。復元失敗時は使用中アセットを破壊せず保持し、以後は停止とログを優先

互換性が同じOS/BuildTargetまで要求される保守的な初版であり、Windows/macOS/Linuxをまたぐ協力を検証済みとは扱わない。任意のAssetBundleを安全にする仕組みではない。自分が検査・ビルドした信頼できるパックだけを使う。

## Unity制作と導入

[ビルダー手順](../../tools/MobAssetBuilder/README.md)を使用する。ソースImportだけを行う経路（`ImportSources`）、ゲームSDK・テンプレートを使わない10体分のbundle生成（`BuildSources`）、検証済みテンプレートを必要とする本番経路（`Build`）を分けた。

1. 記録されたUnity 6000.0.77f1と、正確なURP版・ゲームSDKを用意する。ゲームDLL/SDK/元ゲーム素材は本リポジトリ・配布ZIPへ含めない
2. 各モデルに、検証した基底Monster型・同ビルドのEntityModelテンプレート・骨/体力バー/武器/FXの接続位置・Idle/Walk/Attackのserialized bindingを明示する
3. Generic FBX、既存palette、root motionなしでImport。足元、前方、skin/bounds、shader、アニメの全動作を確認する
4. 不足するSpawn/Stagger/Deathと攻撃アニメの接続を確認。`abilityAnimationReplacements`の存在は調査済みだが、個別Ability名・要素型・攻撃タイミングはまだ作業入力にない。Idleを死亡/攻撃として黙って代入しない
5. 単一BuildTargetについてモデル別（10個）のbundleを生成・再ロード確認し、`contentVersion` と各bundleの大きさ入りのruntime manifestを出力する。実機で品質・攻撃点・判定が一致するまで出荷しない
6. Modは `dotnet build src/SodRpg.Mod/SodRpg.Mod.csproj -c Release -p:GameDir=... -p:ModDeployDir=...` でビルドする（自動配置先を実機Modsの外へ必ず指定する）。配布は `tools/package_mob_mod.py` で隔離ZIPへまとめる

この移植で実際に実行したのは、素材の展開・検査、隔離プロジェクトの準備、Unity 6000.0.77f1 によるモデルのみ（テンプレート未適用）の10 bundle生成と、梱包ツールによる隔離出力までである（Unityログ: `C:\Temp\mob-unity-build.log`、成果物: `C:\Temp\mob-build-output`、配布形: `C:\Temp\mob-package`）。ゲーム実機・マルチプレイは一切実行していない。以下は未確認のまま残る項目（旧PRの記録をそのまま根拠とし、推測で埋めていない）:

## 未確認
1. **URPパッケージの正確な版**: ゲームのDLLには版が埋まらない。本環境では Editor 6000.0.77f1 付属の Universal 3D テンプレートが固定する `17.0.4` を使用した。ゲーム側の実際の版一致は未確認
2. **`Application.version` の実測値**: `version.txt` のラベル `r.1.4.0.13_s` を gameVersion に記録したが、実行時トークンとの一致は未確認
3. **EntityModel テンプレート**: 同一ビルドのゲームPrefabを実物から用意できていない。今回のbundleは `BuildSources`（テンプレート未適用・EntityModelなし・`baseMonsterTypes` 空）で作った制作物であり、このままでは実機では適用されない。実機適用には G2 の実物ダンプが必要
4. **基底Monster型の対応**: [Issue #19 統合仕様](issue19-integration-spec.md) §5.2 の候補表（`frostjaw_prowler`→`Mon_Forest_Hound` ほか）は仮説のまま。型名の実在は本リポジトリの Variants で確認できるが、攻撃・移動との適合は未確認
5. **Attack の結線（`abilityAnimationReplacements`）**: メンバーの存在は◎だが、要素の形・Ability名・キー・攻撃タイミングは未確認。Attackクリップをどのslotへ割り当てるか確定していない
6. **見た目・アニメの品質**: 向き・足元高さ・スケール・溶解・変種色・体力バー位置・bounds/culling は実機表示をまだ見ていない
7. **マルチプレイ**: 同一pack・欠落・版違い・途中参加・切断・再読込・プール再利用の各ケースは、実機2台で未実施
8. **性能**: 同時出現時のCPU/GPU/GC/メモリ・読込時間の実測は未実施（暫定ゲートは同数の元モンスターとの差分判定）
9. **別OS/BuildTarget**: Windows（StandaloneWindows64）のみ。macOS/Linux bundleは未生成

これらが確定するまで、実機適用・配布は行わない。
