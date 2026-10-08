# 料理素材の native batch / MOD リロード回帰試験

実行: `dotnet test -c Release tests/SodRpg.Culinary.Tests`。
`SodRpg.sln` と CI の `mod-tests` に含まれる。

- `ModUnderTest` は本番の `src/SodRpg.Mod/CulinaryIngredientCap.cs` を直接コンパイルする。
- その DLL のバイト列を `Assembly.Load(byte[])` で繰り返し読み込み、別の型・static 状態を持つ MOD コピーを作る。同じ MVID でもコピーが異なることを検証する。
- native 型は別の `NativeApi` アセンブリに置き、すべてのコピーが同じ素材 actor を操作する。これにより、同じコピーの Stop/Install だけでは見えない provenance の消失を検出する。
- 本番の Install/Stop と mutation/count/consume コールバックを呼び、料理一回分の元上限、在庫非切詰め、同時コピー、pool 再利用、独立した復元 actor、加算 overflow、未知の危険な上限、弱参照寿命を確認する。
- 40 / 17 / 63 などは試験用入力であり、実ゲームの prefab 上限を断定する値ではない。

## 検証範囲

`NativeApi` の native メソッドと Harmony は最小スタブであり、実機挙動の代用ではない。この試験が保証するのは本番コールバックと複数アセンブリ間の状態共有である。実際の Harmony detour、Unity pooling、保存/Continue、UI、ホスト/参加者同期は実ゲームで別途確認する。

旧実装が既に `maxStack = int.MaxValue` にした actor を、初めて修正版へ更新するだけで復元することはできない。旧コピーの assembly-local な記録は共有ストアにないため、元の値を推測せず従来どおり fail-soft する。この場合はゲームを再起動し、native 値を持つ actor から修正版を開始する。修正版による記録後の Stop/Install とアセンブリ再読込は保存形式や通信形式を追加せず対応する。
