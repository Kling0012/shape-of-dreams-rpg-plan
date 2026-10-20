using System;

namespace SodRpg.Core.Game
{
    // Real engine examples for the data team to replace with approved cluster tables.
    // Preserve registered IDs and one-based star order after they have been used in saves.
    public static partial class StarClusters
    {
        private static System.Collections.Generic.IReadOnlyList<StarClusterDef> CreateExamples() => Array.AsReadOnly(new[]
        {
            new StarClusterDef
            {
                Id = "h.cetus.cluster.icy-veins", HeroKey = "Hero_Cetus",
                Region = ClusterRegion.Memory("h.cetus.route.icy-veins"), Anchor = "h.cetus.route.icy-veins.7", Shape = ClusterShape.Fan,
                Stars = Array.AsReadOnly(new[]
                {
                    new ClusterStarDef { Kind = ClusterStarKind.MemoryDamage, Name = new Txt("霜の深まり", "Deepening Frost", "霜寒渐深"), Memory = "St_D_IcyVeins", Amount = 3 },
                    new ClusterStarDef { Kind = ClusterStarKind.GimmickBoost, Name = new Txt("冷血の響き", "Cold-Blood Resonance", "冷血共鸣"), Memory = "St_D_IcyVeins", Amount = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_icy_veins_2_amount },
                    new ClusterStarDef { Kind = ClusterStarKind.GimmickParam, Name = new Txt("霜の兆し", "Signs of Frost", "霜之预兆"), Memory = "St_D_IcyVeins", Param = GimmickParam.Chance, Amount = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_icy_veins_3_amount },
                    new ClusterStarDef { Kind = ClusterStarKind.GimmickParam, Name = new Txt("長く残る薄氷", "Lingering Rime", "残留霜华"), Memory = "St_D_IcyVeins", Param = GimmickParam.Duration, Amount = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_icy_veins_4_amount },
                    new ClusterStarDef
                    {
                        Kind = ClusterStarKind.Choice, Name = new Txt("冷血の分岐", "Cold-Blood Fork", "冷血分岔"),
                        Options = Array.AsReadOnly(new[]
                        {
                            new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("薄氷の障壁", "Rime Barrier", "霜华屏障"), Memory = "St_D_IcyVeins",
                                Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Shield, Value = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_icy_veins_5_options_1_gimmick_value } },
                            new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("深海の護り", "Abyssal Ward", "深渊守护"), Power = Power.Barrier, Amount = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_icy_veins_5_options_2_amount }
                        })
                    },
                    new ClusterStarDef { Kind = ClusterStarKind.GimmickBoost, Name = new Txt("重なる冷気", "Layered Cold", "叠加寒气"), Memory = "St_D_IcyVeins", Amount = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_icy_veins_6_amount }
                })
            },
            new StarClusterDef
            {
                Id = "h.cetus.cluster.frozen-recall", HeroKey = "Hero_Cetus",
                Region = ClusterRegion.Bridge("h.cetus.ring.force"), Anchor = "h.cetus.ring.force", Shape = ClusterShape.Chain,
                Stars = Array.AsReadOnly(new[]
                {
                    new ClusterStarDef { Kind = ClusterStarKind.MemoryHaste, Name = new Txt("寒気の巡り", "Chill Cycle", "寒气巡回"), Memory = "St_Q_EmbracingTheChill", Amount = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_frozen_recall_1_amount },
                    new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("冷血の小波", "Cold-Blood Ripple", "冷血涟漪"), Memory = "St_D_IcyVeins",
                        Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Burst, Value = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_frozen_recall_2_gimmick_value, Cooldown = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_frozen_recall_2_gimmick_cooldown } },
                    new ClusterStarDef { Kind = ClusterStarKind.GimmickParam, Name = new Txt("広がる小波", "Widening Ripple", "扩散涟漪"), Memory = "St_D_IcyVeins", Param = GimmickParam.Radius, Amount = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_frozen_recall_3_amount }
                })
            },
            new StarClusterDef
            {
                Id = "h.cetus.cluster.abyssal-shell", HeroKey = "Hero_Cetus",
                Region = ClusterRegion.Outer, Anchor = "h.cetus.outer.abyssal-shell", Shape = ClusterShape.Ring,
                Stars = Array.AsReadOnly(new[]
                {
                    new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("潮の守り", "Tidal Guard", "潮汐守护"), Stat = Stat.Armor, Amount = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_abyssal_shell_1_amount },
                    new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("寒気の飛沫", "Chill Spray", "寒气飞沫"), Memory = "St_Q_EmbracingTheChill",
                        Gimmick = new GimmickDef { Trigger = GimmickTrigger.OnHit, Effect = GimmickEffect.Ricochet, Value = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_abyssal_shell_2_gimmick_value, Arg = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_abyssal_shell_2_gimmick_arg, Cooldown = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_abyssal_shell_2_gimmick_cooldown } },
                    new ClusterStarDef { Kind = ClusterStarKind.GimmickParam, Name = new Txt("重なる飛沫", "Layered Spray", "层叠飞沫"), Memory = "St_Q_EmbracingTheChill", Param = GimmickParam.ExtraTargets, Amount = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_abyssal_shell_3_amount },
                    new ClusterStarDef
                    {
                        Kind = ClusterStarKind.Choice, Name = new Txt("深海の分岐", "Abyssal Fork", "深渊分岔"),
                        Options = Array.AsReadOnly(new[]
                        {
                            new ClusterStarDef { Kind = ClusterStarKind.Stat, Name = new Txt("深海の器", "Abyssal Vessel", "深渊之器"), Stat = Stat.MaxHealthPct, Amount = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_abyssal_shell_4_options_1_amount },
                            new ClusterStarDef { Kind = ClusterStarKind.Notable, Name = new Txt("氷壁の護り", "Ice-Wall Ward", "冰墙守护"), Power = Power.Aegis, Amount = MemoryDamageBalance.Effect_legacy_example_cetus_h_cetus_cluster_abyssal_shell_4_options_2_amount }
                        })
                    }
                })
            }
        });
    }
}
