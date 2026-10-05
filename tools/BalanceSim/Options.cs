using System.Globalization;
using SodRpg.Core.Game;

namespace BalanceSim;

public sealed class Options
{
    public int Runs { get; private set; } = 20;
    public int Players { get; private set; } = 300;
    public ulong Seed { get; private set; } = 1;
    public int Zones { get; private set; } = 4;
    public int Rooms { get; private set; } = 5;
    public int Lesser { get; private set; } = 10;
    public int Normal { get; private set; } = 8;
    public double MiniBoss { get; private set; } = 0.25;
    public int Bosses { get; private set; } = 1;
    public string Policy { get; internal set; } = "secure";
    public double Wipe { get; private set; } = 0.15;
    public double Bounty { get; private set; } = 0.6;
    public int ItemLevel { get; private set; } = 1;
    public int ItemLevelPerZone { get; private set; } = 1;
    public string? Out { get; private set; }
    public bool Help { get; private set; }
    public string Mode { get; internal set; } = "expeditions";
    public int DreamLevel { get; private set; } = Content.MaxDreamLevel;
    public bool Stars => Mode == "stars";
    public bool Sets => Mode == "sets";
    public bool V132Stars => Mode == "v132stars";
    public bool Infinity => Mode == "infinity";

    public int SecureHeat => Policy switch { "delve1" => 1, "greedy" => 3, _ => 0 };

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key is "--help" or "-h") { o.Help = true; continue; }
            if (i + 1 >= args.Length) throw new ArgumentException($"{key} の値が必要です。");
            string value = args[++i];
            switch (key)
            {
                case "--runs": o.Runs = Integer(key, value, 1); break;
                case "--players": o.Players = Integer(key, value, 1); break;
                case "--seed":
                    if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
                        throw new ArgumentException("--seed は非負の64ビット整数です。");
                    o.Seed = seed;
                    break;
                case "--zones": o.Zones = Integer(key, value, 1); break;
                case "--rooms": o.Rooms = Integer(key, value, 1); break;
                case "--lesser": o.Lesser = Integer(key, value, 0); break;
                case "--normal": o.Normal = Integer(key, value, 0); break;
                case "--miniboss": o.MiniBoss = Probability(key, value); break;
                case "--bosses": o.Bosses = Integer(key, value, 0); break;
                case "--policy":
                    if (value is not ("secure" or "delve1" or "greedy"))
                        throw new ArgumentException("--policy は secure / delve1 / greedy です。");
                    o.Policy = value;
                    break;
                case "--wipe": o.Wipe = Probability(key, value); break;
                case "--bounty": o.Bounty = Probability(key, value); break;
                case "--item-level": o.ItemLevel = Integer(key, value, 1); break;
                case "--item-level-per-zone": o.ItemLevelPerZone = Integer(key, value, 0); break;
                case "--out":
                    if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("--out のパスが空です。");
                    o.Out = value;
                    break;
                case "--mode":
                    if (value is not ("expeditions" or "stars" or "sets" or "v132stars" or "infinity"))
                        throw new ArgumentException("--mode は expeditions / stars / sets / v132stars / infinity です。");
                    o.Mode = value;
                    break;
                case "--dream-level":
                    o.DreamLevel = Integer(key, value, 1);
                    if (o.DreamLevel > Content.MaxDreamLevel)
                        throw new ArgumentException($"--dream-level は 1〜{Content.MaxDreamLevel} です。");
                    break;
                default: throw new ArgumentException($"未知の引数: {key}");
            }
        }
        if ((long)o.Zones * o.Rooms > int.MaxValue)
            throw new ArgumentException("ゾーン数×部屋数は32ビット整数の範囲にしてください。");
        return o;
    }

    /// <summary>同じ値の複製（v1.32 計測で方針だけ差し替えて使い回す）。</summary>
    public Options Clone() => new()
    {
        Runs = Runs, Players = Players, Seed = Seed, Zones = Zones, Rooms = Rooms, Lesser = Lesser, Normal = Normal,
        MiniBoss = MiniBoss, Bosses = Bosses, Policy = Policy, Wipe = Wipe, Bounty = Bounty, ItemLevel = ItemLevel,
        ItemLevelPerZone = ItemLevelPerZone, Out = Out, Mode = Mode, DreamLevel = DreamLevel,
    };

    private static int Integer(string key, string value, int min)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) || result < min)
            throw new ArgumentException($"{key} は {min} 以上の整数です。");
        return result;
    }

    private static double Probability(string key, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            || !double.IsFinite(result) || result < 0 || result > 1)
            throw new ArgumentException($"{key} は 0〜1 の確率です（小数点は .）。");
        return result;
    }

    public const string Usage = """
        Dreamforge RPG バランスシミュレーター
        dotnet run --project tools/BalanceSim -c Release -- [引数]
          --runs 20          1人あたりの遠征数
          --players 300      独立したプレイヤー数
          --seed 1           決定的な乱数の種（非負の64ビット整数）
          --zones 4          遠征あたりのゾーン数
          --rooms 5          ゾーンあたりの戦闘部屋数
          --lesser 10        部屋あたりの Lesser 撃破数
          --normal 8         部屋あたりの Normal 撃破数
          --miniboss 0.25    部屋ごとに MiniBoss 1体が出る確率
          --bosses 1         ゾーン末尾の Boss 撃破数
          --policy secure    secure / delve1 / greedy
          --wipe 0.15        遠征ごとの全滅率
          --bounty 0.6       本体行動に依存する依頼の達成確率
          --item-level 1     最初のゾーンのアイテムレベル
          --item-level-per-zone 1  次のゾーンで増えるアイテムレベル
          --mode expeditions  expeditions / stars / sets / v132stars / infinity（30/60/120分、周期10/15/20）
          --out <path>       標準出力に加えてUTF-8のMarkdownファイルに保存
          --help             この説明を表示
        """;
}
