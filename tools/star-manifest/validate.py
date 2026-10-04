"""v1.31 星マニフェストの検証。使い方・正準形は README.md。失敗があれば終了コード1。

正準形（README の「正準形」節）を厳密に検査する。主な検査:
- キー集合が正準形と完全一致（旧欄 effectId / paramTarget / label / pair などは不可）
- 参照整合: anchor / edges / requires / requiresAny / target.star / gimmick.replaces / 橋の condition の ID が
  同じファイル・outer.json・既存（legacy）ID のどれかに存在する
- 移行行の ID は既存 ID（src/SodRpg.Core/Game の HeroSigils / HeroStarRoutes / StarClusters から導出）
"""
import glob
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.normpath(os.path.join(HERE, '..', '..', 'src', 'SodRpg.Core', 'Game'))

# New private IDs（docs/specs/v1.31-design-review.md の表）。外縁は共有160。
EXPECTED = {
    'vesper': 649, 'cetus': 649, 'lacerta': 645, 'husk': 656, 'mist': 656,
    'yubar': 644, 'aurena': 510, 'nachia': 502, 'bismuth': 510, 'outer': 169,
}
# v1.32 B: RunGrowth is the important star that grants a run-long stacking mechanism; RunGrowthMod is a small star/option that modifies it.
GROWTH_KINDS = {'RunGrowth', 'RunGrowthMod'}
KINDS = {'MemoryDamage', 'MemoryHaste', 'GimmickBoost', 'GimmickParam', 'Notable', 'Choice', 'Stat', 'Keystone'} | GROWTH_KINDS
OPTION_KINDS = KINDS - {'Choice', 'Keystone', 'RunGrowth'}
GROWTH_TRIGGERS = {'DamageTakenMaxHpPct': 100, 'ShieldAbsorbedMaxHpPct': 100, 'ParrySuccess': 1000, 'CritBasicAttackKill': 1000}
GROWTH_KEYS = ['trigger', 'threshold', 'cap', 'effects']
GROWTH_MOD_KEYS = ['target', 'capBonus', 'effectPct', 'doubleGain']
GROWTH_UNSUPPORTED_STATS = {'EssenceSlotIdentity', 'EssenceSlotMovement', 'SacrificeReduction', 'FourthAttackShift'}
GROWTH_MAX_CAP, GROWTH_MAX_EFFECTS, GROWTH_MAX_MILLI = 500, 4, 1000000
GROWTH_MAX_CAP_BONUS, GROWTH_MAX_EFFECT_PCT = 200, 500
REGIONS = {'memory', 'bridge', 'outer', 'keystone'}
PARAMS = {None, 'Duration', 'Radius', 'ExtraTargets', 'Chance', 'WindowDuration', 'MarkDuration'}
GATE_PARAMS = {'WindowDuration', 'MarkDuration'}
TRIGGERS = {'OnUse', 'OnHit', 'OnKill', 'OnCrit', 'OnBasicAttack'}
SHAPES = {None, 'fan', 'ring', 'chain'}
MECH = re.compile(r'^C(0[1-9]|1[0-5])$')

STAR_KEYS = ['id', 'region', 'cluster', 'shape', 'anchor', 'edges', 'requires', 'requiresAny', 'kind', 'memory',
             'value', 'param', 'receiver', 'target', 'gimmick', 'power', 'stat', 'options', 'keystone',
             'mechanisms', 'maxRank', 'rankCost', 'nameJa', 'nameEn', 'notes']
MIG_KEYS = ['id', 'region', 'kind', 'memory', 'value', 'param', 'receiver', 'target', 'gimmick', 'power', 'stat',
            'options', 'keystone', 'requires', 'requiresAny', 'maxRank', 'mechanisms', 'nameJa', 'nameEn', 'notes']
OPT_KEYS = ['kind', 'memory', 'value', 'param', 'receiver', 'target', 'gimmick', 'power', 'stat', 'nameJa', 'nameEn']
# RunGrowth rows carry one extra optional key `growth` right after `power` (present exactly for kinds RunGrowth / RunGrowthMod).
STAR_KEYS_G = STAR_KEYS[:STAR_KEYS.index('power') + 1] + ['growth'] + STAR_KEYS[STAR_KEYS.index('power') + 1:]
OPT_KEYS_G = OPT_KEYS[:OPT_KEYS.index('power') + 1] + ['growth'] + OPT_KEYS[OPT_KEYS.index('power') + 1:]


def stat_names():
    src = open(os.path.join(GAME, 'Ids.cs'), encoding='utf-8').read()
    m = re.search(r'enum Stat\s*\{(.*?)\n\s*\}', src, re.S)
    return set(re.findall(r'^\s*(\w+)\s*=\s*\d+', m.group(1), re.M)) if m else set()


