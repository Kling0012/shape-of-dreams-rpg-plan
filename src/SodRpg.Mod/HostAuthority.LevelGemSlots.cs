using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SodRpg.Core.Game;

namespace SodRpg.Mod
{
    // ゲーム本来のプレイ中のレベルに応じて、Q・W・E・Rの記憶のエッセンス枠を増やす（ホストだけが書く）。
    // 星図の枠（Identity・Movement、HostAuthority.GemSlots.cs）とは別の台帳を持ち、Build・保存・通信には関わらない。
    // 枠の書き込みと戻し・はみ出したエッセンスの扱い・継続（Continue）中の保護は、星図の枠と同じ UpdateGemSlot を使う。
    internal sealed partial class HostAuthority
    {
        private static readonly HeroSkillLocation[] LevelGemLocations =
            { HeroSkillLocation.Q, HeroSkillLocation.W, HeroSkillLocation.E, HeroSkillLocation.R };
        private static readonly int[] LevelGemTargets =
            { LevelEssenceSettings.TargetQ, LevelEssenceSettings.TargetW, LevelEssenceSettings.TargetE, LevelEssenceSettings.TargetR };

        private sealed class LevelGemSlotLedger
        {
            internal readonly GemSlotLedger[] Locations = new GemSlotLedger[LevelGemLocations.Length];
            private readonly int[] _persisted;
            internal LevelGemSlotLedger(HeroSkill skill) : this(new int[LevelGemLocations.Length * 2], skill) { }
            private LevelGemSlotLedger(int[] persisted, HeroSkill skill)
            {
                _persisted = persisted;
                for (int i = 0; i < Locations.Length; i++)
                    Locations[i] = new GemSlotLedger(persisted[i * 2], persisted[i * 2] > 0 || persisted[i * 2 + 1] > 0
                        ? persisted[i * 2 + 1] : skill.GetMaxGemCount(LevelGemLocations[i]));
            }
            internal static LevelGemSlotLedger Capture(HeroSkill skill, int[] persisted) => new LevelGemSlotLedger(persisted, skill);
            internal void Persist()
            {
                for (int i = 0; i < Locations.Length; i++)
                {
                    _persisted[i * 2] = Locations[i].OurContribution;
                    _persisted[i * 2 + 1] = Locations[i].LastWritten;
                }
            }
        }

        // 所有の記録は、MODの再読み込みをまたいで AppDomain に残す（星図の枠と同じ理由）。
        private const string LevelGemSlotLedgerStoreKey = "dreamforge.gemslot.level.ownership.v1";
        private static readonly ConditionalWeakTable<HeroSkill, LevelGemSlotLedger> LevelGemSlotLedgers =
            new ConditionalWeakTable<HeroSkill, LevelGemSlotLedger>();

        private static ConditionalWeakTable<HeroSkill, int[]> PersistedLevelGemSlotLedgers =>
            AppDomain.CurrentDomain.GetData(LevelGemSlotLedgerStoreKey) as ConditionalWeakTable<HeroSkill, int[]>
            ?? CreatePersistedLevelGemSlotLedgers();

        private static ConditionalWeakTable<HeroSkill, int[]> CreatePersistedLevelGemSlotLedgers()
        {
            var created = new ConditionalWeakTable<HeroSkill, int[]>();
            AppDomain.CurrentDomain.SetData(LevelGemSlotLedgerStoreKey, created);
            return created;
        }

        private readonly List<WeakReference<HeroSkill>> _levelGemSkills = new List<WeakReference<HeroSkill>>();
        private readonly ConditionalWeakTable<HeroSkill, object> _trackedLevelGemSkills = new ConditionalWeakTable<HeroSkill, object>();
        private float _nextLevelGemSlotCheck;

        /// <summary>1秒ごとに、全員の英雄のレベルを見て Q・W・E・R の枠を合わせる。</summary>
        private void TickLevelGemSlots()
        {
            if (ClientSession.NativeContinueRestoring) return;
            float now = UnityEngine.Time.unscaledTime;
            if (now < _nextLevelGemSlotCheck) return;
            _nextLevelGemSlotCheck = now + 1f;
            var settings = LevelEssenceSettings.Current ?? LevelEssenceSettings.Off;
            if (GemContinueSources.HasParticipants) ProtectNativeContinueLevelGemSlots();
            foreach (var player in DewPlayer.gamePlayers)
                ApplyLevelGemSlotsFor(player != null ? player.hero : null, settings);
        }

