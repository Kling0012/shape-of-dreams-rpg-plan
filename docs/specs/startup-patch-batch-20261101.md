# 起動時のパッチ適用の軽量化

2026-11-01。対象は v3.8.85 を元にした `perf/startup-patch-batch`。実機は未確認。

## 原因の見立て

利用者の Player.log では起動に 41,309〜45,268 ms、そのうちパッチ適用に 37,422〜38,017 ms かかっている。preflight は 2,004〜2,148 ms、resources は 1,883〜5,103 ms。約390クラスに時間が分散している。

上位には BossNativeMovementSource の 2,616 ms、NativeTimescaleIdlePublishPerf の 1,444 ms、InfinityNoSpecialInvitation の 1,170 ms、InfinityRoomIdentity の 1,153 ms がある。NativeTimescaleIdlePublishPerf は最終的にはスキップされている。上位5件の合計は約7.5秒で、残りの約30秒はほかのクラスに分散している。

ゲーム同梱の Harmony を調べると、`Harmony.GetPatchedMethods()` は全所有者の対象を列挙し、それぞれの `GetPatchInfo()` で所有者を調べている。従来の `HasInstalledClass` はこの列挙の中でもパッチ情報を読み直していた。成功したクラスを見つけるまで、他 MOD を含むパッチ情報の直列化データを繰り返し復元していた。対象が1メソッドのクラスにも全体の走査費用がかかる。

Harmony の `PatchClassProcessor` はクラス内では対象ごとにパッチをまとめるが、別クラスでは対象が同じでも置き換えを再生成する。実 DLL の読み取りではパッチクラスは390件。このうち Prepare・Cleanup・動的な対象・Transpiler などを持たず、単一の対象に解決する候補は213件だった。初期のソース検索による388件・212件という概数を、この実 DLL の件数で訂正する。

## 変更1 対象を絞った導入判定

通常のクラス属性だけで対象が決まる場合、`HasInstalledClass`、`EarlierCopyHookIsLive`、`RollBackClass` の探索をその対象だけに絞った。動的な対象、メソッドごとの対象指定、認識できない宣言は全対象を調べる従来の方法へ戻す。Prepare や TargetMethod(s) は呼び直さない。

対象の解決には、ゲーム同梱 Harmony の通常メソッドの解決と同じ `AccessTools.DeclaredMethod` を使う。所有者とクラスの完全名による所属判定は変えていない。再読み込みで別アセンブリの同名クラスへ解決される場合も従来の判定を使う。パッチ本体・導入順・初期化順は変わらない。導入結果自体はキャッシュせず、ロールバック後の未導入もその都度確認する。

実 DLL の Infinity 対象30クラス中29クラスが対象1件に解決した。利用者のログで遅かった InfinityNoSpecialInvitation、InfinityRoomIdentity、InfinityMapMoveSelection もこの範囲に入る。動的な対象を持つ InfinityRevealWorld は従来の全対象走査を使う。

効果は推定。29クラスの通常の導入確認では、成功対象を見つけるまでの全体走査が対象1件の確認になる。全体に P 個の対象がある場合、従来は各確認で最大 P 件の所有者確認と最大 P 件の所属確認が必要だったが、変更後はパッチ情報の復元が1件になる。ほかの MOD の対象数が多い環境ほど省ける処理は多い。利用者の環境での秒数と、37〜38秒のパッチ適用時間のうち何割を占めるかは未計測。

変更前の Release ビルドと NativeStartupTests 16件は成功。変更後の起動・再読み込みテストでは、クラス単位のロールバック、他所有者のパッチの維持、導入失敗後の継続などを含む既存テスト20件が成功した。既存のスキップが1件ある。

## 変更2 移動元を探す IL 読み取りの削減

BossNativeMovementSource は Actor とその継続処理のメソッドを調べ、移動呼び出しを含むメソッドを対象にする。従来は14,681メソッドを Harmony の IL reader に通していた。

生の IL を先に調べ、移動呼び出しを含まないと確認できたメソッドでは Harmony の読み取りを省いた。呼び出しトークンの解決結果は Module ごとの辞書に保持する。同じトークンを繰り返し解決しない。この辞書は対象列挙中だけに使う。既に取得した MethodBody を渡し、事前選別のための再取得も避けた。

移動呼び出しの候補は従来の reader でもう一度確認する。未知の opcode、不正な operand、解決できないトークン、必要な generic context が得られない呼び出しも従来の reader に戻す。例外テーブルが古い本体メソッドを reader が読めない場合は、そのメソッドだけを除外する既存の処理を維持した。Prefix と Finalizer、ホスト・参加者の処理、移動の発動条件は変更していない。

実 DLL を使った比較では、Harmony の読み取りが14,681件から157件へ減った。約98.9%の削減で、固定の3件を含む対象160件は順序も一致した。追加・欠落はいずれも0件。

