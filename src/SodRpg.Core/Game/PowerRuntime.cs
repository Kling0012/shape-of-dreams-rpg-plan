using System;

namespace SodRpg.Core.Game
{
    /// <summary>ある時点で付けるべき一時的な補正（表示単位）。</summary>
    public struct DynamicBonus
    {
        public int AttackSpeedPct;
        public int AttackPct;
        public int PowerPct;
        public int MoveSpeedPct;
        public int Armor;
    }

    /// <summary>
    /// 1キャラ分の固有効果の状態（スタック・残り時間・クールダウン）。時刻は呼び出し側が渡す（秒）。
    /// ゲームへの作用（回復・ダメージ・障壁）は戻り値で知らせ、実行は接続層が行う。
    /// </summary>
    public sealed class PowerRuntime
    {
        public const int MomentumMaxStacks = 5;
        public const float MomentumDuration = 4f;
        public const float RetaliationDuration = 3f;
        public const float TailwindDuration = 2f;
        public const float BarrierInterval = 12f;
        public const float SecondWindCooldown = 60f;
        public const float SecondWindThreshold = 0.3f;
        public const float LifestealInterval = 0.15f;
        public const float ThornsInterval = 1f;
        public const float ExecuteThreshold = 0.3f;
        public const int BlazeEvery = 4;
        public const int BulwarkEnemies = 3;
        public const float ResonanceRange = 10f;
        public const double ChainChance = 0.25;
        public const int ChainTargets = 2;
        public const float ChainRange = 6f;
        public const float ShatterRadius = 4f;
        public const float AegisThreshold = 0.2f;
        public const float AegisCooldown = 20f;
        public const float BloodlustThreshold = 0.5f;

        private float _momentumUntil;
        private float _retaliationUntil;
        private float _tailwindUntil;
        private float _nextBarrier;
        private float _secondWindReady;
        private float _lifestealReady;
        private float _thornsReady;
        private int _blazeCounter;
        private float _aegisReady;

        /// <summary>現在のHP割合（ホストが毎フレーム設定）。血の渇きに使う。</summary>
        public float HealthRatio { get; set; } = 1f;

        public PowerRuntime(Build build, float now)
        {
            Build = build ?? new Build();
            _nextBarrier = now + 3f;
        }

        public Build Build { get; private set; }
        public int MomentumStacks { get; private set; }
        public int NearbyEnemies { get; set; }
        /// <summary>共鳴：自分の分（味方が近くにいれば全量、いなければ半分）。</summary>
        public int ResonanceSelf { get; set; }
        /// <summary>共鳴：近くの味方から分けてもらった分。</summary>
        public int ResonanceShared { get; set; }

        public void SetBuild(Build build) => Build = build ?? new Build();

        public void OnKill(float now)
        {
            if (Build.Get(Power.Momentum) > 0)
            {
                if (now > _momentumUntil) MomentumStacks = 0;
                MomentumStacks = Math.Min(MomentumMaxStacks, MomentumStacks + 1);
                _momentumUntil = now + MomentumDuration;
            }
            if (Build.Get(Power.Tailwind) > 0) _tailwindUntil = now + TailwindDuration;
        }

        /// <summary>撃破した。爆砕の範囲ダメージ（0ならなし）を返す。</summary>
        public float OnKill(float now, float attackDamage)
        {
            OnKill(now);
            int shatter = Build.Get(Power.Shatter);
            return shatter > 0 ? attackDamage * shatter / 100f : 0;
        }

        /// <summary>守護霊：大きな一撃を受けたら障壁量を返す（クールダウン20秒）。</summary>
        public float TakeAegis(float now, float amount, float maxHealth)
        {
            int aegis = Build.Get(Power.Aegis);
            if (aegis <= 0 || maxHealth <= 0 || now < _aegisReady || amount < maxHealth * AegisThreshold) return 0;
            _aegisReady = now + AegisCooldown;
            return maxHealth * aegis / 100f;
        }

        /// <summary>被弾した。棘で返すダメージ（0なら返さない）を返す。</summary>
        public float OnDamaged(float now, float amount, bool attackerIsEnemy)
        {
            if (amount <= 0) return 0;
            if (Build.Get(Power.Retaliation) > 0) _retaliationUntil = now + RetaliationDuration;
            int thorns = Build.Get(Power.Thorns);
            if (thorns <= 0 || !attackerIsEnemy || now < _thornsReady) return 0;
            _thornsReady = now + ThornsInterval;
            return amount * thorns / 100f;
        }

        public struct HitResult
        {
            public float Heal;
            public float ExecuteDamage;
            public float BlazeDamage;
            /// <summary>雷鎖：近くの敵（最大2体）へ与えるダメージ。</summary>
            public float ChainDamage;
        }

