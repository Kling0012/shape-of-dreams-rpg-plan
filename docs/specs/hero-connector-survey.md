# ヒーローコネクタ 調査：他MODの旅人をDreamforgeへ接続する（読み取りのみ）

2026-10-30 調査。コードは変更していない。対象はブランチ docs/hero-connector-survey（954ef0b3）。本体（Shape of Dreams r1.4.1.2）は逆コンパイル（sod-decomp）から読んだ。本文書には本体のコードを引用せず、型名・メソッド名と動作の要約のみを記す。

## 0. 前提

- 旅人のキーは `ClientSession.HeroKeyOf`（`src/SodRpg.Mod/ClientSession.cs:173`）で、Hero コンポーネントの型名（例: `Hero_Lacerta`）。未知の旅人もキーとしてはそのまま通る。
- 本体の9旅人: Hero_Vesper, Hero_Lacerta, Hero_Cetus, Hero_Yubar, Hero_Husk, Hero_Mist, Hero_Nachia, Hero_Aurena, Hero_Bismuth。
- 想定する形: 別のMODが `Hero_<新旅人>` 型とその内容（スキル・アセット）を追加し、Dreamforge 側のコネクタが装備・星図・連携・セーブなどの仕組みへ接続する。本調査はその前に、Dreamforge が旅人ごとに何を持ち、未知の旅人が今どこでどう扱われるかを確かめたもの。

## 1. Dreamforge が旅人ごとに持っている情報

### 1-1. Core の静的な旅人別データ（コード直書き）

| 場所 | 内容 | 値の型 |
| --- | --- | --- |
| `HeroNames.cs` | 旅人の表示名（ja/en。zh は用語集から変換） | 文字列テーブル。未知の旅人は `Hero_` を取った英語名（HeroNames.cs:9） |
| `HeroSigils.cs` | 基準の才能ツリー（印・深層ノード・キーストーン、KeystoneMastery=3） | TalentDef をコードで構築し `Trees` 辞書へ |
| `HeroStarRoutes.cs` | 記憶ごとのルート。62ルート・434星＋夢の輪72星（ファイル冒頭の注釈） | TalentDef テーブル。星IDは `h.<旅人>.route.<slug>.<番号>` |
| `PairCombos.cs` | ペア連携62組（`D(...)` 呼び出し62件）と、制約の旅人別説明文 | PairComboDef テーブル |
| `HeroTreeLayout.cs` | ルートの表示順（BranchOrder）と基準レイアウト | 辞書＋座標 |
| `Build.cs:85` | 移動チャージの有利を1人に固定（`MovementChargeHero = "Hero_Husk"`） | 定数 |
| `TravelerCombat.cs:11` | 近接旅人の判定（Vesper/Husk/Mist/Aurena の4人）と MeleeCoop の設定符号化 | コード（bool・ビット列） |

### 1-2. 生成されたスターマップ（tools/star-manifest → 生成C#）

| 場所 | 内容 | 値の型 |
| --- | --- | --- |
| `tools/star-manifest/<旅人>.json` | 監修済み星図の原典。9旅人分（約540〜740KB/人） | JSON（生成時のみ使う） |
| `src/.../StarClusters/<旅人>.Generated.cs` | 監修済みツリーと移行規則。パイプは gen_cs.py、読み取りは `ManifestReader.Generated.cs` に共通化（実行時にJSONや反射は使わない） | 生成C# |
| `StarMapPlacements.Generated.cs` | 星図の調整済み座標 | 生成C#（座標） |
| `StarClusters/GeneratedRegistration.cs` | 起動時の一括登録（`RegisterAllGenerated`。失敗は旅人ごとに警告に収める）。登録済み旅人の一覧（9人） | 実行時 |
| `Mechanisms/AuthoredStarRegistration.cs` | 監修ツリーの公開API（RegisterAuthored / RegisterMigrations / TryGetRegisteredTree など）と `AuthoredRegistryFingerprint` | 実行時 |
| `Mechanisms/AuthoredStarContract.cs` | 監修ツリーを与えるときの検証契約（後述） | 実行時 |

### 1-3. バランス表（tools/balance/*.json → 生成C#）

| 場所 | 内容 | 値の型 |
| --- | --- | --- |
| `coop.json` → `Balance/Coop.Generated.cs` | 協力の難易度スケール `HeroTargetScale`（旅人別） | double |
| `room-omens.json` → `Balance/RoomOmens.Generated.cs` | 部屋の予兆の試練スケール（旅人別） | double |
| `stars.json`（effects 7337件）→ `Balance/Stars.Generated.cs` | 星の効果の数値 | decimal 定数 |
| `run-growth.json` | Cetus/Vesper の遠征成長（run.g1/m1/m2/選択）と fishScales。監修星図（Cetus/Vesper.Generated.cs）の効果数値として織り込まれる | decimal |