def check_growth(o, ctx, kind, hero, refs, errors, S):
    """kind RunGrowth / RunGrowthMod の `growth` 欄。他の効果欄は null でなければならない。"""
    g = o.get('growth')
    for k in ('memory', 'value', 'param', 'receiver', 'target', 'gimmick', 'power', 'stat', 'options', 'keystone'):
        if k in o and o[k] is not None:
            errors.append(f'{ctx}: {kind} keeps {k} null (the payload is in growth)')
    if kind == 'RunGrowth':
        if not isinstance(g, dict) or list(g) != GROWTH_KEYS:
            errors.append(f'{ctx}: growth keys must be exactly {GROWTH_KEYS} in order')
            return
        trig = g['trigger']
        if trig not in GROWTH_TRIGGERS:
            errors.append(f'{ctx}: bad growth.trigger {trig!r} (one of {sorted(GROWTH_TRIGGERS)})')
        elif not (isinstance(g['threshold'], int) and not isinstance(g['threshold'], bool) and 1 <= g['threshold'] <= GROWTH_TRIGGERS[trig]):
            errors.append(f'{ctx}: growth.threshold must be an int 1..{GROWTH_TRIGGERS[trig]} for {trig} (percent of max HP for the HP triggers, event count otherwise)')
        if not (isinstance(g['cap'], int) and not isinstance(g['cap'], bool) and 1 <= g['cap'] <= GROWTH_MAX_CAP):
            errors.append(f'{ctx}: growth.cap must be an int 1..{GROWTH_MAX_CAP}')
        effs = g['effects']
        if not (isinstance(effs, list) and 1 <= len(effs) <= GROWTH_MAX_EFFECTS):
            errors.append(f'{ctx}: growth.effects must list 1..{GROWTH_MAX_EFFECTS} stat effects')
            return
        names = stat_names()
        seen = set()
        for i, e in enumerate(effs):
            if not (isinstance(e, dict) and list(e) == ['stat', 'amount']):
                errors.append(f'{ctx}: growth.effects[{i}] keys must be exactly [stat, amount]')
                continue
            if e['stat'] not in names or e['stat'] in GROWTH_UNSUPPORTED_STATS or e['stat'] in seen:
                errors.append(f'{ctx}: growth.effects[{i}].stat {e["stat"]!r} is unknown, unsupported or duplicated')
            seen.add(e['stat'])
            a = e['amount']
            if not (isinstance(a, (int, float)) and not isinstance(a, bool) and a > 0
                    and abs(a * 1000 - round(a * 1000)) < 1e-9 and round(a * 1000) <= GROWTH_MAX_MILLI):
                errors.append(f'{ctx}: growth.effects[{i}].amount must be a positive number in thousandths (0.5, 0.3, 1, ...)')
    else:
        if not isinstance(g, dict) or list(g) != GROWTH_MOD_KEYS:
            errors.append(f'{ctx}: growth keys must be exactly {GROWTH_MOD_KEYS} in order')
            return
        t = g['target']
        if t is not None:
            refs.append((ctx, 'growth.target', t))
            ts = S.get(t)
            if ts is not None and ts.get('kind') != 'RunGrowth':
                errors.append(f'{ctx}: growth.target {t} is not a RunGrowth star')
        for k, hi in (('capBonus', GROWTH_MAX_CAP_BONUS), ('effectPct', GROWTH_MAX_EFFECT_PCT)):
            if not (isinstance(g[k], int) and not isinstance(g[k], bool) and 0 <= g[k] <= hi):
                errors.append(f'{ctx}: growth.{k} must be an int 0..{hi}')
        if not isinstance(g['doubleGain'], bool):
            errors.append(f'{ctx}: growth.doubleGain must be a bool')
        if isinstance(g['capBonus'], int) and isinstance(g['effectPct'], int) and g['doubleGain'] is False and g['capBonus'] == 0 and g['effectPct'] == 0:
            errors.append(f'{ctx}: a RunGrowth modifier must change something')
G_REQUIRED = ['trigger', 'effect', 'value', 'arg', 'cooldown', 'target']
G_OPTIONAL = ['condition', 'once', 'everyN', 'valuesByRank', 'triggerByIdentity', 'replaces', 'basis', 'pool', 'strike', 'tuning']
# IdentityStrike（アイデンティティ記憶そのものが与える追加ダメージ）。gimmick.strike の正準形。
STRIKE_MODES = {'AfterDisplacement', 'EveryNthBasicAttack', 'DashBonusAsMemory'}
STRIKE_ELEMENTS = {'None', 'Fire', 'Cold', 'Light', 'Dark'}
STRIKE_SHAPES = {'ForwardLine', 'ForwardArc'}
# MemoryTuning（名前付きの本体記憶の挙動の静的な変更）。gimmick.tuning の正準形。value は百分率（40 = 40%）。
TUNINGS = {'KillingFlowKeepSpeed': ('St_D_TheKillingFlow', 0.01, 90), 'KillingFlowOnHitHealScale': ('St_D_TheKillingFlow', 100, 400),
           'StanceSwordQiAttackBasis': ('St_R_AnnihilationStance', 100, 100)}
STRIKE_KEYS = {'mode', 'element', 'shape', 'range', 'width', 'maxTargets', 'windowSeconds', 'bonusSpeed'}
KS_KEYS = ['upside', 'downside', 'upsideSpec', 'downsideSpec']
SPEC_KEYS = {'memory', 'memories', 'effect', 'field', 'pct', 'from', 'to', 'delta', 'max', 'receiver', 'scope', 'gimmick', 'condition'}