読み取り分析は Linux の .NET 10 と Harmony 2.3.6、`DOTNET_TieredCompilation=0` の条件で実施した。元の走査を新規プロセスで測ると1,494〜1,947 ms、候補選別を含む最終コードは789〜1,145 msだった。JIT の既定条件では効果が安定しない測定もあったため、この値を Unity Mono や利用者 PC の短縮秒数には換算しない。利用者のログの2,616 msには対象のパッチ適用も含まれ、その部分は変わらない。

分析時は ClassLibrary1.dll の32bit必須フラグにより CoreCLR の読み取りが失敗した。作業ツリー内の ignored な `tools/StartupBench/obj/patch-analysis/` に調査用コピーを作り、そのコピーだけ32bit必須フラグを外して依存先として読み込んだ。ゲーム側の DLL は変更していない。分析プログラムはメソッドの列挙と本体の読み取りだけに使い、ゲームの起動や detour の導入には使っていない。

## 見送ったもの

- クラスをまとめて適用する案: 準備処理をまたがない候補を試作したところ、9クラス・4対象をまとめ、置き換えの再生成を5回省けた。ただし異なる対象のパッチを追い越すと、他 MOD の Transpiler が途中のパッチ状態を参照した場合に観測順が変わる。同じ対象の連続するクラスに限定すると2クラス・1対象で、削減は1回だった。追加の探索費用と private API への依存に見合わないため採用しなかった。試作は ignored な分析ディレクトリへ保管した。製品コードは従来のクラス単位の適用・警告・ロールバックを使う。
- Prepare や Transpiler をまたぐ一括適用: コールバックの実行時点や、他 MOD の Transpiler が実行される回数に影響するため見送った。同じメソッドに対する Prefix の順、priority、他所有者のパッチは変更していない。
- NativeTimescaleIdlePublishPerf の準備処理の変更: スキップに1,444 msかかる理由を、この環境では再現できなかった。診断から静的フィールドを読む際の再入ガードも試したが、原因との関係を確認できず採用しなかった。このクラスの遅延は未解決。
- preflight の省略・非同期化、機能初期化の後回し: 準備処理と初期化の観測順を変えずに短縮できる根拠が得られていないため見送った。言語・設定・星図登録などの順序を維持した。

## 最終の検証結果

新しいテストファイル・テストメソッドは作っていない。次の既存の確認を実行した。

- `python3 tools/balance/gen_cs.py --check`: 成功。
- `dotnet build src/SodRpg.Mod -c Release -p:GameDir="$HOME/dev/sod-gamedata/Shape of Dreams" -p:ModDeployDir=/tmp/startup-batch-x`: 成功。エラー0件、警告6件。配置先の指定は一時ディレクトリで、利用者 PC のゲームには配置していない。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Mod.Startup.Tests --no-restore --filter 'FullyQualifiedName~NativeStartupTests|FullyQualifiedName~ModReloadTests|FullyQualifiedName~ReloadStartupTests' --logger 'console;verbosity=minimal'`: 成功20件、失敗0件、既存スキップ1件。スキップは Release の JIT インライン化に関する既存の SecondLoadOfRenamedModAssemblyStillVerifiesItsOwnPatch。
- `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/SodRpg.Core.Tests --no-restore --filter 'FullyQualifiedName~BossNativeScopeGuardTests|FullyQualifiedName~BossRuntimeNativeTests.Movement_source_targets' --logger 'console;verbosity=minimal'`: 成功6件、失敗0件。読めない本体メソッドがあっても対象列挙を続けることと、ホスト・参加者のスコープ処理を確認した。
- 実 DLL の新旧の対象列挙: 160件、順序一致、欠落0件、追加0件。

Serena MCP は接続に失敗したため、検索と必要箇所の読み取りで調査した。ゲーム同梱 Harmony の実装はローカル DLL を逆コンパイルして確認した。逆コンパイルしたコードはリポジトリに保存していない。

未実行は次のとおり。

- ゲームの起動と13本の他 MOD を併用した時間計測。この環境ではゲームを動かせないため、実機は未確認。利用者の PC とゲームには触れていない。
- Issue73.Native.Tests、SodRpg.Mod.Startup.Tests、SodRpg.Mod.AutoCast.Tests の全件実行。依頼末尾の「全件・低速なテストを実行せず、変更部分だけを確認する」という制約に従い、起動・再読み込み・移動元走査の関連テストに絞った。全件実行はパイプラインに残る。

起動全体の短縮時間は未確定。今回省いた処理は29クラスの全体走査と、移動元探索の14,524件の Harmony 読み取りである。置き換えの再生成回数、パッチ本体、初期化順は変えていない。
