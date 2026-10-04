using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed partial class PowerRuntime
    {
        /// <summary>The host sets this only when explicit C09 definitions bind the successful-hit ledger.</summary>
        public bool UsesMemoryPreparationLedger { get; set; }

        private Dictionary<string, GimmickEntry> _nextGimmickPreparations;

        public void ClearGimmickPrimed()
        {
            _primedPercent = 0;
            _primedUntil = float.NegativeInfinity;
        }

        private void RetainGimmickPrimedForBuild(Build next)
        {
            if (_primedPercent <= 0 || Build.Gimmicks.Count == 0 && next.Gimmicks.Count == 0) return;
            if (_nextGimmickPreparations == null) _nextGimmickPreparations = new Dictionary<string, GimmickEntry>(StringComparer.Ordinal);
            _nextGimmickPreparations.Clear();
            foreach (var entry in next.Gimmicks)
                if (entry.Def.Effect == GimmickEffect.Primed) _nextGimmickPreparations.Add(entry.StarId, entry);
            int count = 0;
            bool same = true;
            foreach (var entry in Build.Gimmicks)
            {
                if (entry.Def.Effect != GimmickEffect.Primed) continue;
                count++;
                if (!_nextGimmickPreparations.TryGetValue(entry.StarId, out var replacement)
                    || entry.Memory != replacement.Memory || entry.Def.Trigger != replacement.Def.Trigger
                    || entry.Def.Arg != replacement.Def.Arg || entry.Def.Cooldown != replacement.Def.Cooldown
                    || entry.Def.EffectiveValueOrAuthored != replacement.Def.EffectiveValueOrAuthored
                    || entry.Def.DurationUnits != replacement.Def.DurationUnits
                    || entry.Def.EffectiveDurationSeconds != replacement.Def.EffectiveDurationSeconds
                    || entry.Def.UncappedValue != replacement.Def.UncappedValue
                    || entry.Def.UncappedDurationUnits != replacement.Def.UncappedDurationUnits
                    || (entry.Channel == null) != (replacement.Channel == null)
                    || entry.Channel != null && FractionalScopedModifiers.ChannelKey(entry) != FractionalScopedModifiers.ChannelKey(replacement))
                    same = false;
            }
            if (!same || count != _nextGimmickPreparations.Count) ClearGimmickPrimed();
            _nextGimmickPreparations.Clear();
        }

        /// <summary>Read without consumption so all preparations compete using actual bonus damage.</summary>
        public void CollectNextBasicBonuses(float now, float higherOffense, ICollection<NextBasicBonusCandidate> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            Add("power.echoing-dodge", now < _echoUntil ? NewValue(Power.EchoingDodge) : 0, _echoUntil);
            Add("power.shadow-step", now < _shadowStepUntil ? NewValue(Power.ShadowStep) : 0, _shadowStepUntil);
            Add("power.run-up", _runUp ? NewValue(Power.RunUp) : 0, float.PositiveInfinity);
            Add("power.primed", now < _primedUntil ? _primedPercent : 0, _primedUntil);
            void Add(string id, float percent, float expiry)
            {
                if (percent > 0) output.Add(new NextBasicBonusCandidate(id, Percent(higherOffense, percent), expiry));
            }
        }

        public void ConsumeNextBasicBonus(NextBasicBonusCandidate selected)
        {
            if (selected.IsMemoryPreparation) throw new ArgumentException("The preparation runtime owns this slot.");
            switch (selected.ChannelId)
            {
                case "power.echoing-dodge": _echoUntil = float.NegativeInfinity; break;
                case "power.shadow-step": _shadowStepUntil = float.NegativeInfinity; break;
                case "power.run-up": _runUp = false; _walked = 0; break;
                case "power.primed": _primedUntil = float.NegativeInfinity; _primedPercent = 0; break;
                default: throw new ArgumentException("Unknown existing next-basic bonus.");
            }
        }
    }
}
