using System.Globalization;
using System.Text;
using SodRpg.Core.Game;

namespace BalanceSim;

internal sealed record EquipmentEntry(string Id, string Label, decimal? Value, string Type, string Unit,
    string Status = "measured", string Semantics = "");

internal static class EquipmentReport
{
    public const string ValuePolicy = "actual Core Content definitions before item-level scaling, rolls, enhancement, awakening or Build caps; stored display units, not converted runtime units";
    public const string Limitations = "definition enumeration, not combat output or drop probability; weight is a relative weight, not probability; boss payloads and ordinal reward-stage selectors excluded (stage 8); currency caps are separate from combat caps; named-item values are derived from power-pool bands";

    public static IReadOnlyList<EquipmentEntry> Measure()
    {
        var entries = new List<EquipmentEntry>();
        void Add(string id, string label, int value, string unit, string semantics = "") =>
            entries.Add(new(id, label, value, "int32", unit, Semantics: semantics));
        void AddLink(string id, string label, LinkDef link)
        {
            string semantics = link.Kind + "; requires=" + string.Join("+", link.Requires);
            if (link.Kind == LinkKind.BossReward)
            {
                entries.Add(new(id, label, null, "reference", "profile-stage", "stage-selector-not-tuning", semantics));
                return;
            }
            if (link.Kind == LinkKind.Guard)
            {
                entries.Add(new(id + "/maxHealth", label + " 最大HP", link.Value, "decimal", "percent", Semantics: semantics));
                entries.Add(new(id + "/armor", label + " 防御", link.Value, "decimal", "armor", Semantics: semantics));
                return;
            }
            entries.Add(new(id, label, link.Value, "decimal", "percent", Semantics: semantics));
        }
        foreach (var basis in Content.Bases)
            Add($"equipment/bases/{basis.Id}/implicit", basis.Name.Ja, basis.ImplicitValue,
                StatUnit(basis.ImplicitStat), $"{basis.Slot}/{basis.ImplicitStat}; family={basis.Family}");
        foreach (var unique in Content.Uniques)
        {
            string id = $"equipment/uniques/{unique.Id}";
            string semantics = $"base={unique.BaseId}; set={unique.SetId}; bossMove={unique.BossMove}";
            for (int index = 0; index < unique.Powers.Count; index++)
            {
                var power = unique.Powers[index];
                Add($"{id}/powers/{index}/{power.Power}", unique.Name.Ja, power.Value, PowerUnit(power.Power), semantics);
            }
            if (unique.Link != null) AddLink(id + "/link", unique.Name.Ja, unique.Link);
            if (unique.Powers.Count == 0 && unique.Link == null)
                entries.Add(new(id + "/relationship", unique.Name.Ja, null, "reference", "relationship",
                    unique.BossMove != null ? "boss-payload-excluded" : "set-reference-only", semantics));
        }
        foreach (var named in NamedItems.All)
            for (int index = 0; index < named.Powers.Count; index++)
            {
                var power = named.Powers[index];
                Add($"equipment/named/{named.Id}/powers/{index}/{power.Power}", named.Name.Ja,
                    power.Value, PowerUnit(power.Power), $"derived-band; base={named.BaseId}");
            }
        foreach (var set in Content.Sets)
        {
            string id = $"equipment/sets/{set.Id}";
            if (set.BossTypeName != null)
            {
                entries.Add(new(id + "/relationship", set.Name.Ja, null, "reference", "relationship",
                    "boss-payload-excluded", $"boss={set.BossTypeName}; reward={set.BossReward}"));
                continue;
            }
            foreach (var stat in set.TwoPiece)
                Add($"{id}/twoPiece/{stat.Stat}", set.Name.Ja, stat.Value, StatUnit(stat.Stat), stat.Stat.ToString());
            foreach (var power in set.ThreePiece)
                Add($"{id}/threePiece/{power.Power}", set.Name.Ja, power.Value, PowerUnit(power.Power), power.Power.ToString());
            foreach (var power in set.SixPiece)
                Add($"{id}/sixPiece/{power.Power}", set.Name.Ja, power.Value, PowerUnit(power.Power), power.Power.ToString());
            foreach (var stage in set.LinkStages)
                AddLink($"{id}/links/{stage.RequiredPieces}", set.Name.Ja, stage.Link);
        }
        foreach (var slot in Enum.GetValues<Slot>())
        {
            foreach (var affix in Content.AffixPool(slot))
            {
                string id = $"equipment/affixes/{slot}/{affix.Stat}";
                string semantics = $"{affix.Stat}; minimumRarity={affix.MinRarity}";
                Add(id + "/min", id, affix.Min, StatUnit(affix.Stat), semantics);
                Add(id + "/max", id, affix.Max, StatUnit(affix.Stat), semantics);
                Add(id + "/weight", id, affix.Weight, "relative-weight", semantics);
            }
            foreach (var power in Content.PowerPool(slot))
            {
                string id = $"equipment/power-pools/{slot}/{power.Power}";
                Add(id + "/min", id, power.Min, PowerUnit(power.Power), power.Power.ToString());
                Add(id + "/max", id, power.Max, PowerUnit(power.Power), power.Power.ToString());
            }
        }
        foreach (var stat in Enum.GetValues<Stat>())
            Add($"equipment/caps/stats/{stat}", stat.ToString(), Content.StatCap(stat), StatUnit(stat), stat.ToString());
        foreach (var power in Enum.GetValues<Power>())
            if (power != Power.None)
                Add($"equipment/caps/{(CurrencyStars.IsPower(power) ? "currency" : "powers")}/{power}",
                    power.ToString(), Content.PowerCap(power), PowerUnit(power), power.ToString());
        return entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
    }