# 記憶ID または スロット選択子（@ID/@Q/@R/@M、@Q(St_A|St_B) の制約付き、| で連結可）
MEM = r'St_[A-Za-z0-9_]+'
SELITEM = r'@(?:ID|Q|R|M)(?:\(' + MEM + r'(?:\|' + MEM + r')*\))?'
MEMORY_RE = re.compile(r'^(?:' + MEM + r'|' + SELITEM + r'(?:\|' + SELITEM + r')*)$')
CONDITION_RE = re.compile(r'^Bridge(Success|Mark|Window):(h\.([a-z]+)\.ring\.[a-z]+)$')
RINGS = {'force', 'insight', 'vessel', 'armor', 'recall', 'rhythm', 'resolve', 'renewal'}


# ---------------------------------------------------------------- 既存（legacy）IDの導出
def legacy_data():
    """(legacy ID の集合, 旧ルート星 ID -> (種別, 引数, 記憶), GimmickEffect 名の集合)。"""
    def rd(p):
        return open(os.path.join(GAME, p), encoding='utf-8').read()
    ids = set()
    for m in re.finditer(r'(?:Node|DeepNode|DeepPower|DeepCostly|Key|PowerNode)\("Hero_\w+",\s*"([^"]+)"', rd('HeroSigils.cs')):
        ids.add('h.' + m.group(1))
    routes = {}
    heroes = set()
    rt = rd('HeroStarRoutes.cs')
    cur, order = None, 0
    for line in rt.split('\n'):
        m = re.search(r'new Route\(nodes,\s*"(\w+)",\s*"([^"]+)",\s*"([^"]+)"', line)
        if m:
            h, slug, mem = m.group(1).lower(), m.group(2), m.group(3)
            heroes.add(h)
            for i in range(1, 8):
                ids.add(f'h.{h}.route.{slug}.{i}')
            if mem.startswith(('St_D_', 'St_M_')):
                ids.add(f'h.{h}.route.{slug}.slot')
            cur, order = (h, slug, mem), 0
            continue
        m = re.search(r'\br\.(S|P|L|G|CapG)\((.*)\);', line)
        if m and cur:
            order += 1
            routes[f'h.{cur[0]}.route.{cur[1]}.{order}'] = (m.group(1), m.group(2), cur[2])
    for h in heroes:
        for r in RINGS:
            ids.add(f'h.{h}.ring.{r}')
    for f in glob.glob(os.path.join(GAME, 'StarClusters', '*.cs')):
        txt = open(f, encoding='utf-8').read()
        for m in re.finditer(r'Id = "(h\.[\w.-]+)"[^\n]*\n(.*?)(?=\n {12}new StarClusterDef|\n {8}\}\);)', txt, re.S):
            n = len(re.findall(r'^ {20}new ClusterStarDef', m.group(2), re.M))
            for i in range(1, n + 1):
                ids.add(f'{m.group(1)}.{i}')
    for m in re.finditer(r'TalentDef\("(h\.[\w.-]+)"', rd('StarClusters.cs')):
        ids.add(m.group(1))
    gt = rd('Gimmicks.cs')
    i = gt.index('enum GimmickEffect')
    effects = set(re.findall(r'^\s*(\w+)\s*=\s*\d+', gt[i:gt.index('}', i)], re.M))
    return ids, routes, effects


# ---------------------------------------------------------------- 共通部品の検査
def memory_ok(v):
    return v is None or (isinstance(v, str) and MEMORY_RE.match(v) is not None)


def check_gimmick(g, ctx, legacy, hero, refs, errors, memory=None):
    if not isinstance(g, dict):
        errors.append(f'{ctx}: gimmick must be object')
        return
    keys = set(g)
    missing = [k for k in G_REQUIRED if k not in keys]
    extra = keys - set(G_REQUIRED) - set(G_OPTIONAL)
    if missing:
        errors.append(f'{ctx}: gimmick missing {missing}')
    if extra:
        errors.append(f'{ctx}: gimmick has non-canonical fields {sorted(extra)}')
    if g.get('trigger') not in TRIGGERS:
        errors.append(f'{ctx}: bad trigger {g.get("trigger")}')
    if not isinstance(g.get('effect'), str) or not g.get('effect'):
        errors.append(f'{ctx}: gimmick.effect missing')
    for k in ('value', 'arg', 'cooldown'):
        if not isinstance(g.get(k), (int, float)) or isinstance(g.get(k), bool):
            errors.append(f'{ctx}: gimmick.{k} must be number')
    if not memory_ok(g.get('target')):
        errors.append(f'{ctx}: bad gimmick.target {g.get("target")!r}')
    c = g.get('condition')
    if 'condition' in g:
        m = CONDITION_RE.match(c) if isinstance(c, str) else None
        if not m:
            errors.append(f'{ctx}: bad condition {c!r} (BridgeSuccess|BridgeMark|BridgeWindow:<ring id>)')
        else:
            if m.group(3) != hero:
                errors.append(f'{ctx}: condition ring of another hero {c}')
            refs.append((ctx, 'condition', m.group(2)))
    if 'once' in g and not isinstance(g['once'], bool):
        errors.append(f'{ctx}: once must be bool')
    if 'everyN' in g and not (isinstance(g['everyN'], int) and g['everyN'] >= 1):
        errors.append(f'{ctx}: everyN must be int >= 1')
    if 'valuesByRank' in g and not (isinstance(g['valuesByRank'], list) and g['valuesByRank']
                                    and all(isinstance(x, (int, float)) for x in g['valuesByRank'])):
        errors.append(f'{ctx}: valuesByRank must be non-empty number list')
    if 'triggerByIdentity' in g:
        t = g['triggerByIdentity']
        if not (isinstance(t, dict) and t and all(re.match(MEM + '$', k) and v in TRIGGERS for k, v in t.items())):
            errors.append(f'{ctx}: bad triggerByIdentity')
    if 'replaces' in g:
        refs.append((ctx, 'gimmick.replaces', g['replaces']))
    for k in ('basis', 'pool'):
        if k in g and not (isinstance(g[k], str) and g[k]):
            errors.append(f'{ctx}: {k} must be string')
    check_strike(g, ctx, errors)
    check_tuning(g, ctx, errors, memory)


