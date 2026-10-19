using System;
namespace SodRpg.Core.Game
{
    /// <summary>Localized v1.29 gear behavior. Values remain display units and never depend on item level.</summary>
    public static class NewPowersV129
    {
        public static bool IsPower(Power p) => p >= Power.ShieldbreakBurst && p <= Power.SpilloverStrike;
        public static bool IsConditionalAttribute(Power p)
        {
            switch (p)
            {
                case Power.Vigor: case Power.Overload: case Power.Resonance: case Power.UltimateSurge:
                case Power.Devotion: case Power.CrystalResonance: case Power.LucidBoon: case Power.PreyPride:
                case Power.Retaliation: case Power.GleamingWard: case Power.Medley: return true;
                default: return false;
            }
        }
        private static readonly Txt[] Names =
        {
            new Txt("割れ盾の衝撃", "Shieldbreak Burst", "破盾冲击"),
            new Txt("分かち合う護り", "Shared Ward", "共享之护"),
            new Txt("盾打ち", "Shield Bash", "盾击"),
            new Txt("障壁の輝き", "Gleaming Ward", "护盾光辉"),
            new Txt("共演", "Co-star", "共演"),
            new Txt("凱歌", "Triumph Song", "凯歌"),
            new Txt("見守りの手", "Watchful Hand", "守望之手"),
            new Txt("情けの返礼", "Kindness Returns", "善意的回礼"),
            new Txt("渡り鳥", "Wanderer's Edge", "候鸟之刃"),
            new Txt("一点集中", "Focus Fire", "火力集中"),
            new Txt("包囲突破", "Breakout", "突围"),
            new Txt("決闘者の流儀", "Duelist's Way", "决斗者的流派"),
            new Txt("不動の構え", "Immovable Stance", "不动架势"),
            new Txt("助走", "Run-Up", "助跑"),
            new Txt("歩き撃ち", "Strafe Shot", "走射"),
            new Txt("継ぎの手", "Relay Hand", "接力之手"),
            new Txt("星屑の巡り", "Stardust Cycle", "星屑循环"),
            new Txt("影の継承", "Umbral Heritage", "暗影继承"),
            new Txt("脆き氷", "Brittle Ice", "脆冰"),
            new Txt("元素の収穫", "Elemental Harvest", "元素收获"),
            new Txt("七色の切替", "Prism Shift", "七色切换"),
            new Txt("群れの糧", "Pack Feast", "群狼之粮"),
            new Txt("前衛の誓い", "Vanguard's Oath", "前卫之誓"),
            new Txt("死に花", "Death Bloom", "死之花"),
            new Txt("連奏", "Medley", "连奏"),
            new Txt("詠唱の薙ぎ", "Spellsweep", "咏唱横扫"),
            new Txt("素手の矜持", "Bare-Handed Pride", "空手的矜持"),
            new Txt("口火", "Opening Salvo", "开战火"),
            new Txt("畳みかけ", "Pile-On", "乘胜追击"),
            new Txt("切り札を握る", "Ace in Hand", "手握王牌"),
            new Txt("結晶の循環", "Crystal Circuit", "结晶循环"),
            new Txt("欠片の恵み", "Shard Boon", "碎片的恩惠"),
            new Txt("継ぎの命", "Lifeline", "接力生命"),
            new Txt("夢見の前兆", "Dream Omen", "梦的前兆"),
            new Txt("後衛の流儀", "Rearguard's Way", "后卫的流派"),
            new Txt("怨嗟の鐘", "Toll of Grudge", "怨叹之钟"),
            new Txt("不撓の心", "Unbowed Mind", "不屈之心"),
            new Txt("積もる運", "Piling Luck", "积攒的运气"),
            new Txt("急所の傷", "Weak-Point Wound", "要害创伤"),
            new Txt("会心の飛沫", "Crit Splash", "暴击飞溅"),
            new Txt("戻り刃", "Returning Blade", "回返之刃"),
            new Txt("備えの盾", "Ready Guard", "常备之盾"),
            new Txt("薬師の手際", "Apothecary", "药师妙手"),
            new Txt("溢れる一撃", "Spillover Strike", "满溢一击"),
        };
        public static string Name(Power p) => IsPower(p) ? Names[(int)p - (int)Power.ShieldbreakBurst].ToString() : "-";
        public static Txt Epithet(Power p) => IsPower(p)
            ? new Txt(Names[(int)p - (int)Power.ShieldbreakBurst].Ja + "の", Names[(int)p - (int)Power.ShieldbreakBurst].En) : null;
        public static string Describe(Power p, int v)
        {
            string body;
            switch (p)
            {
                case Power.ShieldbreakBurst: body = Loc.T($"敵のダメージで自分の障壁が完全に割れたとき、自分の周囲{PowersBalance.ShieldbreakBurstRadius}m以内の敵それぞれに、割れる直前の障壁残量か自分の最大HPの高い方の{v}%の範囲ダメージを与える。{PowersBalance.ShieldbreakBurstCooldown}秒に1回まで。時間切れでは発動しない。", $"Area damage +{v}% of the higher of your shield remaining before the hit or your maximum health per enemy within {PowersBalance.ShieldbreakBurstRadius}m of you when enemy damage fully breaks your shield (once per {PowersBalance.ShieldbreakBurstCooldown}s; expiry does not trigger it)"); break;
                case Power.SharedWard: body = Loc.T($"障壁を得たとき、自分の周囲{PowersBalance.SharedWardAllyRange}m以内の味方旅人それぞれに、自分が得た障壁量の{v}%の障壁を{PowersBalance.SharedWardDuration}秒間付与する。分けた障壁からは連鎖せず、重ならずに大きい方だけを適用する。", $"Shield granted to allies +{v}% of the shield you gained, for {PowersBalance.SharedWardDuration}s per allied traveler within {PowersBalance.SharedWardAllyRange}m of you (shared shields do not chain; highest shield only, without stacking)"); break;
                case Power.ShieldBash: body = Loc.T($"障壁がある間、通常攻撃が命中した敵に、現在の障壁量の{v}%の追加無属性魔法ダメージを与える。障壁は消費しない。", $"Basic attack bonus non-elemental magic damage +{v}% of your current shield amount on hit while shielded; does not consume the shield"); break;
                case Power.GleamingWard: body = Loc.T($"自分に障壁がある間、攻撃力・魔力が{v}%上がる。", $"Attack damage and ability power +{v}% while you are shielded"); break;
                case Power.CoStar: body = Loc.T($"味方が奥義の記憶を使ったとき、自分の通常記憶すべての残りクールダウンが{v}%短くなる。自分が奥義を使ったときは{v / 2f:0.#}%短くなる。", $"All your ordinary memories' remaining cooldowns -{v}% when an ally uses an Ultimate memory; -{v / 2f:0.#}% when you use your own Ultimate"); break;
                case Power.TriumphSong: body = Loc.T($"奥義の記憶を使ったとき、自分と周囲{PowersBalance.TriumphSongRange}m以内の味方旅人それぞれを、自分の最大HPの{v}%回復する。基準は受け手ではなく自分の最大HPである。", $"Health restored +{v}% of your maximum health to yourself and each allied traveler within {PowersBalance.TriumphSongRange}m when using an Ultimate memory (based on your maximum health, not the recipient's)"); break;
                case Power.WatchfulHand: body = Loc.T($"生存中の味方旅人のHPが{PowersBalance.WatchfulHandThreshold:0%}未満のとき、その味方に自分の最大HPの{v}%の障壁を{PowersBalance.WatchfulHandDuration}秒間付与する。同じ味方には{PowersBalance.WatchfulHandCooldown}秒に1回まで。距離の制限はない。", $"Shield granted to an ally +{v}% of your maximum health for {PowersBalance.WatchfulHandDuration}s while that living allied traveler is below {PowersBalance.WatchfulHandThreshold:0%} health (once per {PowersBalance.WatchfulHandCooldown}s per ally; no distance limit)"); break;
                case Power.KindnessReturns: body = Loc.T($"自分の記憶で別の味方旅人や召喚獣を回復したとき、味方への実回復量の{v}%を自分も回復する。最大HPを超えた回復分は数えない。", $"Your health restored +{v}% of actual healing granted when your memory heals another allied traveler or summon; excludes overhealing"); break;
                case Power.WanderersEdge: body = Loc.T($"直前の通常攻撃とは別の敵に通常攻撃が命中したとき、攻撃力・魔力の高い方の{v}%の追加ダメージを与える。攻撃1回につき主対象に1回で、同じ敵には{PowersBalance.WanderersEdgeCooldown}秒に1回まで。", $"Basic attack bonus damage +{v}% of the higher of your attack damage or ability power when hitting a different enemy from your previous basic attack (once on the primary target per attack; once per {PowersBalance.WanderersEdgeCooldown}s per enemy)"); break;
                case Power.FocusFire: body = Loc.T($"同じ敵へ通常攻撃を{PowersBalance.FocusFireHits}回続けて当てるたび、攻撃力・魔力の高い方の{v}%の追加ダメージを与える。別の敵に当てるか{PowersBalance.FocusFireWindow}秒以上空くと、数え直す。", $"Basic attack bonus damage +{v}% of the higher of your attack damage or ability power every {PowersBalance.FocusFireHits} consecutive hits on the same enemy (resets on a different target or a gap of at least {PowersBalance.FocusFireWindow}s)"); break;
                case Power.Breakout: body = Loc.T($"自分の周囲{PowersBalance.BreakoutRange}m以内の敵が{PowersBalance.BreakoutMinEnemies}体以下から{PowersBalance.BreakoutTriggerEnemies}体以上になったとき、最大HPの{v}%の障壁を{PowersBalance.BreakoutShieldDuration}秒間得て、{PowersBalance.BreakoutRange}m以内の敵の移動速度を{PowersBalance.BreakoutSlowDuration}秒間{PowersBalance.BreakoutSlowPercent}%下げる。{PowersBalance.BreakoutCooldown}秒に1回まで。", $"Shield +{v}% of your maximum health for {PowersBalance.BreakoutShieldDuration}s and enemy movement speed -{PowersBalance.BreakoutSlowPercent}% for {PowersBalance.BreakoutSlowDuration}s when the enemy count within {PowersBalance.BreakoutRange}m of you rises from {PowersBalance.BreakoutMinEnemies} or fewer to {PowersBalance.BreakoutTriggerEnemies} or more; slows all enemies within {PowersBalance.BreakoutRange}m (once per {PowersBalance.BreakoutCooldown}s)"); break;
                case Power.DuelistsWay: body = Loc.T($"自分の周囲{PowersBalance.DuelistsWayRange}m以内に敵が1体だけいる間、その敵に通常攻撃が命中したとき、攻撃力・魔力の高い方の{v}%の追加ダメージを与える。", $"Basic attack bonus damage +{v}% of the higher of your attack damage or ability power against the sole enemy within {PowersBalance.DuelistsWayRange}m of you"); break;
                case Power.ImmovableStance: body = Loc.T($"{PowersBalance.ImmovableStanceStillSeconds}秒以上動かずにいる間、与えるダメージが{v}%増え、受けるダメージが{v / 2}%減る。動くと解除される。軽減は同じ効果の合計で最大{PowersBalance.ImmovableStanceMaxReduction}%まで。", $"Damage dealt +{v}%, damage taken -{v / 2}% after standing still for {PowersBalance.ImmovableStanceStillSeconds}s, until you move; combined reduction from this effect capped at {PowersBalance.ImmovableStanceMaxReduction}%"); break;
                case Power.RunUp: body = Loc.T($"歩いて合計{PowersBalance.RunUpDistance}m動いた後、次の通常攻撃が命中したとき、攻撃力・魔力の高い方の{v}%の追加ダメージを1回与える。回避・ダッシュ・瞬間移動は数えず、世界を移ると距離を0に戻す。次の通常攻撃に付く追加効果は、最大の1つだけを消費し、残りは保持される。", $"Next basic attack bonus damage +{v}% of the higher of your attack damage or ability power after walking {PowersBalance.RunUpDistance}m; consumed on the next hit. Dodges, dashes and teleports do not count; walking progress resets between worlds. Only the largest next-basic bonus is consumed; others remain"); break;
                case Power.StrafeShot: body = Loc.T($"移動しながら通常攻撃を当てたとき、当てた敵の移動速度を{PowersBalance.StrafeShotSlowDuration}秒間{v}%下げる。重ならず、再び当てると効果時間が延びる。", $"Enemy movement speed -{v}% for {PowersBalance.StrafeShotSlowDuration}s when hit by your basic attack while moving (refreshes without stacking)"); break;
                case Power.RelayHand: body = Loc.T($"敵を倒したとき、最も近い味方旅人の通常記憶のうち残り時間が最長の1つの残りクールダウンが{v}%短くなる。同じ味方には{PowersBalance.RelayHandCooldown}秒に1回まで。", $"Ally's ordinary-memory remaining cooldown -{v}% on kill; affects the nearest allied traveler's ordinary memory with the longest remaining cooldown (once per {PowersBalance.RelayHandCooldown}s per ally)"); break;
                case Power.StardustCycle: body = Loc.T($"自分の光付与で敵の光が{PowersBalance.StardustCycleLightStacks}つに達したとき、自分の通常記憶のうち残り時間が最長の1つの残りクールダウンが{v}%短くなる。自分全体で{PowersBalance.StardustCycleGlobalCooldown}秒に1回、同じ敵には{PowersBalance.StardustCyclePerEnemyCooldown}秒に1回まで。", $"Ordinary-memory remaining cooldown -{v}% when your Light application brings an enemy to {PowersBalance.StardustCycleLightStacks} Light stacks; affects your ordinary memory with the longest remaining cooldown (once per {PowersBalance.StardustCycleGlobalCooldown}s overall; once per {PowersBalance.StardustCyclePerEnemyCooldown}s per enemy)"); break;
                case Power.UmbralHeritage: body = Loc.T($"闇が{PowersBalance.UmbralHeritageMinStacks}つ以上ある敵を倒したとき、倒した敵の周囲{PowersBalance.UmbralHeritageRange}m以内の別の敵最大{PowersBalance.UmbralHeritageMaxTargets}体に、それぞれ個別の判定で{v}%の確率で闇を1つ付ける。闇は5つまで。", $"Dark application chance +{v} percentage points per target when killing an enemy with at least {PowersBalance.UmbralHeritageMinStacks} Dark stacks; independently rolls to apply 1 Dark stack to up to {PowersBalance.UmbralHeritageMaxTargets} other enemies within {PowersBalance.UmbralHeritageRange}m of the killed enemy (Dark capped at 5 stacks)"); break;
                case Power.BrittleIce: body = Loc.T($"冷気を受けている敵に会心でダメージを与えたとき、攻撃力・魔力の高い方の{v}%の追加ダメージを与える。通常攻撃・記憶のどちらも対象になる。", $"Bonus damage +{v}% of the higher of your attack damage or ability power on a critical hit against a chilled enemy; applies to basic attacks and memories"); break;
                case Power.ElementalHarvest: body = Loc.T($"{PowersBalance.ElementalHarvestMinTypes}種類以上の属性がある敵を倒したとき、倒した敵の周囲{PowersBalance.ElementalHarvestRange}m以内の敵それぞれに、属性の種類数1つにつき攻撃力・魔力の高い方の{v}%の範囲ダメージを与える。最大{PowersBalance.ElementalHarvestMaxTypes}種類分まで数え、連鎖しない。", $"Area damage +{v}% of the higher of your attack damage or ability power per element type on the killed enemy, dealt to each enemy within {PowersBalance.ElementalHarvestRange}m; requires at least {PowersBalance.ElementalHarvestMinTypes} element types (counts up to {PowersBalance.ElementalHarvestMaxTypes} types; does not chain)"); break;
                case Power.PrismShift: body = Loc.T($"直前とは別の属性を敵に付けたとき、最大HPの{v}%の障壁を{PowersBalance.PrismShiftDuration}秒間得る。{PowersBalance.PrismShiftCooldown}秒に1回まで。重ならず、大きい方だけを適用する。", $"Shield +{v}% of your maximum health for {PowersBalance.PrismShiftDuration}s when applying a different element from your previous application (once per {PowersBalance.PrismShiftCooldown}s; highest shield only, without stacking)"); break;
                case Power.PackFeast: body = Loc.T($"自分の召喚獣が敵を倒したとき、最大HPの{v}%の障壁を{PowersBalance.PackFeastDuration}秒間得る。重ならず、大きい方だけを適用する。", $"Shield +{v}% of your maximum health for {PowersBalance.PackFeastDuration}s when your summon kills an enemy (highest shield only, without stacking)"); break;
                case Power.VanguardsOath: body = Loc.T($"敵までの最短距離がパーティで最も短い旅人である間、得る障壁量が{v}%増える。ソロの場合は、敵がいる間に{v / 2f:0.#}%増える。敵がいないと無効。", $"Shield amount received +{v}% (+{v / 2f:0.#}% when solo) while your distance to your nearest enemy is the shortest in the party; active in solo while enemies exist, inactive without enemies"); break;
                case Power.DeathBloom: body = Loc.T($"自分の召喚獣が敵に倒されたとき、その召喚獣の周囲{PowersBalance.DeathBloomRange}m以内の敵それぞれに、攻撃力・魔力の高い方の{v}%の範囲ダメージを与える。時間切れでは発動せず、連鎖しない。", $"Area damage +{v}% of the higher of your attack damage or ability power per enemy within {PowersBalance.DeathBloomRange}m of your summon when an enemy kills it (expiry does not trigger this; does not chain)"); break;
                case Power.Medley: body = Loc.T($"使った通常記憶の種類1つにつき、攻撃力・魔力が{v}%上がる。最大{PowersBalance.MedleyMaxTypes}種類分まで数える。新しい種類を使うと、全体の効果時間が{PowersBalance.MedleyDuration}秒に延びる。同じ記憶を再使用しても、種類数も期限も変わらない。", $"Attack damage and ability power +{v}% per distinct ordinary memory used, up to {PowersBalance.MedleyMaxTypes} types; a new type refreshes the whole duration to {PowersBalance.MedleyDuration}s. Reusing the same memory changes neither the count nor the expiry"); break;
                case Power.Spellsweep: body = Loc.T($"通常記憶を使った後{PowersBalance.SpellsweepWindow}秒以内の次の通常攻撃が命中したとき、当てた敵の周囲{PowersBalance.BasicSplashRadius}m以内の別の敵それぞれに、通常攻撃の与ダメージの{v}%の範囲追加ダメージを与える。1回で消費し、記憶を再使用すると効果時間が延びる。", $"Area bonus damage +{v}% of your basic attack's damage per other enemy within {PowersBalance.BasicSplashRadius}m of its target, on the next basic hit within {PowersBalance.SpellsweepWindow}s of using an ordinary memory (consumed once; memory use refreshes the window)"); break;
                case Power.BareHandedPride: body = Loc.T($"通常記憶がすべてクールダウン中で使える回数がない間、通常攻撃が命中したとき、通常記憶すべての残りクールダウンが{v / 10f:0.#}秒短くなる。{PowersBalance.BareHandedPrideCooldown}秒に1回まで。", $"All ordinary memories' remaining cooldowns -{v / 10f:0.#}s on a basic attack hit while every ordinary memory is cooling down with no available charges (once per {PowersBalance.BareHandedPrideCooldown}s)"); break;
                case Power.OpeningSalvo: body = Loc.T($"通常記憶がすべて使える状態から最初の通常記憶を使ったとき、その記憶の残りクールダウンが{v}%短くなる。すべて使える状態に戻ると、再び準備される。", $"Used ordinary memory's remaining cooldown -{v}% on the first ordinary-memory use while all ordinary memories are ready; rearms when all are ready again"); break;
                case Power.PileOn: body = Loc.T($"ほかの記憶を挟まず同じ記憶を{PowersBalance.PileOnUses}回続けて使うたび、その記憶の残りクールダウンが{v}%短くなる。移動の記憶を使うと、回数には数えずに連続使用がリセットされる。", $"Used memory's remaining cooldown -{v}% every {PowersBalance.PileOnUses} consecutive uses of the same memory without another memory in between (Movement memory use does not count and resets the sequence)"); break;
                case Power.AceInHand: body = Loc.T($"装備中の奥義の記憶が使える間、通常記憶の与ダメージが{v}%増える。星座で奥義になった記憶も対象で、奥義がなければ無効。", $"Ordinary-memory damage +{v}% while your equipped Ultimate memory is ready (includes memories converted to Ultimate by constellations; inactive without an Ultimate)"); break;
                case Power.CrystalCircuit: body = Loc.T($"自分のエッセンスが発動したとき、通常記憶すべての残りクールダウンが{v}%短くなる。", $"All ordinary memories' remaining cooldowns -{v}% when your Essence activates"); break;
                case Power.ShardBoon: body = Loc.T($"黄金の欠片かドリームダストの欠片を壊したとき、自分の最大HPの{v / 10f:0.#}%を回復する。{PowersBalance.ShardBoonCooldown}秒に1回まで。", $"Health restored +{v / 10f:0.#}% of your maximum health when breaking a Gold or Dreamdust fragment (once per {PowersBalance.ShardBoonCooldown}s)"); break;
                case Power.Lifeline: body = Loc.T($"自分に最大HPを超える回復が出た後{PowersBalance.LifelineDuration}秒間、自分の召喚獣の与ダメージが{v}%増える。重ならず、再発動すると効果時間が延びる。", $"Your summons' damage +{v}% for {PowersBalance.LifelineDuration}s after you receive overhealing (refreshes without stacking)"); break;
                case Power.DreamOmen: body = Loc.T($"夢の出来事が始まったとき、通常記憶すべての残りクールダウンが{v}%短くなり、最大HPの{v / 2f:0.#}%の障壁を{PowersBalance.DreamOmenShieldDuration}秒間得る。", $"All ordinary memories' remaining cooldowns -{v}% and shield +{v / 2f:0.#}% of your maximum health for {PowersBalance.DreamOmenShieldDuration}s when a dream event begins"); break;
                case Power.RearguardsWay: body = Loc.T($"敵までの最短距離がパーティで最も長い旅人である間、通常記憶の与ダメージが{v}%増える。ソロや敵がいない間は無効。", $"Ordinary-memory damage +{v}% while your distance to your nearest enemy is the longest in the party (inactive when solo or without enemies)"); break;
                case Power.TollOfGrudge: body = Loc.T($"実際に失ったHPの合計が自分の最大HPの{PowersBalance.TollOfGrudgeThreshold:0%}に達するたび、自分の周囲{PowersBalance.TollOfGrudgeRange}m以内の敵それぞれに、自分の最大HPの{v}%の範囲魔法ダメージを与える。障壁が吸収した分は数えず、世界を移ると蓄積を0に戻す。", $"Area magic damage +{v}% of your maximum health per enemy within {PowersBalance.TollOfGrudgeRange}m of you whenever actual health lost totals {PowersBalance.TollOfGrudgeThreshold:0%} of your maximum health (shield absorption does not count; progress resets between worlds)"); break;
                case Power.UnbowedMind: body = Loc.T($"敵から実際にスタンを受けたとき、最大HPの{v}%の障壁と、{PowersBalance.UnbowedMindDuration}秒間の行動妨害無効を得る。{PowersBalance.UnbowedMindCooldown}秒に1回まで。障壁が壊れても、行動妨害無効は続く。", $"Shield +{v}% of your maximum health for {PowersBalance.UnbowedMindDuration}s and crowd-control immunity +{PowersBalance.UnbowedMindDuration}s when actually stunned by an enemy (once per {PowersBalance.UnbowedMindCooldown}s; immunity persists if the shield breaks)"); break;
                case Power.PilingLuck: body = Loc.T($"通常攻撃が会心せずに命中するたび、次の通常攻撃の会心率が{v}%上がる。最大{PowersBalance.PilingLuckMaxStacks}回分まで蓄積し、会心すると0に戻る。確定会心の攻撃では蓄積もリセットもしない。", $"Next basic attack critical chance +{v} percentage points per non-critical basic hit; accumulates up to {PowersBalance.PilingLuckMaxStacks} times and resets on a critical hit (guaranteed critical hits neither add nor reset it)"); break;
                case Power.WeakPointWound: body = Loc.T($"同じ敵に会心を{PowersBalance.WeakPointWoundCrits}回当てるたび、攻撃力・魔力の高い方の{v}%の追加ダメージを与える。通常攻撃・記憶のどちらも対象になる。敵ごとに数え、会心の間隔が{PowersBalance.WeakPointWoundWindow}秒以上空くと、数え直す。", $"Bonus damage +{v}% of the higher of your attack damage or ability power every {PowersBalance.WeakPointWoundCrits} critical hits on the same enemy, from basic attacks or memories (tracked per enemy; resets after at least {PowersBalance.WeakPointWoundWindow}s between critical hits)"); break;
                case Power.CritSplash: body = Loc.T($"通常攻撃が会心で命中したとき、当てた敵の周囲{PowersBalance.BasicSplashRadius}m以内の別の敵それぞれに、通常攻撃の与ダメージの{v}%の範囲追加ダメージを与える。追加分は会心せず、連鎖しない。", $"Area bonus damage +{v}% of your basic attack's damage per other enemy within {PowersBalance.BasicSplashRadius}m of its target on a critical basic hit (splash cannot critically hit or chain)"); break;
                case Power.ReturningBlade: body = Loc.T($"自分の記憶のダメージで敵を倒したとき、その記憶の残りクールダウンが{v}%短くなる。", $"Killing memory's remaining cooldown -{v}% when your memory's damage kills an enemy; affects that same memory"); break;
                case Power.ReadyGuard: body = Loc.T($"敵とのダメージのやり取りが{PowersBalance.ReadyGuardWindow}秒間なかった後、最初にダメージを与えるか受けたとき、最大HPの{v}%の障壁を{PowersBalance.ReadyGuardDuration}秒間得る。障壁による吸収もダメージのやり取りに数える。", $"Shield +{v}% of your maximum health for {PowersBalance.ReadyGuardDuration}s on the first hostile damage dealt or received after {PowersBalance.ReadyGuardWindow}s without either (shield absorption also counts as combat damage)"); break;
                case Power.Apothecary: body = Loc.T($"HPポーションを使ったとき、通常記憶すべての残りクールダウンが{v}%短くなり、自分の周囲{PowersBalance.ApothecaryAllyRange}m以内の味方旅人それぞれを、ポーション回復量の{PowersBalance.ApothecaryAllyHealShare * 100:0}%回復する。", $"All ordinary memories' remaining cooldowns -{v}% and allied health restored +{PowersBalance.ApothecaryAllyHealShare * 100:0}% of the potion's healing per allied traveler within {PowersBalance.ApothecaryAllyRange}m of you when using a health potion"); break;
                case Power.SpilloverStrike: body = Loc.T($"敵を倒したとき、倒した敵の周囲{PowersBalance.SpilloverStrikeRange}m以内の別の敵1体に、撃破した一撃の過剰ダメージの{v}%の追加ダメージを与える。連鎖しない。", $"Bonus damage +{v}% of the killing hit's excess damage to one other enemy within {PowersBalance.SpilloverStrikeRange}m of the killed enemy (does not chain)"); break;
                default: return "-";
            }
            if (p == Power.SharedWard || p == Power.TriumphSong || p == Power.Apothecary)
                body += CoopDistance.Description;
            // Equipment bases share a cap before enhancement, awakening and Overcharge; stars add on top.
            // Use the runtime's equipment ceiling before converting display units (15 * 2.5 => 37).
            int capValue = Content.PowerCap(p);
            int ceiling = PowerRuntime.EquipmentCeiling(p);
            string cap = p == Power.BareHandedPride
                ? Loc.T($"装備の元の値による1回の短縮量は合計で最大{capValue / 10f:0.#}秒で、強化・覚醒・過剰で最大{ceiling / 10f:0.#}秒まで伸びる。星の分はその上に加わる。", $" (reduction per trigger from equipment base values capped at {capValue / 10f:0.#}s, raised up to {ceiling / 10f:0.#}s by enhancement, awakening and Overcharge). Star values add on top of this equipment cap")
                : p == Power.ShardBoon
                    ? Loc.T($"装備の元の値による1回の回復量は合計で最大HPの{capValue / 10f:0.#}%で、強化・覚醒・過剰で最大{ceiling / 10f:0.#}%まで伸びる。星の分はその上に加わる。", $" (healing per trigger from equipment base values capped at {capValue / 10f:0.#}% of your maximum health, raised up to {ceiling / 10f:0.#}% by enhancement, awakening and Overcharge). Star values add on top of this equipment cap")
                    : Loc.T($"装備の元の値は合計で最大{capValue}%で、強化・覚醒・過剰で最大{ceiling}%まで伸びる。星の分はその上に加わる。1回の効果量に対する上限で、敵の数や発動回数は合算しない。", $" (equipment base values combined are capped at {capValue}% per activation, raised up to {ceiling}% by enhancement, awakening and Overcharge; targets and triggers are not summed). Star values add on top of this equipment cap");
            if (p == Power.ElementalHarvest || p == Power.PilingLuck || p == Power.Medley)
                cap += p == Power.ElementalHarvest
                    ? Loc.T("この上限は属性1種類あたりの割合に適用される。", ". This cap applies to the percentage per element type")
                    : p == Power.PilingLuck
                        ? Loc.T("この上限は蓄積1回あたりの会心率増加に適用される。", ". This cap applies to the critical-chance increase per stack")
                        : Loc.T("この上限は記憶1種類あたりの増加に適用される。", ". This cap applies to the bonus per memory type");
            if (p == Power.CoStar || p == Power.DreamOmen || p == Power.Apothecary)
                cap += Loc.T("この上限はクールダウン短縮の割合に適用される。", ". This cap applies to the cooldown-reduction percentage");
            if (p == Power.DreamOmen)
                cap += Loc.T("障壁の割合上限はその半分である。", " (the shield percentage cap is half of this)");
            if (p == Power.GleamingWard || p == Power.Medley)
                cap += Loc.T($"条件つきの攻撃力・魔力の増加は、覚醒前の数値で全効果の合計が最大{PowerRuntime.ConditionalPowerCap}%まで。",
                    $". Conditional attack damage and ability power bonuses together are capped at +{PowerRuntime.ConditionalPowerCap}%, counted before awakening");
            return body + cap;
        }
    }
}
