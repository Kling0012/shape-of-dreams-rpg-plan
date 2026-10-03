using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    public sealed partial class PowerRuntime
    {
        /// <summary>The host sets this only when explicit C09 definitions bind the successful-hit ledger.</summary>
        public bool UsesMemoryPreparationLedger { get; set; }

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