def check_tuning(g, ctx, errors, memory):
    """effect == MemoryTuning と gimmick.tuning は必ず対。トリガーは OnUse、効果量は gimmick.value（百分率）。"""
    is_tuning = g.get('effect') == 'MemoryTuning'
    if is_tuning != ('tuning' in g):
        errors.append(f'{ctx}: gimmick.effect MemoryTuning and gimmick.tuning must appear together')
        return
    if not is_tuning:
        return
    t = g['tuning']
    if not (isinstance(t, dict) and set(t) == {'kind'} and t['kind'] in TUNINGS):
        errors.append(f'{ctx}: tuning must be {{"kind": one of {sorted(TUNINGS)}}}')
        return
    mem, lo, hi = TUNINGS[t['kind']]
    if memory is not None and memory != mem:
        errors.append(f'{ctx}: tuning {t["kind"]} belongs to {mem}, not {memory}')
    if g.get('trigger') != 'OnUse' or g.get('cooldown') != 0 or g.get('arg') != 0 or g.get('target') is not None:
        errors.append(f'{ctx}: MemoryTuning needs trigger OnUse (a static modification), arg 0, cooldown 0, target null')
    for k in ('condition', 'once', 'everyN', 'valuesByRank', 'triggerByIdentity', 'replaces', 'basis', 'pool', 'strike'):
        if k in g:
            errors.append(f'{ctx}: MemoryTuning does not take gimmick.{k}')
    v = g.get('value')
    if not (isinstance(v, (int, float)) and not isinstance(v, bool) and lo <= v <= hi):
        errors.append(f'{ctx}: MemoryTuning {t["kind"]} value is a percentage in {lo}..{hi}')
    elif t['kind'] == 'StanceSwordQiAttackBasis' and v != 100:
        errors.append(f'{ctx}: StanceSwordQiAttackBasis takes no amount (value 100)')


def check_strike(g, ctx, errors, memory=None):
    """effect == IdentityStrike と gimmick.strike は必ず対。モードごとの必須・禁止欄を厳密に検査する。"""
    is_strike = g.get('effect') == 'IdentityStrike'
    if is_strike != ('strike' in g):
        errors.append(f'{ctx}: gimmick.effect IdentityStrike and gimmick.strike must appear together')
        return
    if not is_strike:
        return
    st = g['strike']
    if not isinstance(st, dict) or not set(st) <= STRIKE_KEYS or st.get('mode') not in STRIKE_MODES:
        errors.append(f'{ctx}: strike must be an object with mode in {sorted(STRIKE_MODES)} and keys within {sorted(STRIKE_KEYS)}')
        return
    mode = st['mode']
    num = lambda v: isinstance(v, (int, float)) and not isinstance(v, bool)
    if g.get('trigger') != 'OnHit' or g.get('cooldown') != 0 or g.get('arg') != 0 or g.get('target') is not None:
        errors.append(f'{ctx}: IdentityStrike needs trigger OnHit (own basic attack hit), arg 0, cooldown 0, target null')
    for k in ('condition', 'once', 'valuesByRank', 'triggerByIdentity', 'replaces', 'basis', 'pool'):
        if k in g:
            errors.append(f'{ctx}: IdentityStrike does not take gimmick.{k}')
    if mode == 'DashBonusAsMemory':
        if set(st) != {'mode'} or g.get('value') != 0 or 'everyN' in g:
            errors.append(f'{ctx}: DashBonusAsMemory carries only mode, value 0 and no everyN (it adds no damage)')
        return
    need = {'mode', 'element', 'shape', 'range', 'width'} | ({'windowSeconds'} if mode == 'AfterDisplacement' else set())
    if not need <= set(st):
        errors.append(f'{ctx}: strike {mode} needs {sorted(need - set(st))}')
        return
    if st['element'] not in STRIKE_ELEMENTS or st['shape'] not in STRIKE_SHAPES:
        errors.append(f'{ctx}: bad strike element/shape')
    if not (num(g.get('value')) and 0 < g['value'] <= 200):
        errors.append(f'{ctx}: IdentityStrike value is the attack damage % (0 < value <= 200)')
    if not (num(st['range']) and 1 <= st['range'] <= 15) or not (num(st['width']) and st['width'] > 0):
        errors.append(f'{ctx}: strike range must be 1..15 m and width > 0 (line width in m / arc angle in degrees)')
    elif st['shape'] == 'ForwardArc' and st['width'] > 360 or st['shape'] == 'ForwardLine' and st['width'] > 15:
        errors.append(f'{ctx}: strike width exceeds its shape limit (arc <= 360 degrees, line <= 15 m)')
    if 'maxTargets' in st and not (isinstance(st['maxTargets'], int) and 1 <= st['maxTargets'] <= 16):
        errors.append(f'{ctx}: strike maxTargets must be an int 1..16')
    if mode == 'AfterDisplacement':
        if 'everyN' in g or 'bonusSpeed' in st:
            errors.append(f'{ctx}: AfterDisplacement takes neither everyN nor bonusSpeed')
        if not (num(st['windowSeconds']) and 0.5 <= st['windowSeconds'] <= 10):
            errors.append(f'{ctx}: strike windowSeconds must be 0.5..10')
    else:
        if not (isinstance(g.get('everyN'), int) and 1 <= g['everyN'] <= 100) or 'windowSeconds' in st:
            errors.append(f'{ctx}: EveryNthBasicAttack needs gimmick.everyN 1..100 (1 = every basic attack) and no windowSeconds')
        if 'bonusSpeed' in st and not (num(st['bonusSpeed']) and 0 <= st['bonusSpeed'] <= 10):
            errors.append(f'{ctx}: strike bonusSpeed is the extra attack damage % per 1% bonus attack speed (0..10)')


