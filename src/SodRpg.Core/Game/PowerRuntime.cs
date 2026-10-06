using System;
using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>ある時点で付けるべき一時的な補正（表示単位）。</summary>
    public struct DynamicBonus
    {
        public float AttackSpeedPct;
        public float AttackPct;
        public float PowerPct;
        public int MoveSpeedPct;
        public float MaxHealthPct;
        public float Armor;
    }

    /// <summary>
    /// 1キャラ分の固有効果の状態（スタック・残り時間・クールダウン）。時刻は呼び出し側が渡す（秒）。
    /// ゲームへの作用（回復・ダメージ・障壁）は戻り値で知らせ、実行は接続層が行う。
    /// </summary>
    public sealed partial class PowerRuntime
    {
        // 調整数値の原本は tools/balance/powers.json → Balance/Powers.Generated.cs（Issue #149 段階8）。
        public const int MomentumMaxStacks = PowersBalance.MomentumMaxStacks;
        public const float MomentumDuration = PowersBalance.MomentumDuration;
        public const float RetaliationDuration = PowersBalance.RetaliationDuration;
        public const float TailwindDuration = PowersBalance.TailwindDuration;
        public const float BarrierInterval = PowersBalance.BarrierInterval;
        /// <summary>護りの灯の最初の障壁までの時間（以後は BarrierInterval ごと）。</summary>
        public const float BarrierFirstDelay = PowersBalance.BarrierFirstDelay;
        /// <summary>護りの灯の障壁が残る時間（v1.28：12秒続くと常に張られている状態になるため短く）。</summary>
        public const float BarrierDuration = PowersBalance.BarrierDuration;
        /// <summary>星の盾（Ultimate で張る障壁）が残る時間。</summary>
        public const float StarShieldDuration = PowersBalance.StarShieldDuration;
        public const float SecondWindCooldown = PowersBalance.SecondWindCooldown;
        public const float SecondWindThreshold = PowersBalance.SecondWindThreshold;
        public const float LifestealInterval = PowersBalance.LifestealInterval;
        public const float ThornsInterval = PowersBalance.ThornsInterval;
        public const float ExecuteThreshold = PowersBalance.ExecuteThreshold;
        public const int BulwarkEnemies = PowersBalance.BulwarkEnemies;
        public const float ResonanceRange = PowersBalance.ResonanceRange;
        public const double ChainChance = PowersBalance.ChainChance;
        public const int ChainTargets = PowersBalance.ChainTargets;
        public const float ChainRange = PowersBalance.ChainRange;
        public const float ShatterRadius = PowersBalance.ShatterRadius;
        public const float AegisThreshold = PowersBalance.AegisThreshold; // v1.28：タンクは最大HPが多く20%の一撃はまれなので10%に
        public const float AegisCooldown = PowersBalance.AegisCooldown;
        public const float BloodlustThreshold = PowersBalance.BloodlustThreshold;
        public const float ConvergenceCooldown = PowersBalance.ConvergenceCooldown;
        public const float SurgeDuration = PowersBalance.SurgeDuration;
        public const float SoulSiphonInterval = PowersBalance.SoulSiphonInterval;
        public const float WhirlwindInterval = PowersBalance.WhirlwindInterval;
        public const float WhirlwindRadius = PowersBalance.WhirlwindRadius;
        public const int FrenzyMaxEnemies = PowersBalance.FrenzyMaxEnemies; // v1.28：敵が多く出るので5体では頭打ちが早い
        public const float OpeningStrikeThreshold = PowersBalance.OpeningStrikeThreshold;
        public const float SprintDuration = PowersBalance.SprintDuration;
        /// <summary>回避の残響：回避の後、次の通常攻撃への上乗せができる猶予（新しい回避で延びる）。</summary>
        public const float EchoingDodgeWindow = PowersBalance.EchoingDodgeWindow;
        /// <summary>瞬歩の刃：回避・ダッシュ・瞬間移動の後、次の通常攻撃への上乗せができる猶予。</summary>
        public const float ShadowStepWindow = PowersBalance.ShadowStepWindow;
        public const float VigorThreshold = PowersBalance.VigorThreshold;
        public const float OverloadDuration = PowersBalance.OverloadDuration;
        public const float FinaleWindow = PowersBalance.FinaleWindow;
        public const float FinaleCooldown = PowersBalance.FinaleCooldown;
        public const float CriticalEchoCooldown = PowersBalance.CriticalEchoCooldown;
        public const int CrystalResonanceMaxTiers = PowersBalance.CrystalResonanceMaxTiers;
        public const int PreyPrideMaxLevel = PowersBalance.PreyPrideMaxLevel;
        public const int DevotionMaxStacks = PowersBalance.DevotionMaxStacks;
        public const float OverflowingLifeDuration = PowersBalance.OverflowingLifeDuration;
        public const float OverflowingLifeMaxHealthRatio = PowersBalance.OverflowingLifeMaxHealthRatio;
        public const int WildfireMinStacks = PowersBalance.WildfireMinStacks;
        public const float WildfireRange = PowersBalance.WildfireRange;
        public const float WildfireCooldown = PowersBalance.WildfireCooldown;
        public const float StillWaterDuration = PowersBalance.StillWaterDuration;
        public const float StillWaterCooldown = PowersBalance.StillWaterCooldown;
        public const int SpendersWardGold = PowersBalance.SpendersWardGold;
        public const int SpendersWardMaxStacks = PowersBalance.SpendersWardMaxStacks;
        public const float SpendersWardDuration = PowersBalance.SpendersWardDuration;
        public const float PerfectReadDuration = PowersBalance.PerfectReadDuration;
        public const float PerfectReadCooldown = PowersBalance.PerfectReadCooldown;
        public const int LucidBoonMaxDreams = PowersBalance.LucidBoonMaxDreams;
        /// <summary>連携（記憶の余韻）の持続時間。効果は重ならず、時間だけ伸びる。</summary>
        public const float LinkSurgeDuration = PowersBalance.LinkSurgeDuration;

        private readonly Dictionary<LinkDef, float> _linkSurges = new Dictionary<LinkDef, float>();
        private readonly List<LinkDef> _expiredLinkSurges = new List<LinkDef>();

        private float _momentumUntil;
        private float _retaliationUntil;
        private float _tailwindUntil;
        private float _nextBarrier;
        private float _secondWindReady;
        private float _lifestealReady;
        private float _thornsReady;
        private float _aegisReady;
        private bool _nextHitIsFourth;
        private float _surgeUntil;
        private float _soulSiphonReady;
        private float _whirlwindReady;
        private float _sprintUntil;
        private float _echoUntil = float.NegativeInfinity;
        private float _shadowStepUntil = float.NegativeInfinity;
        private float _overloadUntil;
        private float _finaleQ = float.NegativeInfinity;
        private float _finaleW = float.NegativeInfinity;
        private float _finaleE = float.NegativeInfinity;
        private float _finaleReady;
        private float _criticalEchoReady;
        private float _stillWaterReady = float.NegativeInfinity;
        private float _perfectReadReady = float.NegativeInfinity;
        private float _perfectReadUntil = float.NegativeInfinity;
        private int _wardGoldRemainder;
        private readonly float[] _wardUntil = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
        private readonly Dictionary<int, float> _wildfireReady = new Dictionary<int, float>();
        private readonly List<int> _expiredWildfire = new List<int>();
        private float _nextWildfirePrune;
        private readonly Dictionary<int, float> _convergenceReady = new Dictionary<int, float>();
        private readonly Rng _rng;

        /// <summary>現在のHP割合（ホストが毎フレーム設定）。血の渇き・不屈に使う。</summary>
        public float HealthRatio { get; set; } = 1f;

        public PowerRuntime(Build build, float now, ulong seed = 1)
        {
            Build = build ?? new Build();
            _nextBarrier = now + PowersBalance.BarrierFirstDelay;
            _rng = new Rng(seed);
            _lastCombat = now;
        }

        /// <summary>本体の通常攻撃が放たれた。4発目なら次の命中で烈火が発動する（Vesper・Lacerta の4発目と連動）。</summary>
        public void OnAttackFired(bool isFourthAttack)
        {
            _nextHitIsFourth = isFourthAttack; // 4発目が外れたら、次に放った攻撃で取り消す
        }

        /// <summary>本体が回避・ダッシュ・瞬間移動した。瞬歩の刃だけを準備し、再移動では時間だけを延長する。</summary>
        public void OnSelfMovement(float now)
        {
            if (Build.Get(Power.ShadowStep) > 0) _shadowStepUntil = now + ShadowStepWindow;
        }

        /// <summary>Memory を使った。一時補正を始める。回避なら回避の残響・瞬歩の刃の3秒も始める（重ならず延長）。</summary>
        public void OnSkillUsed(float now, bool isMovement, bool isUltimate, bool isIdentity = false)
        {
            if (isUltimate && Build.Get(Power.UltimateSurge) > 0) _surgeUntil = now + SurgeDuration;
            if (isMovement && Build.Get(Power.Sprint) > 0) _sprintUntil = now + SprintDuration;
            if (isMovement && Build.Get(Power.EchoingDodge) > 0) _echoUntil = now + EchoingDodgeWindow;
            if (isMovement) OnSelfMovement(now);
            if (!isMovement && !isUltimate && !isIdentity && Build.Get(Power.Overload) > 0) _overloadUntil = now + OverloadDuration;
        }

        public struct SkillResult
        {
            public float WhirlwindDamage;
            public float Shield;
        }

        /// <summary>Memory の使用結果。旋風・星の加護の量を返す。旋風は攻撃力と魔力の高い方（attackOrPower）で計算する。</summary>
        public SkillResult OnSkillUsed(float now, bool isMovement, bool isUltimate, float attackOrPower, float maxHealth, bool isIdentity = false)
        {
            OnSkillUsed(now, isMovement, isUltimate, isIdentity);
            var r = new SkillResult();
            int whirlwind = Build.Get(Power.Whirlwind);
            if (isMovement && whirlwind > 0 && now >= _whirlwindReady)
            {
                _whirlwindReady = now + WhirlwindInterval;
                r.WhirlwindDamage = attackOrPower * whirlwind / 100f;
            }
            int starShield = Build.Get(Power.StarShield);
            if (isUltimate && starShield > 0) r.Shield = maxHealth * starShield / 100f;
            return r;
        }

        /// <summary>四元の共鳴：敵に4属性が揃っていれば爆発ダメージを返す（同じ敵へは6秒に1回）。攻撃力と魔力の高い方（attackOrPower）で計算する。</summary>
        public float TakeConvergence(float now, int victimId, bool allFour, float attackOrPower)
        {
            int v = Build.Get(Power.Convergence);
            if (v <= 0 || !allFour) return 0;
            if (_convergenceReady.TryGetValue(victimId, out float ready) && now < ready) return 0;
            _convergenceReady[victimId] = now + ConvergenceCooldown;
            // Active victim cooldowns must survive large encounters.
            if (_convergenceReady.Count > 200)
            {
                var expired = new List<int>();
                foreach (var pair in _convergenceReady)
                    if (now >= pair.Value) expired.Add(pair.Key);
                foreach (int id in expired) _convergenceReady.Remove(id);
            }
            return attackOrPower * v / 100f;
        }

        public Build Build { get; private set; }
        public int MomentumStacks { get; private set; }
        public int NearbyEnemies { get; set; }
        /// <summary>共鳴：自分の分（味方が近くにいれば全量、いなければ半分）。</summary>
        public int ResonanceSelf { get; set; }
        /// <summary>共鳴：近くの味方から分けてもらった分。</summary>
        public int ResonanceShared { get; set; }

        /// <summary>装着中のエッセンスの品質合計（%）。</summary>
        public int GemQualityTotal { get; set; }
        /// <summary>現在のハンター追跡度。補正は0〜3に制限する。</summary>
        public int HuntLevel { get; set; }
        public int DevotionStacks { get; private set; }
        /// <summary>有効なEvilの明晰夢の数。6つまで、補正の合計は18%まで。</summary>
        public int EvilDreamCount { get; set; }

        /// <summary>連携（同調）：条件を満たしている間の攻撃力・魔力%（ホストの定期走査で更新）。</summary>
        public float LinkAttunePct { get; set; }
        /// <summary>連携（守り）：条件を満たしている間の最大HP%（ホストの定期走査で更新）。</summary>
        public float LinkGuardHealthPct { get; set; }
        /// <summary>連携（守り）：条件を満たしている間の防御（ホストの定期走査で更新）。</summary>
        public float LinkGuardArmor { get; set; }

        /// <summary>余韻は重ならない。発動元ごとに、装着中だけ5秒の期限を延長する。</summary>
        public void OnLinkSurge(float now, LinkDef source)
        {
            if (source != null && source.Kind == LinkKind.MemorySurge && source.Value > 0)
                _linkSurges[source] = now + LinkSurgeDuration;
        }

        /// <summary>装着条件を外した余韻だけを消す。他の有効な発動元は残す。</summary>
        public void RetainLinkSurges(IReadOnlyList<LinkDef> satisfied, float now)
        {
            _expiredLinkSurges.Clear();
            foreach (var pair in _linkSurges)
            {
                bool keep = false;
                if (now < pair.Value && satisfied != null)
                    for (int i = 0; i < satisfied.Count; i++)
                        if (ReferenceEquals(satisfied[i], pair.Key)) { keep = true; break; }
                if (!keep) _expiredLinkSurges.Add(pair.Key);
            }
            foreach (var source in _expiredLinkSurges) _linkSurges.Remove(source);
        }

        /// <summary>使った記憶の連携加速を合計する。1回の発動でクールダウン全量（100%）まで。</summary>
        public static float LinkHastePercent(IReadOnlyList<LinkDef> satisfied, string usedMemory)
        {
            long total = 0;
            if (satisfied == null || usedMemory == null) return 0;
            for (int i = 0; i < satisfied.Count; i++)
            {
                var link = satisfied[i];
                if (link == null || link.Kind != LinkKind.MemoryHaste || link.Requires == null
                    || Array.IndexOf(link.Requires, usedMemory) < 0) continue;
                total += Math.Max(0, link.ValueMilli);
                if (total >= 100 * BuildPrecision.Scale) return 100;
            }
            return total / (float)BuildPrecision.Scale;
        }

        /// <summary>止水：自分の技によるスタンで張る障壁量。成功したときだけ内部CDを開始する。</summary>
        public float TakeStillWater(float now, bool ownSkillStun, float maxHealth)
        {
            int v = Math.Min(Content.PowerCap(Power.StillWater), Build.Get(Power.StillWater));
            if (v <= 0 || !ownSkillStun || maxHealth <= 0 || now < _stillWaterReady) return 0;
            _stillWaterReady = now + StillWaterCooldown;
            return maxHealth * v / 100f;
        }

        /// <summary>散財の護り：新たに張る障壁量だけを返す。既存の障壁の期限は延長しない。</summary>
        public float TakeSpendersWard(float now, int goldSpent, float maxHealth)
        {
            int v = Math.Min(Content.PowerCap(Power.SpendersWard), Build.Get(Power.SpendersWard));
            if (v <= 0 || goldSpent <= 0 || maxHealth <= 0) return 0;
            long gold = (long)_wardGoldRemainder + goldSpent;
            int grants = (int)(gold / SpendersWardGold);
            _wardGoldRemainder = (int)(gold % SpendersWardGold);
            int added = 0;
            for (int i = 0; i < _wardUntil.Length && added < grants; i++)
            {
                if (now < _wardUntil[i]) continue;
                _wardUntil[i] = now + SpendersWardDuration;
                added++;
            }
            // 上限で弾かれた100ゴールド分は貯めず、100未満の端数だけを持ち越す。
            return maxHealth * v / 100f * added;
        }

        public int SpendersWardStacks(float now)
        {
            int stacks = 0;
            foreach (float until in _wardUntil)
                if (now < until) stacks++;
            return stacks;
        }

        /// <summary>見切り：無敵で実際に無効化したときだけ、重ならない時限補正を更新する。</summary>
        public bool TakePerfectRead(float now, bool negatedByInvulnerability)
        {
            if (Build.Get(Power.PerfectRead) <= 0 || !negatedByInvulnerability || now < _perfectReadReady) return false;
            _perfectReadReady = now + PerfectReadCooldown;
            _perfectReadUntil = now + PerfectReadDuration;
            return true;
        }

        /// <summary>終曲：slot は Q=0/W=1/E=2。3種を8秒以内に使うとR短縮の割合を返す。</summary>
        public float TakeFinale(float now, int slot)
        {
            int v = Build.Get(Power.Finale);
            if (v <= 0) return 0;
            switch (slot)
            {
                case 0: _finaleQ = now; break;
                case 1: _finaleW = now; break;
                case 2: _finaleE = now; break;
                default: return 0;
            }
            if (now < _finaleReady || now - _finaleQ > FinaleWindow
                || now - _finaleW > FinaleWindow || now - _finaleE > FinaleWindow) return 0;
            _finaleReady = now + FinaleCooldown;
            // 同じ組み合わせを使い回さず、次の発動には3種を改めて使う。
            _finaleQ = _finaleW = _finaleE = float.NegativeInfinity;
            return v / 100f;
        }

        /// <summary>会心の余韻：通常攻撃の会心でQ/W/Eを短縮する秒数。</summary>
        public float TakeCriticalEcho(float now, bool isCrit)
        {
            int v = Build.Get(Power.CriticalEcho);
            if (v <= 0 || !isCrit || now < _criticalEchoReady) return 0;
            _criticalEchoReady = now + CriticalEchoCooldown;
            return v / 10f;
        }

        /// <summary>足枷：対象にスタン・スロウ・冷気がある場合のダメージ増幅。</summary>
        public float FettersAmplification(bool hindered) =>
            hindered ? Math.Max(0, Build.Get(Power.Fetters)) / 100f : 0;

        /// <summary>溢れる命：1回の超過回復から張る障壁量。</summary>
        public float TakeOverflowingLife(float discardedAmount, float maxHealth)
        {
            int v = Build.Get(Power.OverflowingLife);
            if (v <= 0 || discardedAmount <= 0 || maxHealth <= 0) return 0;
            return Math.Min(discardedAmount * v / 100f, maxHealth * OverflowingLifeMaxHealthRatio);
        }

        /// <summary>祈願：成功した聖堂使用を積む。上限到達時や未装着ならfalse。</summary>
        public bool OnShrineUsed()
        {
            if (Build.Get(Power.Devotion) <= 0 || DevotionStacks >= DevotionMaxStacks) return false;
            DevotionStacks++;
            return true;
        }

        public void OnZoneLoaded(float now = 0)
        {
            DevotionStacks = 0;
            ResetNewPowersForZone();
            _lastCombat = now;
        }

        /// <summary>飛び火：火3重以上からの伝播判定。同じ敵からは成功後2秒待つ。</summary>
        public bool TakeWildfire(float now, int victimId, int fireStacks, double roll)
        {
            int v = Build.Get(Power.Wildfire);
            if (v <= 0 || fireStacks < WildfireMinStacks || roll * 100 >= v) return false;
            if (_wildfireReady.TryGetValue(victimId, out float ready) && now < ready) return false;
            // 生きているクールダウンを消さず、古い敵の記録だけを再利用リストで掃除する。
            if (_wildfireReady.Count > 200 && now >= _nextWildfirePrune)
            {
                _nextWildfirePrune = now + WildfireCooldown;
                _expiredWildfire.Clear();
                foreach (var kv in _wildfireReady)
                    if (now >= kv.Value) _expiredWildfire.Add(kv.Key);
                foreach (int id in _expiredWildfire) _wildfireReady.Remove(id);
            }
            _wildfireReady[victimId] = now + WildfireCooldown;
            return true;
        }
        public void SetBuild(Build build)
        {
            var replacement = build ?? new Build();
            RetainGimmickPrimedForBuild(replacement);
            Build = replacement;
            RetainEquippedNewPowerState();
            // Recheck passive conditions on the next scan; retain only unchanged surge sources.
            LinkAttunePct = 0;
            LinkGuardHealthPct = 0;
            LinkGuardArmor = 0;
            // SetBuild has no clock. The next timed scan still expires retained windows normally.
            RetainLinkSurges(Build.Links, float.NegativeInfinity);
            _expiredLinkSurges.Clear();
        }

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

        /// <summary>撃破した。爆砕の範囲ダメージ（攻撃力と魔力の高い方で計算。0ならなし）を返す。</summary>
        public float OnKill(float now, float attackOrPower)
        {
            OnKill(now);
            int shatter = Build.Get(Power.Shatter);
            return shatter > 0 ? attackOrPower * shatter / 100f : 0;
        }

        public struct KillResult
        {
            public float ShatterDamage;
            public float Heal;
        }

        /// <summary>撃破した。爆砕の範囲ダメージ（攻撃力と魔力の高い方で計算）と吸魂の回復量を返す。</summary>
        public KillResult OnKill(float now, float attackOrPower, float maxHealth)
        {
            var r = new KillResult { ShatterDamage = OnKill(now, attackOrPower) };
            int soulSiphon = Build.Get(Power.SoulSiphon);
            if (soulSiphon > 0 && maxHealth > 0 && now >= _soulSiphonReady)
            {
                _soulSiphonReady = now + SoulSiphonInterval;
                r.Heal = maxHealth * soulSiphon / 1000f;
            }
            return r;
        }

        /// <summary>守護霊：大きな一撃を受けたら障壁量を返す（間隔は AegisCooldown 秒）。</summary>
        public float TakeAegis(float now, float amount, float maxHealth)
        {
            int aegis = Build.Get(Power.Aegis);
            if (aegis <= 0 || maxHealth <= 0 || now < _aegisReady || amount < maxHealth * AegisThreshold) return 0;
            _aegisReady = now + AegisCooldown;
            return maxHealth * aegis / 100f;
        }

        /// <summary>被弾した。棘で返すダメージ（0なら返さない）を返す。</summary>
        public float OnDamaged(float now, float amount, bool attackerIsEnemy, bool? attackedByEnemy = null)
        {
            if (amount <= 0) return 0;
            // 逆襲：敵に攻撃されたときだけ。自傷・味方由来のダメージでは発動しない。
            if (Build.Get(Power.Retaliation) > 0 && (attackedByEnemy ?? attackerIsEnemy)) _retaliationUntil = now + RetaliationDuration;
            int thorns = Build.Get(Power.Thorns);
            if (thorns <= 0 || !attackerIsEnemy || now < _thornsReady) return 0;
            _thornsReady = now + ThornsInterval;
            return amount * thorns / 100f;
        }

        /// <summary>逆襲（被弾後3秒）の窓の内側か。刻印の条件式（M6）はこの既存窓を参照する。</summary>
        public bool WithinRetaliationWindow(float now) => now < _retaliationUntil;

        public struct HitResult
        {
            public float Heal;
            public float ExecuteDamage;
            public float OpeningDamage;
            public float BlazeDamage;
            /// <summary>雷鎖：近くの敵（最大2体）へ与えるダメージ。</summary>
            public float ChainDamage;
            /// <summary>回避の残響：回避後3秒以内の次の通常攻撃に上乗せるダメージ（その命中で消費）。</summary>
            public float EchoDamage;
            /// <summary>瞬歩の刃：本体の移動後3秒以内の次の通常攻撃に上乗せるダメージ（その命中で消費）。</summary>
            public float ShadowStepDamage;
            public float RunUpDamage;
            public float PrimedDamage;
            /// <summary>付与する属性のスタック数（火・冷気・光・闇の順）。</summary>
            public int FireStacks, ColdStacks, LightStacks, DarkStacks;
        }

        /// <summary>
        /// 通常攻撃が命中した。回復量・追加ダメージ・属性のスタック数を返す。
        /// 烈火・雷鎖・回避の残響・瞬歩の刃は攻撃力と魔力の高い方、処刑・先制は攻撃力で計算する。
        /// </summary>
        public HitResult OnAttackHit(float now, float maxHealth, float attackDamage, float abilityPower,
            float victimHealthRatio, bool isCrit = false, double roll = 1.0, bool consumeNextBasic = true)
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
            int opening = Build.Get(Power.OpeningStrike);
            if (opening > 0 && victimHealthRatio >= OpeningStrikeThreshold) r.OpeningDamage = attackDamage * opening / 100f;
            float higher = Math.Max(attackDamage, abilityPower);
            int blaze = Build.Get(Power.Blaze);
            if (blaze > 0 && _nextHitIsFourth) r.BlazeDamage = higher * blaze / 100f;
            _nextHitIsFourth = false;
            if (consumeNextBasic) TakeLargestNextBasic(now, higher, ref r);
            r.FireStacks = ElementStacks(Power.Ember);
            r.ColdStacks = ElementStacks(Power.Frost);
            r.LightStacks = ElementStacks(Power.Radiance);
            r.DarkStacks = ElementStacks(Power.Umbra);
            // 闇だけ、会心で当たったらさらに1つ重ねる（影を持るときだけ）。
            if (isCrit && Build.Get(Power.Umbra) > 0) r.DarkStacks++;
            int chain = Build.Get(Power.ChainLightning);
            if (chain > 0 && roll < ChainChance) r.ChainDamage = higher * chain / 100f;
            return r;
        }

        /// <summary>属性付与の量。100ごとに確実に1つ、残り（100未満の分）はその%の確率でもう1つ。</summary>
        private int ElementStacks(Power p)
        {
            int v = Build.Get(p);
            if (v <= 0) return 0;
            int stacks = v / 100;
            if (_rng.NextDouble() * 100 < v % 100) stacks++;
            return stacks;
        }

        /// <summary>今付けるべき一時補正。</summary>
        public DynamicBonus Current(float now)
        {
            if (now > _momentumUntil) MomentumStacks = 0;
            // 連携（v1.26）：同調は常時、記憶の余韻は条件の記憶を使った後の5秒間。
            float linkSurge = 0;
            foreach (var pair in _linkSurges)
                if (now < pair.Value) linkSurge = Math.Max(linkSurge, pair.Key.ValuePercent);
            float linkAttack = LinkAttunePct + linkSurge;
            int conditionalTotal = ConditionalAttributes(now);
            return new DynamicBonus
            {
                AttackSpeedPct = Build.Get(Power.Momentum) * MomentumStacks + (HealthRatio < BloodlustThreshold ? Build.Get(Power.Bloodlust) : 0)
                    + Math.Max(0, Build.Get(Power.Frenzy)) * Math.Min(FrenzyMaxEnemies, Math.Max(0, NearbyEnemies))
                    + (now < _sprintUntil ? Math.Max(0, Build.Get(Power.Sprint)) : 0)
                    + (now < _perfectReadUntil ? Math.Min(Content.PowerCap(Power.PerfectRead), Math.Max(0, Build.Get(Power.PerfectRead))) : 0),
                AttackPct = conditionalTotal + linkAttack,
                PowerPct = conditionalTotal + linkAttack,
                MoveSpeedPct = (now < _tailwindUntil ? Build.Get(Power.Tailwind) : 0)
                    + (now < _sprintUntil ? Math.Max(0, Build.Get(Power.Sprint)) : 0),
                MaxHealthPct = LinkGuardHealthPct,
                Armor = (NearbyEnemies >= BulwarkEnemies ? Build.Get(Power.Bulwark) : 0) + LinkGuardArmor,
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
                h._resonanceSharedBase = 0;
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
                    if (v / 2 > heroes[j].ResonanceShared)
                    {
                        heroes[j].ResonanceShared = v / 2;
                        heroes[j]._resonanceSharedBase = heroes[i].Build.ConditionalBase(Power.Resonance) / 2f;
                    }
                }
                heroes[i].ResonanceSelf = anyAlly ? v : v / 2;
            }
        }
    }

    /// <summary>表示単位からゲームの StatBonus の単位への変換。会心・属性は割合（1% = 0.01）。</summary>
    public static class StatUnits
    {
        /// <summary>小数を許す値（RunGrowth など）をゲームの単位へ。整数版と同じ換算。</summary>
        public static float ToGame(Stat s, double value)
        {
            switch (s)
            {
                case Stat.CritChancePct:
                case Stat.CritDamagePct:
                case Stat.FireAmp:
                case Stat.ColdAmp:
                case Stat.LightAmp:
                case Stat.DarkAmp:
                    return (float)(value / 100d);
                default:
                    return (float)value;
            }
        }

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
                case Stat.FourthAttackShift:
                    return value;
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
