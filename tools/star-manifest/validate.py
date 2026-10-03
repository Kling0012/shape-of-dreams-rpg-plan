"""v1.31 星マニフェストの検証。使い方は README.md。失敗があれば終了コード1。"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
# New private IDs（docs/specs/v1.31-design-review.md の表）。外縁は共有160。
EXPECTED = {
    'vesper': 645, 'cetus': 645, 'lacerta': 645, 'husk': 646, 'mist': 652,
    'yubar': 644, 'aurena': 510, 'nachia': 502, 'bismuth': 510, 'outer': 160,
}
KINDS = {'MemoryDamage', 'MemoryHaste', 'GimmickBoost', 'GimmickParam', 'Notable', 'Choice', 'Stat', 'Keystone'}
REGIONS = {'memory', 'bridge', 'outer', 'keystone', 'migration'}
PARAMS = {None, 'Duration', 'Radius', 'ExtraTargets', 'Chance'}
TRIGGERS = {'OnUse', 'OnHit', 'OnKill', 'OnCrit', 'OnBasicAttack'}
MECH = re.compile(r'^C(0[1-9]|1[0-5])$')


def check(name):
    errors = []
    path = os.path.join(HERE, name + '.json')
    if not os.path.exists(path):
        return [f'{name}: file missing']
    try:
        data = json.load(open(path, encoding='utf-8'))
    except Exception as ex:  # noqa: BLE001
        return [f'{name}: invalid JSON: {ex}']
    stars = data.get('stars', [])
    ids = [s.get('id') for s in stars]
    dup = {i for i in ids if ids.count(i) > 1}
    if dup:
        errors.append(f'{name}: duplicate ids {sorted(dup)[:10]}')
    new = [s for s in stars if s.get('region') != 'migration']
    if len(new) != EXPECTED[name]:
        errors.append(f'{name}: {len(new)} new stars, expected {EXPECTED[name]}')
    for s in stars:
        sid = s.get('id', '?')
        if s.get('region') not in REGIONS:
            errors.append(f'{sid}: bad region {s.get("region")}')
        if s.get('region') == 'migration':
            continue
        k = s.get('kind')
        if k not in KINDS:
            errors.append(f'{sid}: bad kind {k}')
            continue
        for f in ('nameJa', 'nameEn'):
            if not s.get(f):
                errors.append(f'{sid}: missing {f}')
        if s.get('param') not in PARAMS:
            errors.append(f'{sid}: bad param {s.get("param")}')
        if k in ('MemoryDamage', 'MemoryHaste', 'GimmickBoost', 'GimmickParam') and not (s.get('memory') or s.get('receiver')):
            errors.append(f'{sid}: {k} needs memory or receiver')
        if k == 'GimmickParam' and not s.get('param'):
            errors.append(f'{sid}: GimmickParam needs param')
        if k in ('MemoryDamage', 'MemoryHaste', 'GimmickBoost', 'GimmickParam', 'Stat') and not isinstance(s.get('value'), (int, float)):
            errors.append(f'{sid}: {k} needs numeric value')
        if k == 'Choice' and len(s.get('options') or []) != 2:
            errors.append(f'{sid}: Choice needs exactly 2 options')
        if k == 'Stat' and s.get('region') != 'outer':
            errors.append(f'{sid}: Stat only allowed in outer')
        if k == 'Keystone' and not s.get('keystone'):
            errors.append(f'{sid}: Keystone needs upside/downside')
        if k == 'Notable' and not (s.get('gimmick') or s.get('power')):
            errors.append(f'{sid}: Notable needs gimmick or power')
        for g in [s.get('gimmick')] + [o.get('gimmick') for o in (s.get('options') or []) if isinstance(o, dict)]:
            if not g:
                continue
            if g.get('trigger') not in TRIGGERS:
                errors.append(f'{sid}: bad trigger {g.get("trigger")}')
            src = s.get('memory') or ''
            if src.startswith('St_M_'):
                errors.append(f'{sid}: movement memory {src} used as trigger source')
        for m in s.get('mechanisms') or []:
            if not MECH.match(m):
                errors.append(f'{sid}: bad mechanism {m}')
        if not s.get('edges') and not s.get('anchor'):
            errors.append(f'{sid}: no anchor/edges (unreachable)')
    return errors


def main():
    names = sys.argv[1:] or list(EXPECTED)
    total = 0
    for n in names:
        errs = check(n)
        total += len(errs)
        print(f'{n}: ' + ('OK' if not errs else f'{len(errs)} errors'))
        for e in errs[:40]:
            print('  ' + e)
    sys.exit(1 if total else 0)


if __name__ == '__main__':
    main()
