using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public static partial class HostBuildValidation
    {
        public static Profile CompatibleProfile(Profile profile, string heroKey)
        {
            var source = profile.Hero(heroKey);
            if (source.ExtraPoints == 0) return profile;
            var snapshot = profile.Clone();
            var hero = snapshot.Hero(heroKey);
            hero.ExtraPoints = 0;
            Rules.ResetTalents(snapshot, heroKey);
            var engine = Rules.AllocationValidationForHero(heroKey);
            int remaining = snapshot.TalentPoints(heroKey);
            var pending = new List<string>(source.Talents.Keys);
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < pending.Count;)
                {
                    string id = pending[i];
                    var talent = engine.Talent(id);
                    if (talent == null || !engine.CanReach(hero, talent)) { i++; continue; }
                    int ranks = Math.Min(source.Talents[id], remaining / talent.RankCost);
                    if (ranks > 0)
                    {
                        hero.Talents.Add(id, ranks);
                        if (source.TalentChoices.TryGetValue(id, out int choice)) hero.TalentChoices.Add(id, choice);
                        remaining -= ranks * talent.RankCost;
                        changed = true;
                    }
                    pending.RemoveAt(i);
                }
                foreach (string id in source.Keystones)
                {
                    if (id == null || hero.HasKeystone(id)) continue;
                    var talent = engine.Talent(id);
                    int cost = talent?.KeystoneDefinition?.Cost ?? Content.KeystoneCost;
                    if (talent == null || remaining < cost || hero.KeystoneCount >= hero.KeystoneSlotCount
                        || !engine.KeystoneUnlocked(hero, heroKey, talent)) continue;
                    hero.AddKeystone(id);
                    remaining -= cost;
                    changed = true;
                }
            } while (changed);
            return snapshot;
        }

        /// <summary>
        /// 禁忌化（#415）を解釈できない旧ホストへの送信用に、旧ホストが拒否する遺物を装備から外したコピーを作る。
        /// 対象は禁忌化した遺物（15欄形式を読めない）と、旧上限（Content.MaxRetunes）を超えて再調律した遺物
        /// （職人解放後のオレンジ枠。旧ホストの HostGearValidation は Retunes の超過だけでビルド全体を拒否する）。
        /// 印や回数だけを落として送るとホスト側の能力計算が変わるため、遺物ごと外して明示的な制限とする。
        /// 外すものがなければ元の Profile を返す（コピーしない）。
        /// </summary>
        public static Profile WithoutUnsupportedGear(Profile profile, string heroKey)
        {
            bool Stripped(Relic r) => r != null && (r.Forbidden != null || r.Retunes > Content.MaxRetunes);
            var source = profile.Hero(heroKey);
            bool any = false;
            foreach (string uid in source.Equipped)
            {
                if (uid != null && Stripped(profile.FindStash(uid))) { any = true; break; }
            }
            if (!any) return profile;
            var snapshot = profile.Clone();
            var hero = snapshot.Hero(heroKey);
            for (int slot = 0; slot < hero.Equipped.Length; slot++)
            {
                string uid = hero.Equipped[slot];
                if (uid != null && Stripped(snapshot.FindStash(uid))) hero.Equipped[slot] = null;
            }
            return snapshot;
        }
    }
}