def check_effect_obj(o, ctx, kind_rule, legacy, hero, refs, errors, S, effects):
    """star / option / migration row に共通の効果欄の検査。"""
    kind = o.get('kind')
    if kind in GROWTH_KINDS:
        if kind_rule == 'migration':
            errors.append(f'{ctx}: {kind} is not allowed in a migration row')
        check_growth(o, ctx, kind, hero, refs, errors, S)
        return
    if 'growth' in o:
        errors.append(f'{ctx}: growth only for RunGrowth / RunGrowthMod')
    mem, rec = o.get('memory'), o.get('receiver')
    if not memory_ok(mem):
        errors.append(f'{ctx}: bad memory {mem!r} (St_* or @ID/@Q/@R/@M[(..)])')
    if not memory_ok(rec):
        errors.append(f'{ctx}: bad receiver {rec!r}')
    if o.get('param') not in PARAMS:
        errors.append(f'{ctx}: bad param {o.get("param")}')
    if o.get('param') in GATE_PARAMS:
        # 橋の受付時間 / 印の持続時間。橋の星団の行で、対象が橋のringID（effect なし）のときだけ。Window/Mark の別は gen_cs.py が実際のペアで検査する。
        t = o.get('target') or {}
        if kind != 'GimmickParam' or kind_rule == 'migration' or not str(t.get('star') or '').startswith(f'h.{hero}.ring.') or t.get('effect'):
            errors.append(f'{ctx}: {o.get("param")} needs a bridge GimmickParam with target.star = h.{hero}.ring.* and no target.effect')
    g = o.get('gimmick')
    if g is not None:
        check_gimmick(g, ctx, legacy, hero, refs, errors, mem)
        gt = g.get('target') if isinstance(g, dict) else None
        if gt and rec != gt:
            errors.append(f'{ctx}: receiver {rec!r} must equal gimmick.target {gt!r}')
    if kind in ('MemoryDamage', 'MemoryHaste', 'GimmickBoost', 'GimmickParam', 'Stat') and not isinstance(o.get('value'), (int, float)) \
            and not (kind_rule == 'migration' and o.get('stat')):
        errors.append(f'{ctx}: {kind} needs numeric value')
    if kind in ('MemoryDamage', 'MemoryHaste', 'GimmickBoost', 'GimmickParam') and not (mem or rec):
        errors.append(f'{ctx}: {kind} needs memory or receiver')
    if kind == 'GimmickParam' and not o.get('param'):
        errors.append(f'{ctx}: GimmickParam needs param')
    if kind == 'Notable' and not (g or o.get('power')):
        errors.append(f'{ctx}: Notable needs gimmick or power')
    for k in ('power', 'stat'):
        v = o.get(k)
        if v is not None and not (isinstance(v, dict) and set(v) == {'name', 'perRank'} and isinstance(v['name'], str)
                                  and isinstance(v['perRank'], (int, float))):
            errors.append(f'{ctx}: bad {k} (expect {{name, perRank}})')
    if mem and mem.startswith('St_M_') and g is not None:
        errors.append(f'{ctx}: movement memory {mem} used as trigger source')
    # target {star, effect}
    t = o.get('target')
    if kind in ('GimmickBoost', 'GimmickParam'):
        if not (isinstance(t, dict) and set(t) == {'star', 'effect'}):
            errors.append(f'{ctx}: {kind} needs target {{star, effect}}')
        else:
            if t['star'] is not None:
                refs.append((ctx, 'target.star', t['star']))
                ts = S.get(t['star'])
                if ts is not None and t['effect'] is not None:
                    tg = ts.get('gimmick')
                    if tg and tg.get('effect') != t['effect']:
                        errors.append(f'{ctx}: target.effect {t["effect"]} != gimmick.effect {tg.get("effect")} of {t["star"]}')
            if t['effect'] is not None and t['effect'] not in effects:
                errors.append(f'{ctx}: unknown target.effect {t["effect"]}')
    elif t is not None:
        errors.append(f'{ctx}: target only for GimmickBoost/GimmickParam')