### 1-4. 名前の登録簿と記憶の仕掛け

| 場所 | 内容 | 値の型 |
| --- | --- | --- |
| `Links.cs` | 旅人・記憶（St_*）・本質（Gem_*）の名前簿（非公開辞書）。リンク要件の判定 | 辞書と Txt |
| `Mechanisms/VerifiedMechanismSlots.cs` | 記憶 →（所有する旅人、仕掛け区分）。本体由来の記憶を含む | 辞書。指紋は ContentFingerprint の一部 |

### 1-5. セーブ（Profile / ProfileCodec）

| 場所 | 内容 | 値の型 |
| --- | --- | --- |
| `Profile.cs` `HeroState` | 旅人ごとの状態（装備・才能と割り当て・キーストーン・監修移行の版・LimboInfinityUnlocked・キル数＝熟練度・星XP・星の組み合わせ） | キーは旅人キー。なければ作る（未指定は "default"） |
| `ProfileCodec.cs` | profile.json の `heroes` オブジェクト。実行中は `run.heroKey` | キー文字列をそのまま読み書き |

### 1-6. Mod側（UI と型を限定したパッチ）

| 場所 | 内容 | 値の型 |
| --- | --- | --- |
| `DreamforgeUi.GearColumn.cs` | 装備欄の旅人選択（KnownHeroes 9人の並び＋Profile.Heroes.Keys の追記） | 配列。未知の旅人も選択肢に出る |
| `DreamforgeUi.cs` | 画面の旅人キーの決定（ローカル → ロビー → Profile の最初の `Hero_*` → 既定）と表示名 | コード |
| `NativeMistLifetime.cs`、`HostAuthority.CrowdControlRecovery.cs` | Hero_Mist の事故対策（消えた目標への移動停止など） | 型限定パッチ |
| `HostAuthority.StunSourceFilter.cs` | Hero_Cetus の静穏キー限定（Cetus 以外の呼び出しは InvalidOperationException） | 型限定パッチ |
| `NativeBismuthAutoCast.cs` | Se_Star_Bismuth_D_SkillHasteAndAutoCast への自動詠唱 | 型限定パッチ |
| `HostAuthority.PairCombos.cs` | Hero_Nachia のペア特例 | コード |

### 1-7. 旅人のキーで汎用にできている仕組み（参考）

任務（Bounty.cs）、実績（Feats.cs）、インフィニティの記録（InfinityRecords.cs / InfinityRewards.cs）、図鑑、日次の熟練度（DailyDream.cs）は旅人キーを使わない。`Rules.cs` の割り当て検証は heroKey を引数で通し、`Onboarding.AutoEquipStarter`、`HostBuildValidation`（.Compatibility.cs 含む）も同様に汎用。

## 2. 未知の旅人での現在の挙動

### 2-1. フォールバックで動く（落ちない）

| 箇所 | 挙動 |
| --- | --- |
| `HeroNames.Of` | `Hero_` を取った英語名。テスト済み（StarMapViewTests.cs:152 の `Hero_Unknown`） |
| `HeroSigils.TreeFor`（:222） | 汎用ツリー `Content.Talents`（既定の動作。注釈も明記） |
| `HeroTreeLayout.ForHero`（:86） | 汎用レイアウト（汎用ツリーの輪） |
| `StarMapPlacements.Generated` | 調整座標なし → 既定の扇状配置（警告ログのみ） |
| `Coop.Generated` `HeroTargetScale` | `default: return 1d;` |
| `RoomOmens.Generated` `TrialScale` | `default:` で既定スケール 1.0 |
| `VerifiedMechanismSlots` | TryGetValue ベース → 空/false。記憶の仕掛け区分は無いだけ |
| `Profile.Hero` / `ProfileCodec` | 旅人キーを汎用に読み書き。未知の旅人の状態もそのまま保存される |
| `HostBuildValidation.TryAccept` | 旅人キー自体は引数扱い。星IDはホスト側の登録で検証（4-3） |
| 装備欄のUI | Profile.Heroes.Keys から未知の旅人を選択肢に足す |
| 型限定パッチ | 対象の型でなければ何もしない |

### 2-2. 例外を投げる（コネクタが当たる門）

すべて `Links.IsTraveler`（Links.cs:216、名前簿は既知9人）で弾かれる。未知の旅人キーで次のAPIを呼ぶと例外になる。

