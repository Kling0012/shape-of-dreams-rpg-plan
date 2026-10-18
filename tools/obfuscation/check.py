#!/usr/bin/env python3
"""Compare release DLL metadata without loading game code or decompiling C#.

Usage: check.py plain.dll obfuscated.dll obfuscar.xml
Requires the .NET SDK; Metadata/ uses Mono.Cecil 0.11.6.
"""
import argparse
from collections import Counter
import fnmatch
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

WIRE_TYPE_RE = re.compile(r'^(Dreamforge\w*Msg|DreamforgeCoopTradeUp|DreamforgeCoopTradeDown)$')
PATCH_METHOD_RE = re.compile(r'^(prefix|postfix|transpiler|finalizer|targetmethods?|prepare|cleanup)$', re.I)
ENTRY_MEMBERS = {'Awake', 'Update', 'OnGUI', 'OnDestroy', 'OnApplicationFocus',
                 'OnConfigChanged', 'LoadConfigsToDisk'}


def run(command):
    env = dict(os.environ, DOTNET_ROLL_FORWARD='LatestMajor')
    result = subprocess.run(command, capture_output=True, text=True, env=env, timeout=120)
    if result.returncode:
        sys.stderr.write(result.stdout + result.stderr)
        raise RuntimeError(f'Command failed: {command[0]} (exit {result.returncode})')
    return result.stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('plain', type=Path)
    parser.add_argument('obfuscated', type=Path)
    parser.add_argument('config', type=Path)
    args = parser.parse_args()
    project = Path(__file__).resolve().parent / 'Metadata' / 'Metadata.csproj'
    print('Reading DLL metadata with Mono.Cecil 0.11.6...', flush=True)
    run(['dotnet', 'build', str(project), '-c', 'Release', '--nologo', '-v:q'])
    reader = project.parent / 'bin' / 'Release' / 'net8.0' / 'Metadata.dll'
    plain = json.loads(run(['dotnet', str(reader), str(args.plain.resolve())]))
    obfuscated = json.loads(run(['dotnet', str(reader), str(args.obfuscated.resolve())]))
    before = {t['name']: t for t in plain['types']}
    after = {t['name']: t for t in obfuscated['types']}
    failures = []

    def check(ok, label, details=()):
        print(('[ok] ' if ok else '[FAIL] ') + label, flush=True)
        if not ok:
            failures.append(label)
            for detail in details:
                print('  ' + str(detail), flush=True)

    root = ET.parse(args.config).getroot()
    variables = {v.get('name'): v.get('value') for v in root.findall('Var')}
    check(variables.get('HideStrings') == 'false' and variables.get('OptimizeMethods') == 'false',
          'Only names are obfuscated (HideStrings=false, OptimizeMethods=false)')
    check(plain['assembly'] == obfuscated['assembly'], 'Assembly name is unchanged')
    check(plain['references'] == obfuscated['references'], 'Assembly references are unchanged')
    check(plain['resources'] == obfuscated['resources'], 'Embedded resource names are unchanged')
    check(len(before) == len(after), f'Type count is unchanged ({len(before)} -> {len(after)})')
    renamed = len(before.keys() - after.keys())
    check(renamed >= len(before) / 2, f'Renamed types: {renamed}/{len(before)} ({renamed / len(before):.1%})')

    # Resolve the same type rules that the release config uses; reject unsupported rules.
    protected = {}
    for rule in root.findall('.//SkipType'):
        if set(rule.attrib) - {'name', 'decorator', 'typeinherits', 'skipMethods', 'skipFields', 'skipProperties', 'skipEvents'}:
            raise RuntimeError(f'Unsupported SkipType rule: {rule.attrib}')
        matches = [t for t in before.values()
                   if fnmatch.fnmatchcase(t['name'], rule.get('name', '*'))
                   and (not rule.get('decorator') or rule.get('decorator') in t['attributes'])
                   and (not rule.get('typeinherits') or rule.get('typeinherits') == t['baseType'])]
        check(bool(matches), f'SkipType rule matches: {rule.attrib}')
        for t in matches:
            flags = protected.setdefault(t['name'], set())
            flags.update(k for k, v in rule.attrib.items() if k.startswith('skip') and v == 'true')
    missing = sorted(protected.keys() - after.keys())
    check(not missing, f'Protected type names: {len(protected)}', missing)

    def signature(m):
        return (m['name'], len(m['parameters']), m['genericCount'])

    lost = []
    counts = Counter()
    for name, flags in protected.items():
        old, new = before[name], after.get(name)
        if new is None:
            continue
        for flag, kind in [('skipFields', 'fields'), ('skipProperties', 'properties'), ('skipEvents', 'events')]:
            if flag not in flags:
                continue
            old_names = {v if isinstance(v, str) else v['name'] for v in old[kind]}
            new_names = {v if isinstance(v, str) else v['name'] for v in new[kind]}
            lost.extend(f'{name}: missing {kind} {n}' for n in sorted(old_names - new_names))
            counts[kind] += len(old_names)
        if 'skipMethods' in flags:
            wanted = Counter(signature(m) for m in old['methods'])
            got = Counter(signature(m) for m in new['methods'])
            lost.extend(f'{name}: missing method {s}' for s in (wanted - got).elements())
            counts['methods'] += len(old['methods'])
            for method in old['methods']:
                candidates = [m for m in new['methods'] if signature(m) == signature(method)]
                if not any([p['name'] for p in m['parameters']] == [p['name'] for p in method['parameters']]
                           for m in candidates):
                    lost.append(f'{name}::{method["name"]}: changed parameter names')
    check(not lost, f'Protected members: {dict(counts)}', lost)

    # Preserve every attributed patch type, every patch method, and all parameter names
    # on those types (including original argument names and ___field injection).
    patch_types = [t for t in before.values() if 'HarmonyLib.HarmonyPatch' in t['attributes']]
    patch_errors = []
    patch_methods = 0
    patch_params = 0
    for old in patch_types:
        new = after.get(old['name'])
        if new is None:
            patch_errors.append(f'Missing patch type {old["name"]}')
            continue
        if old['attributes'] != new['attributes']:
            patch_errors.append(f'Changed attributes on {old["name"]}')
        for method in old['methods']:
            if not (PATCH_METHOD_RE.match(method['name'])
                    or any(a.startswith('HarmonyLib.Harmony') for a in method['attributes'])):
                continue
            patch_methods += 1
            patch_params += len(method['parameters'])
            candidates = [m for m in new['methods'] if signature(m) == signature(method)]
            if not any([p['name'] for p in m['parameters']] == [p['name'] for p in method['parameters']]
                       and m['attributes'] == method['attributes'] for m in candidates):
                patch_errors.append(f'{old["name"]}::{method["name"]}: changed name, attributes, or parameters')
    check(bool(patch_types) and not patch_errors,
          f'Harmony: {len(patch_types)} patch types, {patch_methods} methods, {patch_params} parameter names', patch_errors)

    wire = [t for t in before.values() if WIRE_TYPE_RE.match(t['simpleName'])
            or 'Mirror.NetworkMessage' in t['interfaces']]
    wire_errors = []
    for t in wire:
        if t['name'] not in protected or not {'skipFields', 'skipProperties'} <= protected[t['name']]:
            wire_errors.append(f'Unprotected wire DTO {t["name"]}')
    check(bool(wire) and not wire_errors, f'Wire DTOs: {len(wire)}', wire_errors)

    # Newtonsoft JSON traverses public fields/properties recursively. Discover roots
    # from the preserved DTOs and from generic DeserializeObject<T> calls in the IL.
    json_roots = {name for name, flags in protected.items() if 'skipFields' in flags}
    for t in before.values():
        for method in t['methods']:
            for call in method['calls']:
                if call['owner'] == 'Newtonsoft.Json.JsonConvert' and call['name'] == 'DeserializeObject':
                    json_roots.update(a for a in call['arguments'] if a in before)
    pending = list(json_roots)
    visited = set()
    json_errors = []
    while pending:
        name = pending.pop()
        if name in visited or name not in before:
            continue
        visited.add(name)
        t = before[name]
        flags = protected.get(name, set())
        if t['isEnum']:
            continue
        if not {'skipFields', 'skipProperties'} <= flags:
            json_errors.append(f'Unprotected JSON DTO {name}')
        for member in t['fields'] + t['properties']:
            if member['isPublic']:
                pending.extend(n for n in before if n == member['type'] or n in re.split(r'[<>,\[\]]', member['type']))
    check(not json_errors, f'JSON DTO graph: {len(visited)} types', json_errors)

    enums = [t for t in before.values() if t['isEnum']]
    enum_errors = []
    for t in enums:
        new = after.get(t['name'])
        want = [(f['name'], f['constant']) for f in t['fields']]
        got = [(f['name'], f['constant']) for f in new['fields']] if new else []
        if want != got:
            enum_errors.append(t['name'])
    check(not enum_errors, f'Enum names and values: {len(enums)}', enum_errors)

    # Literal names (including nameof) used by reflection/manual Harmony registration
    # must still identify members in protected types. All IL literals stay unchanged.
    literals = Counter(s for t in before.values() for m in t['methods'] for s in m['strings'])
    obf_literals = Counter(s for t in after.values() for m in t['methods'] for s in m['strings'])
    check(literals == obf_literals, f'IL string literals are unchanged ({sum(literals.values())})')
    reflection_errors = []
    for t in before.values():
        for method in t['methods']:
            if not any(c['owner'].startswith(('HarmonyLib.AccessTools', 'System.Type', 'System.Reflection'))
                       or c['name'] == '.ctor' and c['owner'] == 'HarmonyLib.HarmonyMethod' for c in method['calls']):
                continue
            # Include literals from helpers and callers in this type (dynamic name arguments).
            names = {s for m in t['methods'] for s in m['strings']}
            targets = {n for m in t['methods'] for n in m['typeTokens'] if n in before}
            for target_name in targets:
                target = before[target_name]
                matched = {m['name'] for m in target['methods']} & names
                for member in matched:
                    if target['name'] not in protected or 'skipMethods' not in protected[target['name']]:
                        reflection_errors.append(f'{target["name"]}::{member} (literal in {t["name"]})')
    check(not reflection_errors, 'Reflection/manual patch method names are protected', sorted(set(reflection_errors)))

    entry = after.get('SodRpg.Mod.DreamforgeMod', {})
    entry_methods = {m['name'] for m in entry.get('methods', [])}
    check(entry.get('baseType') == 'ModBehaviour' and ENTRY_MEMBERS <= entry_methods
          and any(f['name'] == 'config' for f in entry.get('fields', [])),
          'MOD entry, Unity callbacks, and config field are present')
    print(f'Checked {len(protected)} protected types; failures={len(failures)}', flush=True)
    return int(bool(failures))


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired, ET.ParseError) as error:
        sys.exit(f'Metadata check failed: {error}')