def check_options(o, ctx, kind_rule, legacy, hero, refs, errors, S, effects):
    opts = o.get('options')
    if o.get('kind') != 'Choice':
        if opts is not None:
            errors.append(f'{ctx}: options only for Choice')
        return
    if not (isinstance(opts, list) and len(opts) == 2):
        errors.append(f'{ctx}: Choice needs exactly 2 options')
        return
    for i, op in enumerate(opts):
        c2 = f'{ctx}#opt{i}'
        if not isinstance(op, dict) or list(op) not in (OPT_KEYS, OPT_KEYS_G) or (list(op) == OPT_KEYS_G) != (op.get('kind') in GROWTH_KINDS):
            errors.append(f'{c2}: option keys must be exactly {OPT_KEYS} in order (plus growth right after power for RunGrowthMod)')
            continue
        if op['kind'] not in OPTION_KINDS:
            errors.append(f'{c2}: bad option kind {op["kind"]}')
            continue
        check_effect_obj(op, c2, kind_rule, legacy, hero, refs, errors, S, effects)


def check_spec_list(lst, ctx, legacy, hero, refs, errors):
    if lst is None:
        return
    if not (isinstance(lst, list) and lst):
        errors.append(f'{ctx}: spec list must be null or non-empty list')
        return
    for i, e in enumerate(lst):
        c2 = f'{ctx}[{i}]'
        if not isinstance(e, dict) or not (set(e) <= SPEC_KEYS):
            errors.append(f'{c2}: bad spec keys')
            continue
        if not (isinstance(e.get('effect'), str) and isinstance(e.get('field'), str)):
            errors.append(f'{c2}: spec needs effect and field')
        forms = [k for k in ('pct', 'delta', 'to') if k in e]
        if e.get('field') == 'Grant':
            if forms or 'from' in e or 'max' in e:
                errors.append(f'{c2}: Grant spec carries only the gimmick (no pct/delta/to/from/max)')
        elif len(forms) != 1:
            errors.append(f'{c2}: spec needs exactly one of pct / delta / to (from only with to)')
        if 'from' in e and 'to' not in e:
            errors.append(f'{c2}: from without to')
        if 'max' in e and 'delta' not in e:
            errors.append(f'{c2}: max only with delta')
        for k in ('pct', 'delta', 'to', 'from', 'max'):
            if k in e and (not isinstance(e[k], (int, float)) or isinstance(e[k], bool)):
                errors.append(f'{c2}: {k} must be number')
        if 'memory' in e and 'memories' in e:
            errors.append(f'{c2}: memory and memories are exclusive')
        for k in ('memory', 'receiver'):
            if k in e and not memory_ok(e[k]):
                errors.append(f'{c2}: bad {k} {e[k]!r}')
        if 'memories' in e and not (isinstance(e['memories'], list) and e['memories'] and all(memory_ok(x) and x for x in e['memories'])):
            errors.append(f'{c2}: bad memories')
        if 'condition' in e and not (isinstance(e['condition'], str)
                                     and re.fullmatch(r'TargetHealthBelow:(100|[1-9]?\d)(\.\d+)?|OutsideRetaliationWindow', e['condition'])):
            errors.append(f'{c2}: bad condition {e.get("condition")!r} (TargetHealthBelow:<0-100> or OutsideRetaliationWindow)')
        if e.get('field') == 'Grant':
            if not isinstance(e.get('gimmick'), dict):
                errors.append(f'{c2}: Grant needs gimmick')
            else:
                check_gimmick(e['gimmick'], c2, legacy, hero, refs, errors)
        elif 'gimmick' in e:
            errors.append(f'{c2}: gimmick only with field Grant')


# ---------------------------------------------------------------- ファイル単位の検査
def load_ids(name):
    try:
        d = json.load(open(os.path.join(HERE, name + '.json'), encoding='utf-8'))
        return {s['id'] for s in d['stars'] if s.get('region') != 'migration'}
    except Exception:  # noqa: BLE001
        return set()