| 箇所 | 例外 |
| --- | --- |
| `AuthoredStarRegistration.RegisterAuthored`（:61, :116） | ArgumentException |
| `AuthoredStarRegistration.RegisterMigrations`（:61, :69） | ArgumentException（ツリー未登録なら InvalidOperationException） |
| `StarClusters.ValidateCluster`（:136） | InvalidOperationException "Unknown hero." |
| `GeneratedRegistration.RegisterGeneratedHero`（:59, :77） | ArgumentException "No generated star map for ..." |
| `AuthoredStarContract.GenerateAuthored`（:169, :178） | 既存の才能が汎用ツリー（HeroKey=null）なら "Invalid existing talent"、監修定義も全件 IsTraveler が必須 |

つまり、未知の旅人に星図を与える正規の入口は今は無く、登録系はすべて門が閉じている。加えて次の制約がある。

- `Links.RequirementSatisfied`（:284）: 未知の旅人を対象にしたリンク要件は満たせない（例外にはならない）。
- `AuthoredStarContract` の `HeroMemory`: 記憶は Links.MemoryNames にあり、既存ツリーのルート記憶でなければならない。他MODの新記憶はここで拒否される。
- `HostAuthority.StunSourceFilter.ConfigureCalmStun`: Cetus 以外で InvalidOperationException。ただしCetus限定の内部契約で、未知の旅人とは無関係。

### 2-3. 機能が欠けるだけ

- 近接判定（`TravelerCombat.IsMelee`）: 4人以外は近接扱いにならず、MeleeCoop も無効。
- ペア連携・記憶ルート・監修星図・調整座標・旅人別スケール・VerifiedMechanismSlots: 未知の旅人の分は存在しない（`PairCombos.StarName` は未知の星IDで「前提の星」に落ちる）。

### 2-4. セーブへの影響

未知の旅人のキーで保存が壊れる箇所は見つからなかった。`heroes` オブジェクトはキーを汎用に扱い、検証（HostBuildValidation）は拒否であって破壊ではない。残る案件は、汎用ツリーの割り当てを持った旅人へ後から監修ツリーを登録するときの移行で、`RegisterMigrations` の仕組みが既にある。

## 3. 本体のMOD読み込み機構（逆コンパイルより）

### 3-1. フォルダ構成と metadata.json

- MODは `Mods/` 配下のフォルダ1つ。必須は `about/metadata.json` のみ。任意で about/icon.png、preview.png、description.txt、publishedfileid.txt。
- metadata.json の欄は id、name、author、modVer、supportedGameVer、assemblies（DLL 名の配列、ワイルドカード可）。Dreamforge の実例: `{"id":"com.kling0012.dreamforgerpg","name":"Dreamforge RPG","author":"Kling0012","modVer":"3.8.84","supportedGameVer":["*"],"assemblies":["DreamforgeRPG.dll"]}`。
- **依存関係を書く欄はない。** 読み込み順の解決もない。

### 3-2. 読み込みの流れ

1. `DewMod.ReloadFromActiveMods` が `DewSave.platformSettings.activeMods`（利用者が並べ替えた順）の順に読む。
2. `DewMod.Load` は DLL のバイト列を読み、MonoCecil でアセンブリ名に `_`＋時刻印を付けてから Assembly.Load する（名前の衝突避け。型の FullName は変わらない）。
3. ModBehaviour のサブクラスを探して AddComponent し、`harmony`（IDは metadata.id）を渡す。
4. MODフォルダ直下の `overrides/` はJSON差し替え（`RegisterJsonOverride`）。**対象は DewResources のデータベースに既にある物だけ。新規アセットは作れない。**

### 3-3. 他MODのDLL・型を探す方法

- `DewMod.loadedInstances` / `DewMod.allMods`（ModItem に metadata とフォルダパス）で存在と並びを確認できる。
- 型は `Dew.GetTypeFromShortName`、または AppDomain のアセンブリ走査（アセンブリ名は変わっても型名は保たれる）。
- 読み込みの節目は `DewMod.onModLoadStarted / onModLoadEnded`。順序の保証は無いので、コネクタ側で相手の準備を待つ仕組みが要る。

### 3-4. 本体が旅人を列挙する場所と解錠

- `Dew.InitAllTypeReferences`（アセンブリ読込後の初期化）が AppDomain 内の Hero サブクラスを集め、`Dew.allHeroes` へ入れる。MODの型も自動的に入る。
- ロビーの一覧（UI_Lobby_HeroList）は `Dew.HeroOrder` 順（未知は 9999＋名前の順）に並べ、`Dew.IsHeroIncludedInGame` で絞り、`DewResources.GetByShortTypeName<Hero>` でプレハブを得る。
- 解錠は `DewSave.profileMain.heroes[型名]` の UnlockData（無ければ Locked）。`DewProfile.UnlockHero` で解錠済みにできる。

