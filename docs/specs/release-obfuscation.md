# 配布用 DLL の名前の難読化

DreamforgeRPG.dll の型・メソッド・フィールド・プロパティ・イベント・引数の名前を、配布用ビルドだけで難読化する。気軽な逆コンパイルで処理を読み取りにくくすることが目的であり、解析を完全に防ぐものではない。

## 道具と設定

難読化には MIT ライセンスの Obfuscar.GlobalTool 2.2.50 を使う。検査には Python 3、.NET SDK、Mono.Cecil 0.11.6 を使う。今回のビルド環境は .NET SDK 10.0.111。検査用のプログラムは net8.0 を対象とし、check.py が `DOTNET_ROLL_FORWARD=LatestMajor` を指定して実行する。Mono.Cecil のパッケージは初回の検査で復元する。

```sh
dotnet tool install --global Obfuscar.GlobalTool --version 2.2.50
dotnet build src/SodRpg.Mod -c Release \
  -p:GameDir="$HOME/dev/sod-gamedata/Shape of Dreams" \
  -p:ModDeployDir=/tmp/obf-x -p:Obfuscate=true
```

Obfuscar は `~/.dotnet/tools/obfuscar.console`、次に PATH から探す。別の場所を使う場合は `-p:ObfuscarCommand=/path/to/obfuscar.console` を渡す。プロパティなしのビルドでは難読化も検査も実行しない。

設定は `tools/obfuscation/obfuscar.xml` に置く。`HideStrings=false` と `OptimizeMethods=false` を明記し、処理の流れの変更、文字列の暗号化、実行時の復号、アンチデバッグは使わない。`SuppressIldasm=false` で逆アセンブルを拒む属性の追加も無効にする。実行時に追加の道具や DLL を読み込む必要はない。

## ビルドと配布物

`ObfuscateReleaseDll` は Build の後、DeployToGame の前に実行する。入力には obj のコンパイラ出力を使い、増分ビルドで難読化を二重に掛けない。出力は `src/SodRpg.Mod/bin/Release/netstandard2.1/DreamforgeRPG.dll` に置き換える。通常ビルドに戻すと、bin にも難読化前の DLL がコピーされる。

作業用の DLL と名前の対応表は `src/SodRpg.Mod/obj/Release/netstandard2.1/obfuscate/` に置く。`DreamforgeRPG.plain.dll` と `out/Mapping.txt` は開発用であり、配布物に含めない。配置ターゲットがコピーするのは検査に通った DLL、about、icons のみ。

Obfuscar や Python がない場合、難読化に失敗した場合、出力がない場合、検査に失敗した場合はビルドを失敗させる。検査を省略するプロパティは設けない。失敗したビルドでは bin にコンパイラ出力が残ることがあるため、配布処理は必ずビルドの終了コードを確認し、失敗時にパッケージを作らない。`release2.sh` は今回の変更対象外であり、配布側で `Obfuscate=true` を指定する変更が別途必要。

DeployToGame は従来どおり `$(GameDir)/Mods` が存在するときだけ動く。今回のゲームデータにはこのディレクトリがないため、`ModDeployDir` へのコピーは未実行。bin の DLL は直接検査した。

## 除外する名前

一覧の根拠はゲーム側の DewMod・ModBehaviour と MOD の保存・通信・リフレクション処理。具体的な型名は obfuscar.xml に記載する。分類には重複があり、合計は476型。以下の件数は2026-10-18に main の `91728d99` を取り込んだソースで確認した値。

| 分類 | 件数 | 代表例と理由 |
| --- | ---: | --- |
| 入口・設定・Unity の完全除外 | 4型 | DreamforgeMod、DreamforgeConfig、FrostFxLifetime、NativeDreamContent/SeedTemplateTag。コールバック名、設定のフィールド名、InvokeRepeating の呼び出し名を保つ。 |
| JSON・設定のメンバーを保つ明示指定 | 63型 | DreamforgeBuildMsg、DreamforgeCoopTradeUp/Down、InfinityMode/Envelope、InfinityRunState。フィールドとプロパティが JSON のキーになる。上の4型も含む。 |
| 通信の DTO | 36型 | Dreamforge*Msg と DreamforgeCoopTradeUp/Down。CustomRpc は単純型名、Mirror の NetworkMessage は完全型名を通信の識別に使う。 |
| Harmony の属性付きパッチ | 298型 | HarmonyPatch の付いた型。型・メソッド・プロパティの名前とメソッドの引数名を保つ。508個のパッチメソッド、642個の引数名を比較する。 |
| enum | 104型 | 全 enum の型名・列挙値・数値を保つ。ToString や名前による変換が保存・通信・表示のキーを変えないようにする。 |
| 手動パッチと変数経由の検索 | 10型 | NativeDreamContent、NativeProcSpawnSpread、NativeKillRewards、PressureEnemyCount、CulinaryIngredientCap など。nameof を含む文字列でメソッドを探す。 |
| 入れ子の型の親 | 7型の明示指定と手動パッチ側の親 | InfinityMode、HostAuthority、DreamforgeUi、ClientSession など。子の DTO・enum の完全名が変わらないよう、親の型名を保つ。 |
| 型名による判定 | 11型の明示指定 | essence-caps の10パッチと Protocol。設定によるパッチの選択、通信エラーを記録する名前空間の判定を保つ。パッチには Harmony の除外規則も適用する。 |