def check(name, legacy, routes, enum_effects, all_effects):
    errors = []
    path = os.path.join(HERE, name + '.json')
    if not os.path.exists(path):
        return [f'{name}: file missing'], 0
    try:
        data = json.load(open(path, encoding='utf-8'))
    except Exception as ex:  # noqa: BLE001
        return [f'{name}: invalid JSON: {ex}'], 0
    if set(data) != {'hero', 'source', 'stars'}:
        errors.append(f'{name}: top-level keys must be hero/source/stars (found {sorted(data)})')
    stars = data.get('stars', [])
    ids = [s.get('id') for s in stars]
    dup = {i for i in ids if ids.count(i) > 1}
    if dup:
        errors.append(f'{name}: duplicate ids {sorted(dup)[:10]}')
    new = [s for s in stars if s.get('region') != 'migration']
    mig = [s for s in stars if s.get('region') == 'migration']
    if len(new) != EXPECTED[name]:
        errors.append(f'{name}: {len(new)} new stars, expected {EXPECTED[name]}')
    S = {s['id']: s for s in new}
    outer_ids = load_ids('outer') if name != 'outer' else set()
    effects = enum_effects | all_effects
    provisional = 0
    refs = []  # (ctx, field, id)

    for s in new:
        sid = s.get('id', '?')
        if list(s) not in (STAR_KEYS, STAR_KEYS_G) or (list(s) == STAR_KEYS_G) != (s.get('kind') in GROWTH_KINDS):
            errors.append(f'{sid}: keys must be exactly {STAR_KEYS} (diff {sorted(set(s) ^ set(STAR_KEYS))}); RunGrowth / RunGrowthMod rows add `growth` right after `power`')
            continue
        region, kind = s['region'], s['kind']
        if region not in REGIONS:
            errors.append(f'{sid}: bad region {region}')
        if kind not in KINDS:
            errors.append(f'{sid}: bad kind {kind}')
            continue
        for f in ('nameJa', 'nameEn'):
            if not s.get(f):
                errors.append(f'{sid}: missing {f}')
            elif re.search(r'equipped[QRM]\(|[QRID]\*', s[f]):
                errors.append(f'{sid}: old selector notation in {f}')
        if s['shape'] not in SHAPES:
            errors.append(f'{sid}: bad shape {s["shape"]}')
        if not isinstance(s['maxRank'], int) or not isinstance(s['rankCost'], int):
            errors.append(f'{sid}: maxRank/rankCost must be int')
        for m in s['mechanisms']:
            if not MECH.match(m):
                errors.append(f'{sid}: bad mechanism {m}')
        if kind == 'Stat' and region != 'outer':
            errors.append(f'{sid}: Stat only allowed in outer')
        if kind == 'Stat' and not s['stat']:
            errors.append(f'{sid}: Stat needs stat')
        if kind not in ('Choice',):
            check_effect_obj(s, sid, 'star', legacy, name, refs, errors, S, effects)
        else:
            if any(s[k] is not None for k in ('memory', 'value', 'param', 'receiver', 'gimmick', 'power', 'stat', 'target')):
                errors.append(f'{sid}: Choice must keep effect fields null (use options)')
        check_options(s, sid, 'star', legacy, name, refs, errors, S, effects)
        # keystone
        if (kind == 'Keystone') != (region == 'keystone'):
            errors.append(f'{sid}: kind Keystone <-> region keystone mismatch')
        if kind == 'Keystone':
            ks = s['keystone']
            if not isinstance(ks, dict) or list(ks) != KS_KEYS:
                errors.append(f'{sid}: keystone keys must be exactly {KS_KEYS}')
            else:
                for k in ('upside', 'downside'):
                    if not (isinstance(ks[k], str) and ks[k]):
                        errors.append(f'{sid}: keystone.{k} missing')
                check_spec_list(ks['upsideSpec'], f'{sid}.upsideSpec', legacy, name, refs, errors)
                check_spec_list(ks['downsideSpec'], f'{sid}.downsideSpec', legacy, name, refs, errors)
            if s['gimmick'] or s['power'] or s['options']:
                errors.append(f'{sid}: Keystone effects go to keystone.*Spec (gimmick/power/options must be null)')
            if s['cluster'] != f'{name}.key':
                errors.append(f'{sid}: keystone cluster must be {name}.key')
            if s['edges']:
                errors.append(f'{sid}: Keystone edges must be [] (placement via anchor/requires)')
            if s['notes'].startswith('仮置き:'):
                provisional += 1
        elif s['keystone'] is not None:
            errors.append(f'{sid}: keystone only for Keystone')
        if '仮置き' in s['notes'] and not s['notes'].startswith('仮置き:') and kind == 'Keystone':
            errors.append(f'{sid}: provisional placement note must start with "仮置き:"')
        if kind != 'Keystone' and s['notes'].startswith('仮置き:'):
            errors.append(f'{sid}: "仮置き:" is for keystones only')
        # graph
        a = s['anchor']
        if not isinstance(s['edges'], list) or not isinstance(s['requires'], list) or not isinstance(s['requiresAny'], list):
            errors.append(f'{sid}: edges/requires/requiresAny must be lists')
            continue
        if not a and not s['edges']:
            errors.append(f'{sid}: no anchor/edges (unreachable)')
        if a:
            refs.append((sid, 'anchor', a))
            at = S.get(a)
            if at is not None and kind != 'Keystone' and at['cluster'] == s['cluster']:
                errors.append(f'{sid}: anchor {a} is inside the same cluster (anchor is for external entry only)')
        for r in s['requires'] + s['requiresAny']:
            refs.append((sid, 'requires', r))
            if r == sid:
                errors.append(f'{sid}: requires itself')
        for e in s['edges']:
            refs.append((sid, 'edges', e))
            t = S.get(e)
            if t is None:
                continue
            if e == sid:
                errors.append(f'{sid}: edge to itself')
            same = s['cluster'] == t['cluster']
            outer_ring = name == 'outer'
            if not same and not outer_ring:
                errors.append(f'{sid}: edge {e} leaves the cluster (use anchor/requires)')
            if sid not in t['edges']:
                errors.append(f'{sid}: edge {e} is not symmetric')

    # 参照整合
    for ctx, field, r in refs:
        if not isinstance(r, str) or not r:
            errors.append(f'{ctx}: empty {field}')
            continue
        ok = r in S or r in legacy or r in outer_ids
        if not ok:
            errors.append(f'{ctx}: {field} -> unknown id {r}')

    # 移行行
    mids = set()
    for s in mig:
        sid = s.get('id', '?')
        if list(s) != MIG_KEYS:
            errors.append(f'{sid}: migration keys must be exactly {MIG_KEYS} (diff {sorted(set(s) ^ set(MIG_KEYS))})')
            continue
        if sid not in legacy:
            errors.append(f'{sid}: migration id is not an existing legacy id')
        if sid in S:
            errors.append(f'{sid}: migration id collides with a new star id')
        mids.add(sid)
        if not (s['nameJa'] and s['nameEn'] and s['notes']):
            errors.append(f'{sid}: migration row needs nameJa/nameEn/notes')
        if not isinstance(s['maxRank'], int) or s['maxRank'] < 1:
            errors.append(f'{sid}: bad maxRank')
        for m in s['mechanisms']:
            if not MECH.match(m):
                errors.append(f'{sid}: bad mechanism {m}')
        k = s['kind']
        if k is not None and k not in KINDS:
            errors.append(f'{sid}: bad kind {k}')
            continue
        if k is None:
            if any(s[x] is not None for x in ('memory', 'value', 'param', 'receiver', 'gimmick', 'power', 'stat', 'options', 'keystone', 'target')):
                errors.append(f'{sid}: kind null row must not carry effect fields')
        elif k == 'Choice':
            if any(s[x] is not None for x in ('memory', 'value', 'param', 'receiver', 'gimmick', 'power', 'stat', 'target')):
                errors.append(f'{sid}: Choice must keep effect fields null (use options)')
        elif k == 'Keystone':
            ks = s['keystone']
            if not isinstance(ks, dict) or list(ks) != KS_KEYS or not ks['upside'] or not ks['downside']:
                errors.append(f'{sid}: bad keystone object')
            else:
                check_spec_list(ks['upsideSpec'], f'{sid}.upsideSpec', legacy, name, refs, errors)
                check_spec_list(ks['downsideSpec'], f'{sid}.downsideSpec', legacy, name, refs, errors)
        else:
            check_effect_obj(s, sid, 'migration', legacy, name, refs, errors, S, effects)
        if k != 'Keystone' and s['keystone'] is not None:
            errors.append(f'{sid}: keystone only for Keystone')
        check_options(s, sid, 'migration', legacy, name, refs, errors, S, effects)
        for r in s['requires'] + s['requiresAny']:
            if r not in legacy and r not in S:
                errors.append(f'{sid}: requires -> unknown id {r}')
    # 移行行内の参照（condition / replaces / target.star）
    done = set()
    for ctx, field, r in refs:
        if ctx in mids or (ctx.split('#')[0] in mids):
            if (ctx, field, r) in done:
                continue
            done.add((ctx, field, r))
            if not (r in S or r in legacy or r in outer_ids):
                errors.append(f'{ctx}: {field} -> unknown id {r}')
    return errors, provisional


