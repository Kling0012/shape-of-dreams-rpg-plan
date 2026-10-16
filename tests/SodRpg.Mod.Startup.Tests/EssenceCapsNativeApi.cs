using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;

// 本体エッセンス面の境界スタブ（docs/reviews の逆コンパイル結果に基づく）。実 NativeEssenceCaps /
// NativeMemoryReuse のパッチ対象と、抽出した HostAuthority 本体（OnSkillUse・OnNativeGemUseV129）が
// 触る本体 API の数式と前提だけを再現する。ゲーム専用の振る舞い（同期・UI・演出）は持たない。
namespace SodRpg.Mod
{
    public enum LevelScaling { None, GemDefault }
    public enum SkillType { Normal, Ultimate }
    public sealed class CastInfo { }

    // 本体の品質スケーリング（GemDefault はレベル当たりの加算で線形）。
    public class ScalingValue
    {
        public LevelScaling leveling = LevelScaling.GemDefault;
        public float addedScalingPerLevel;
        public float GetAddedScalingMultiplierPerLevel() => addedScalingPerLevel;
        public float GetScalingMultiplier(int level) => 1f + GetAddedScalingMultiplierPerLevel() * Math.Max(0, level - 1);
    }

    // 本体 Gem.GetValue は効果値の読み出し口。ソフト上限の免除判定はこの経路の Prefix/Postfix で行う。
    public class Gem : Actor
    {
        public Hero owner;
        public int level = 1;
        public virtual float GetValue(ScalingValue value) => value.GetScalingMultiplier(level);
    }
    public sealed class Gem_R_Mortality : Gem { }
    public sealed class Gem_R_Ricochet : Gem { public float ricochetChance { get; set; } }
    public sealed class Gem_R_Epiphany : Gem { public float refundCooldownRatio { get; set; } }
    public sealed class Se_Gem_E_Fever_LivingBomb : StatusEffect
    {
        public float damageAmp;
        public float GetDamageAmp() => damageAmp;
    }
    public sealed class Gem_L_Embertail : Gem
    {
        internal SkillBonus _bonus = new SkillBonus();
        public void ActiveLogicUpdate(float dt) { }
    }
    public class SkillBonus { public float cooldownMultiplier = 1f; }

    public struct EventInfoSkillUse
    {
        public SkillTrigger skill;
        public HeroSkillLocation type;
    }

    // 抽出した本体メソッド（OnSkillUse / OnNativeGemUseV129）が依存する HostAuthority 面。
    // 連携再計算・仕掛けキュー・報告送信は本体側の下請けなので、ここでは呼び出しを記録するのみ。
    internal sealed partial class HostAuthority
    {
        internal sealed class ReceivedBuild
        {
            public Build Build;
        }

        internal sealed class HeroRuntime
        {
            public Hero Hero;
            public PowerRuntime Powers;
            public ReceivedBuild AppliedBuild;
            public readonly List<LinkDef> SatisfiedLinks = new List<LinkDef>();
        }

        internal readonly Dictionary<Hero, HeroRuntime> _runtimes = new Dictionary<Hero, HeroRuntime>();
        internal int _powerSupportDepth, _gimmickDamageDepth, _reactionEffectDepth, _pairDamageDepth;
        private readonly HashSet<Power> _triggeredPowers = new HashSet<Power>();
        internal readonly List<string> QueueGimmicksLog = new List<string>();

        internal HostAuthority(HeroRuntime seed)
        {
            if (seed != null && seed.Hero != null) _runtimes[seed.Hero] = seed;
        }

        private static bool Alive(Hero h) => h != null && h.isActive && !h.isKnockedOut;

        // 抽出元の OnSkillUse は private なので、試験からはこの経路で本体メソッドを呼ぶ。
        internal void InvokeOnSkillUseForTests(HeroRuntime rt, EventInfoSkillUse info) => OnSkillUse(rt, info);

        private void LogPowerTrigger(Power power)
        {
            if (_triggeredPowers.Add(power)) Log.Info("power " + power + " triggered");
        }

        private void SendBountyReport(HeroRuntime rt, BountyReportKind kind, int value) { }
        private void ApplyWaypointMemoryCooldown(Hero hero, EventInfoSkillUse info) { }
        private void UpdateLinks(HeroRuntime rt, bool alive) { }
        private void LogLinkApplied(LinkDef link) { }
        private void QueueGimmicks(HeroRuntime rt, GimmickTrigger trigger, string memory, Entity victim, float damage,
            bool pairGenerated = false, Actor actor = null, bool direct = true)
            => QueueGimmicksLog.Add(trigger + " " + memory);
        private static void DamageAround(Hero hero, UnityEngine.Vector3 center, float radius, float amount, Entity except,
            int maxTargets, bool magic, bool gimmick = false) { }
    }
}