        /// <summary>通常攻撃が命中した。回復量と追加ダメージを返す。</summary>
        public HitResult OnAttackHit(float now, float maxHealth, float attackDamage, float victimHealthRatio, double roll = 1.0)
        {
            var r = new HitResult();
            int lifesteal = Build.Get(Power.Lifesteal);
            if (lifesteal > 0 && now >= _lifestealReady)
            {
                _lifestealReady = now + LifestealInterval;
                r.Heal = maxHealth * lifesteal / 1000f;
            }
            int exec = Build.Get(Power.Executioner);
            if (exec > 0 && victimHealthRatio < ExecuteThreshold) r.ExecuteDamage = attackDamage * exec / 100f;
            int blaze = Build.Get(Power.Blaze);
            if (blaze > 0 && ++_blazeCounter >= BlazeEvery)
            {
                _blazeCounter = 0;
                r.BlazeDamage = attackDamage * blaze / 100f;
            }
            int chain = Build.Get(Power.ChainLightning);
            if (chain > 0 && roll < ChainChance) r.ChainDamage = attackDamage * chain / 100f;
            return r;
        }

        /// <summary>今付けるべき一時補正。</summary>
        public DynamicBonus Current(float now)
        {
            if (now > _momentumUntil) MomentumStacks = 0;
            int resonance = ResonanceSelf + ResonanceShared;
            return new DynamicBonus
            {
                AttackSpeedPct = Build.Get(Power.Momentum) * MomentumStacks + (HealthRatio < BloodlustThreshold ? Build.Get(Power.Bloodlust) : 0),
                AttackPct = (now < _retaliationUntil ? Build.Get(Power.Retaliation) : 0) + resonance,
                PowerPct = resonance,
                MoveSpeedPct = now < _tailwindUntil ? Build.Get(Power.Tailwind) : 0,
                Armor = NearbyEnemies >= BulwarkEnemies ? Build.Get(Power.Bulwark) : 0,
            };
        }

        /// <summary>障壁の時刻なら障壁量（最大HP比）を返す。</summary>
        public float TakeBarrier(float now, float maxHealth)
        {
            int barrier = Build.Get(Power.Barrier);
            if (barrier <= 0 || now < _nextBarrier) return 0;
            _nextBarrier = now + BarrierInterval;
            return maxHealth * barrier / 100f;
        }

        /// <summary>灯守の発動条件を満たせば回復量を返す。</summary>
        public float TakeSecondWind(float now, float health, float maxHealth)
        {
            int sw = Build.Get(Power.SecondWind);
            if (sw <= 0 || now < _secondWindReady || maxHealth <= 0 || health >= maxHealth * SecondWindThreshold) return 0;
            _secondWindReady = now + SecondWindCooldown;
            return maxHealth * sw / 100f;
        }

        /// <summary>共鳴の配分を計算する。values[i] はキャラ i の共鳴値、near[i][j] は i と j が範囲内か。</summary>
        public static void DistributeResonance(PowerRuntime[] heroes, Func<int, int, bool> near)
        {
            foreach (var h in heroes)
            {
                h.ResonanceSelf = 0;
                h.ResonanceShared = 0;
            }
            for (int i = 0; i < heroes.Length; i++)
            {
                int v = heroes[i].Build.Get(Power.Resonance);
                if (v <= 0) continue;
                bool anyAlly = false;
                for (int j = 0; j < heroes.Length; j++)
                {
                    if (i == j || !near(i, j)) continue;
                    anyAlly = true;
                    heroes[j].ResonanceShared = Math.Max(heroes[j].ResonanceShared, v / 2);
                }
                heroes[i].ResonanceSelf = anyAlly ? v : v / 2;
            }
        }
    }

    /// <summary>表示単位からゲームの StatBonus の単位への変換。会心・属性は割合（1% = 0.01）。</summary>
    public static class StatUnits
    {
        public static float ToGame(Stat s, int value)
        {
            switch (s)
            {
                case Stat.CritChancePct:
                case Stat.CritDamagePct:
                case Stat.FireAmp:
                case Stat.ColdAmp:
                case Stat.LightAmp:
                case Stat.DarkAmp:
                    return value / 100f;
                default:
                    return value;
            }
        }
    }

    /// <summary>ゾーン内の「突破した戦闘部屋数」の変化から、ラン通算の増分を取り出す。</summary>
    public sealed class RoomCounter
    {
        private int _last = -1;

        /// <summary>新しい値を受け取り、増えた分を返す（初回・減少・ゾーン切替時は0）。</summary>
        public int Observe(int clearedInZone)
        {
            int prev = _last;
            _last = clearedInZone;
            if (prev < 0 || clearedInZone <= prev) return 0;
            return clearedInZone - prev;
        }

        /// <summary>基準値を設定し直す。現在値が分かっていれば渡す（その値からの増分を数える）。</summary>
        public void Reset(int baseline = -1) => _last = baseline;
    }
}