DewMod は metadata.json の assemblies にある `DreamforgeRPG.dll` を読み、ModBehaviour を継承する型を列挙して AddComponent する。ModBehaviour は公開フィールドから設定を探し、`GetType().Name + "." + field.Name + ".json"` で設定ファイル名を作る。そのため DreamforgeMod と config の名前を保つ。

profile.json と途中再開データの ProfileCodec は、明示した文字列のキーと数値で読み書きする。Profile や ProfileCodec の内部名を難読化しても、そのキーは変わらない。Newtonsoft.Json がフィールドを列挙する DTO は別に除外し、参照先をたどった161型について設定の漏れを検査する。通信の JSON と enum の名前も保つため、旧版との通信形式は変更しない。

リソース名と IL の文字列リテラルも難読化前後で比較する。Loc のキー、固定のログ文、保存・通信のキーはこの比較で確認する。将来、MOD 内の型やメンバーを名前で検索する処理、DTO、Unity のコンポーネントを追加するときは除外設定も更新する。検査の名前検索の検出は型トークンと文字列の照合であり、任意の動的なリフレクションを証明するものではない。

enum はすべて一括規則（`typeinherits="System.Enum"`）で型名と列挙値を保つため、トップレベルに enum を足しても obfuscar.xml への個別の指定は要らない。enum や DTO を型に入れ子にしたときだけ親型の指定を足す必要があり、取りこぼしは check.py が検出する（2026-10-18、`ClientSession/SkillBlock` で検出し、親の `ClientSession` を追加した）。

## 検査コマンドと結果

ビルド内で同じ検査を実行する。単独で実行する場合は、リポジトリのルートから次を使う。

```sh
python3 tools/obfuscation/check.py \
  src/SodRpg.Mod/obj/Release/netstandard2.1/obfuscate/DreamforgeRPG.plain.dll \
  src/SodRpg.Mod/bin/Release/netstandard2.1/DreamforgeRPG.dll \
  tools/obfuscation/obfuscar.xml
```

check.py は Metadata/ の小さなプログラムで Mono.Cecil のメタデータを JSON に取り出して比較する。DLL をゲームへ読み込まず、C# 全文の逆コンパイルも行わない。以前の ILSpy による全文検査は8分で終わらなかったため置き換えた。最終の難読化ビルドは検査込みで約29秒だった。

今回実行した結果は次のとおり。

- `Obfuscate=true` の Release ビルドと、bin の DLL に対する単独検査が成功した。1,527型のうち1,051型、68.8%の型名が変更された。
- 除外した476型と、1,424フィールド、56プロパティ、898メソッドの名前を確認した。メソッドを保護する型では引数名も比較した。
- Harmony の型・メソッド・属性・引数名、通信の36型、JSON の参照先161型、104 enum の名前と値を確認した。
- アセンブリ名、参照 DLL の一覧、埋め込みリソース名、35,554個の文字列リテラルが変わっていないことを確認した。
- ObfuscarCommand に存在しないパスを指定したビルドが失敗した。成功済みの出力が作業ディレクトリに残っていても失敗する。難読化前の DLL を比較先に渡すと検査が失敗することも確認した。
- プロパティなしの Release ビルドが成功した。難読化後に通常ビルドへ戻し、bin に `SodRpg.Core.Game.Build` が元の名前で存在することも確認した。
- SodRpg.Mod.Startup.Tests は192件成功、0件失敗、1件スキップ。Issue73.Native.Tests は308件成功、0件失敗、0件スキップ。

Startup.Tests は MOD のソースをテスト用 API とともにコンパイルする構成で、配布 DLL を直接読み込まない。そのため難読化 DLL の入口と Harmony 属性の列挙は、上記の Mono.Cecil による検査で確認した。

## 未実行と実機の確認項目

ゲームの実行環境がないため、難読化 DLL での MOD 起動、既存の profile.json と途中再開データの読み込み、協力プレイの接続・同期・取引は未実行。旧版の相手と MOD なしの相手についても実機で確認する。実機確認だけの Issue は作らず、マージの条件にしない。

Startup.Tests の `SecondLoadOfRenamedModAssemblyStillVerifiesItsOwnPatch` は既存の Skip 指定により未実行。リポジトリ全体の長いテスト、実際の配布パッケージの作成、Steam への公開は今回の範囲に含めず未実行。DeployToGame の配置結果も前述のディレクトリ条件により未実行。