def collect_effects():
    """全ファイルの gimmick.effect（新機構の効果名を含む）。"""
    out = set()
    for f in glob.glob(os.path.join(HERE, '*.json')):
        try:
            d = json.load(open(f, encoding='utf-8'))
        except Exception:  # noqa: BLE001
            continue
        for s in d.get('stars', []):
            for o in [s] + [x for x in (s.get('options') or []) if isinstance(x, dict)]:
                g = o.get('gimmick')
                if isinstance(g, dict) and isinstance(g.get('effect'), str):
                    out.add(g['effect'])
    return out


def main():
    names = sys.argv[1:] or list(EXPECTED)
    try:
        legacy, routes, enum_effects = legacy_data()
    except Exception as ex:  # noqa: BLE001
        print(f'cannot read legacy ids from {GAME}: {ex}')
        sys.exit(1)
    all_effects = collect_effects()
    total = 0
    for n in names:
        if n not in EXPECTED:
            print(f'{n}: unknown name')
            total += 1
            continue
        errs, prov = check(n, legacy, routes, enum_effects, all_effects)
        total += len(errs)
        extra = f' (仮置き keystone {prov})' if prov and not errs else ''
        print(f'{n}: ' + ('OK' if not errs else f'{len(errs)} errors') + extra)
        for e in errs[:40]:
            print('  ' + e)
    sys.exit(1 if total else 0)


if __name__ == '__main__':
    main()