    private static string StatUnit(Stat stat) => stat switch
    {
        Stat.AttackFlat => "attack", Stat.PowerFlat => "ability-power", Stat.MaxHealthFlat => "health",
        Stat.Armor => "armor", Stat.HealthRegen => "health/second", Stat.Haste => "haste",
        Stat.Tenacity => "tenacity", Stat.FourthAttackShift => "attacks",
        Stat.EssenceSlotIdentity or Stat.EssenceSlotMovement => "essence-slots",
        Stat.CritChancePct or Stat.CritDamagePct => "percentage-points",
        _ => "percent",
    };

    private static string PowerUnit(Power power) => power switch
    {
        Power.Bulwark => "armor",
        Power.Ember or Power.Radiance or Power.Umbra => "hundredths-element-stack",
        Power.Lifesteal or Power.SoulSiphon or Power.ShardBoon => "tenths-percent-max-health",
        Power.CriticalEcho or Power.BareHandedPride => "tenths-second",
        Power.KillGoldPct or Power.EliteKillGoldPct => "percent-gold",
        Power.DreamDustPct or Power.DreamDustDelvePct => "percent-dream-dust",
        Power.Cinder => "element-stacks",
        Power.Frost or Power.Wildfire or Power.UmbralHeritage or Power.PilingLuck => "percentage-points",
        _ => "percent",
    };

    public static string Render(IReadOnlyList<EquipmentEntry> entries)
    {
        var text = new StringBuilder();
        text.AppendLine("# 装備の型付き数値一覧");
        text.AppendLine();
        text.AppendLine("- " + ValuePolicy);
        text.AppendLine("- " + Limitations);
        text.AppendLine("- 異なる単位・通貨の合算は行わない。参照のみの部品は null（ゼロではない）。");
        text.AppendLine();
        text.AppendLine("| ID | 型 | 値 | 単位 | 状態 | 意味 |");
        text.AppendLine("| --- | --- | ---: | --- | --- | --- |");
        foreach (var entry in entries)
            text.AppendLine($"| `{entry.Id}` | {entry.Type} | {entry.Value?.ToString(CultureInfo.InvariantCulture) ?? "—"} | {entry.Unit} | {entry.Status} | {entry.Semantics} |");
        return text.ToString();
    }
}