### 3-5. 旅人を追加するのに必要な物

型（Hero のサブクラス）は自動で列挙される。必要なのは次の2つ。

1. Hero プレハブを DewResources のデータベースに登録すること。データベースは `Resources.Load("MainResources")` から作られ、overrides では追加できない。登録の実際の方法は **未確認**（ワークショップには独自旅人MODの前例があるので、何らかの方法はある。公開辞書の直接変更か、リソース側の仕組みかは確かめられていない）。
2. 解錠状態の設定（既定は Locked）と、スキル一式の用意。

なおクライアント側の生成は `SpawnManager.SpawnFromDewDatabaseHandler` がアセットIDからプレハブを解決する。見つからなければ LogError で null を返す（4-4）。

## 4. 協力プレイ

### 4-1. Hello と機能の広告

- `ClientSession.Hello.cs` / `HostAuthority.Hello.cs` が DreamforgeHelloMsg（protocol、modVer、内容の指紋、インフィニティ可否、meleeCoop、継続情報）を交換する。違っても**警告のみで機能は続行**。装備・報酬・インフィニティの利用条件にはしない設計。
- 機能単位の広告（DreamforgeBuildInputCapabilityMsg: netLite、roomOmens、forbiddenSmith、宿敵、状態写像、自動詠唱圧など）は、Hello を受けた相手だけ有効になる。新旅人の有無を伝える欄は今は無い。

### 4-2. 内容の指紋（ContentFingerprint）

`ContentFingerprint` は `HeroSigils.All`・`PairCombos.All` のID、`VerifiedMechanismSlots.Fingerprint`、`StarClusters.AuthoredRegistryFingerprint` などを含む。コネクタが旅人を登録すると指紋が変わり、登録していない側には「内容（星・遺物などの登録）が一致しない」警告が出る。機能は止まらない。

### 4-3. ホストの装備検証（新旅人を片方だけが持つ場合）

`HostAuthority.BuildValidation.ProcessBuildUpdates` は参加者の装備を `ClientSession.HeroKeyOf` の旅人キーで受け、`HostBuildValidation.TryValidateAllocation`（:149）で**ホスト側の登録に無い星IDを「割り当て」不正として拒否**する（例外は受け止めて拒否理由へ変える。1人の失敗が他の参加者を巻き込まない）。つまり参加者が新旅人で星を取っても、ホストにその登録が無ければ Dreamforge の効果は乗らない。本体の処理としては、追加効果の計算はホスト側の登録（HeroSigils と監修ツリー）から来る。

### 4-4. 本体側（旅人のアセットが無いクライアント）

旅人のアセットを持たないクライアントでは、SpawnManager がアセットIDからプレハブを解決できず、LogError と null が返る（その旅人はその画面に現れない）。例外で止まる報告は逆コンパイル上は無い。クロスプラットフォームのロビーではMODの再読み込みが禁止され、ロビーの isModded 表示は MOD由来の Actor 型名のハッシュから作られる。

## 5. 未確認の点

1. **他MODが Hero プレハブを DewResources のデータベースへ登録する実際の方法。** ワークショップの独自旅人MOD（Nasus / Smolder / Riven など）が存在するので手段はあるが、この環境では `Mods/Documentation.txt` と `Mods/ModTemplate` が無く、機構を確認できなかった（issue19 調査でも未確認のまま）。接続の前提になるため、実装の前にワークショップ側の作例かドキュメントを確認する必要がある。
2. `Dew.ClearTypeReferences` の呼び出し元（再走査の有無）。MODの遅い読み込みで allHeroes が増えるかどうかに関わる。
3. UI_Lobby_HeroList の `Dew.IsHeroIncludedInGame` の中身（除外条件の詳細）。
4. 実機での挙動（本調査はコード読みのみ。実行はしていない）。

## 6. 接続の要になる箇所（要約）

- 旅人のキー: `ClientSession.HeroKeyOf`（型名）で、すべて汎用に流れている。
- 閉じた門: `Links.IsTraveler`（名前簿）が `RegisterAuthored` / `RegisterMigrations` / `ValidateCluster` / `GenerateAuthored` の入口を占める。コネクタはここ（名前簿の追加、または別入口）を開く形になる。
- 既定の動作: 汎用ツリー・汎用レイアウト・スケール1.0・名前は型名から。未知の旅人でも落ちず、保存も壊れない。
- 協力: 指紋の不一致は警告止まり。効果の実体はホスト側の登録で決まり、無ければ拒否（拒否であって破壊ではない）。