        private void ApplyLevelGemSlotsFor(Hero hero, LevelEssenceSettings settings)
        {
            var skill = hero != null && hero.isActive ? hero.Skill : null;
            if (skill == null) return;
            ApplyLevelGemSlots(skill, HeroLevelOf(hero), settings);
        }

        // ゲーム本来のレベル（英雄の Status.level）。実機で値の動きを未確認のため、読み取りはここ1か所に集める。
        private static int HeroLevelOf(Hero hero)
        {
            try { return hero.Status != null ? Math.Max(1, hero.Status.level) : 1; }
            catch (Exception) { return 1; }
        }

        private LevelGemSlotLedger TrackLevelGemSkill(HeroSkill skill)
        {
            var ledger = LevelGemSlotLedgers.GetValue(skill, s =>
            {
                var store = PersistedLevelGemSlotLedgers;
                var persisted = store.GetValue(s, _ => new int[LevelGemLocations.Length * 2]);
                if (persisted.Length < LevelGemLocations.Length * 2)
                {
                    // 旧版の Q・W・E の所有記録を保ち、R の分だけ末尾に足す。
                    Array.Resize(ref persisted, LevelGemLocations.Length * 2);
                    store.Remove(s);
                    store.Add(s, persisted);
                }
                return LevelGemSlotLedger.Capture(s, persisted);
            });
            if (!_trackedLevelGemSkills.TryGetValue(skill, out _))
            {
                _trackedLevelGemSkills.Add(skill, ledger);
                _levelGemSkills.Add(new WeakReference<HeroSkill>(skill));
            }
            return ledger;
        }

        private void ApplyLevelGemSlots(HeroSkill skill, int level, LevelEssenceSettings settings)
        {
            var ledger = TrackLevelGemSkill(skill);
            int cap = Math.Min(settings.Ceiling, LevelEssenceSettings.RuntimeCapLimit);
            for (int i = 0; i < LevelGemLocations.Length; i++)
            {
                int native = ledger.Locations[i].DecideRemoval(skill.GetMaxGemCount(LevelGemLocations[i])).Target;
                UpdateGemSlot(skill, ledger.Locations[i], LevelGemLocations[i],
                    native >= cap || !LevelEssenceSettings.RuntimeBonusAvailable ? 0 : settings.Desired(LevelGemTargets[i], level), false, cap);
            }
            ledger.Persist();
        }

        // 継続の復元で保存済みのエッセンスが枠の外に出ているあいだは、すでに足した分を縮めずに保つ。
        private void ProtectNativeContinueLevelGemSlots()
        {
            var settings = LevelEssenceSettings.Current ?? LevelEssenceSettings.Off;
            foreach (var player in DewPlayer.gamePlayers)
            {
                var hero = player != null ? player.hero : null;
                var skill = hero != null && hero.isActive ? hero.Skill : null;
                if (skill == null || !PendingNativeContinueGemSource(skill)) continue;
                var ledger = TrackLevelGemSkill(skill);
                for (int i = 0; i < LevelGemLocations.Length; i++)
                    UpdateGemSlot(skill, ledger.Locations[i], LevelGemLocations[i],
                        ledger.Locations[i].OurContribution, false, Math.Min(settings.Ceiling, LevelEssenceSettings.RuntimeCapLimit));
                ledger.Persist();
            }
        }

        private void DetachLevelGemSlots()
        {
            foreach (var weak in _levelGemSkills)
                if (weak.TryGetTarget(out var skill))
                {
                    RestoreLevelGemSlots(skill);
                    _trackedLevelGemSkills.Remove(skill);
                }
            _levelGemSkills.Clear();
            _nextLevelGemSlotCheck = 0f;
        }

        private void RestoreLevelGemSlots(HeroSkill skill)
        {
            if (ClientSession.NativeContinueRestoring) return;
            if (skill == null || !LevelGemSlotLedgers.TryGetValue(skill, out var ledger)) return;
            // 非アクティブな本体には書けない。同じ本体が戻ってきたときのために所有の記録は残す。
            if (skill.hero == null || !skill.hero.isActive) return;
            for (int i = 0; i < LevelGemLocations.Length; i++)
                UpdateGemSlot(skill, ledger.Locations[i], LevelGemLocations[i], 0, true);
            ledger.Persist();
        }
    }
}
