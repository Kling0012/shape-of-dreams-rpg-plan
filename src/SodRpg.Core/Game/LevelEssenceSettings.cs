using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>英雄の本来のレベルによる Q・W・E・R の追加枠。ホストの設定を使う。</summary>
    public sealed class LevelEssenceSettings
    {
        public const int DefaultEveryLevels = 10;
        public const int MinEveryLevels = 1, MaxEveryLevels = 99;
        public const int MinCeiling = EssenceSlots.NativeCapPerLocation;
        public const int MaxCeiling = 8;
        public const int TargetQ = 1, TargetW = 2, TargetE = 4, TargetR = 8, TargetAll = TargetQ | TargetW | TargetE | TargetR;

        public bool Enabled { get; }
        public int Targets { get; }
        public int Ceiling { get; }
        public int EveryLevels { get; }

        // MOD側でHUDの準備ができるまで4に制限する。台帳の保存形式には入れない。
        public static int RuntimeCapLimit { get; set; } = MaxCeiling;
        public static bool RuntimeBonusAvailable { get; set; } = true;
        public static LevelEssenceSettings Default { get; } = Create(true, true, true, true, true, DefaultEveryLevels, MinCeiling);
        public static LevelEssenceSettings Current { get; set; } = Default;
        public static LevelEssenceSettings Off { get; } = Create(false, true, true, true, true, DefaultEveryLevels, MinCeiling);

        private LevelEssenceSettings(bool enabled, int targets, int everyLevels, int ceiling)
        {
            Enabled = enabled;
            Targets = targets & TargetAll;
            EveryLevels = ClampEveryLevels(everyLevels);
            Ceiling = ClampCeiling(ceiling);
        }

        public static LevelEssenceSettings Create(bool enabled, bool q, bool w, bool e, bool r, int everyLevels, int ceiling)
            => new LevelEssenceSettings(enabled, (q ? TargetQ : 0) | (w ? TargetW : 0) | (e ? TargetE : 0) | (r ? TargetR : 0), everyLevels, ceiling);

        public static int ClampCeiling(int ceiling) => Math.Max(MinCeiling, Math.Min(MaxCeiling, ceiling));
        public static int ClampEveryLevels(int everyLevels) => Math.Max(MinEveryLevels, Math.Min(MaxEveryLevels, everyLevels));
        public bool Includes(int target) => (Targets & target) != 0;
        public int TotalAtLevel(int level) => Math.Max(0, level) / EveryLevels;
        public int Desired(int target, int level) => Enabled && Includes(target) ? TotalAtLevel(level) : 0;

        public string Describe() => Loc.T(
            $"レベル連動のエッセンス枠：{(Enabled ? "オン" : "オフ")}（{TargetsText()}）\n{EveryLevels}レベルごとに +1、混沌と合わせて1つの記憶は最大{Ceiling}枠",
            $"Level-based essence slots: {(Enabled ? "on" : "off")} ({TargetsText()})\n+1 every {EveryLevels} levels; at most {Ceiling} slots per memory including Chaos", $"随等级增加的精华槽：{(Enabled ? "on" : "off")}（{TargetsText()}）\n每{EveryLevels}级 +1；含混沌在内，每个记忆最多{Ceiling}槽");

        public string TargetsText()
        {
            var parts = new List<string>();
            if (Includes(TargetQ)) parts.Add("Q");
            if (Includes(TargetW)) parts.Add("W");
            if (Includes(TargetE)) parts.Add("E");
            if (Includes(TargetR)) parts.Add("R");
            return parts.Count == 0 ? Loc.T("対象なし", "no targets", "无目标") : string.Join("・", parts);
        }
    }
}
