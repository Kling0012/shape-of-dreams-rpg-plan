# Connecting traveler mods to Dreamforge

[日本語](MODDING.ja.md) | [Connector specification](specs/hero-connector.md)

A traveler mod can declare its name, memories, and star map in `dreamforge.json` in its MOD folder. The traveler mod supplies the hero prefab and memory implementations. Enable Dreamforge's hero connector and restart the game after changing declarations.

## Declaration

Keep `dreamforgeApi` at 1. `starManifest` names a file relative to the source MOD folder. In this example, Dreamforge reads `stars.json` beside the declaration. `starTree` provides a fallback. Existing declarations without `starManifest` continue to work.

```json
{
  "dreamforgeApi": 1,
  "heroes": [{
    "key": "Hero_Nova",
    "name": { "ja": "ノヴァ", "en": "Nova", "zh": "诺瓦" },
    "melee": false,
    "memories": [{
      "key": "St_Q_NovaBlink",
      "name": { "ja": "瞬き", "en": "Blink", "zh": "闪烁" }
    }],
    "starManifest": "stars.json",
    "starTree": {
      "nodes": [{ "id": "fallback", "stat": "AttackPct", "perRank": 1, "maxRank": 1 }]
    }
  }]
}
```

Runtime registration uses the existing `Dreamforge.Api.HeroConnector.RegisterHeroes` API. Set `sourceModId` to the loaded traveler mod's ID; the host resolves referenced files within that MOD folder. Registrations during an expedition are deferred until it ends.

## Star manifest

Use the [canonical one-object-per-star format](../tools/star-manifest/README.md). This complete `stars.json` example contains one MemoryDamage star. Numeric values are checked against registered stars of the same kind from the nine shipped travelers.

```json
{
  "hero": "Hero_Nova",
  "source": "Nova MOD",
  "stars": [{
    "id": "blink.focus",
    "region": "memory",
    "cluster": "blink",
    "shape": "chain",
    "anchor": "route.St_Q_NovaBlink.1",
    "edges": [],
    "requires": [],
    "requiresAny": [],
    "kind": "MemoryDamage",
    "memory": "St_Q_NovaBlink",
    "value": 1,
    "param": null,
    "receiver": null,
    "target": null,
    "gimmick": null,
    "power": null,
    "stat": null,
    "options": [],
    "keystone": null,
    "mechanisms": [],
    "maxRank": 1,
    "rankCost": 1,
    "nameJa": "瞬きの冴え",
    "nameEn": "Focused Blink",
    "nameZh": "专注闪烁",
    "notes": ""
  }]
}
```

`hero` must match the declared key. Short IDs receive the prefix `x.Hero_Nova.`, so this star is saved as `x.Hero_Nova.blink.focus`. The `h.` namespace and other travelers' `x.` namespaces are reserved. Existing stars and generated routes cannot be overwritten. The first occurrence reserves a duplicate ID.

Every declared memory receives a generated seven-star route. Its IDs run from `x.<hero-key>.route.<memory-id>.1` through `.7`. The first six stars have three ranks costing one point each; the last has one rank costing three points, following the shipped route structure. Their effect is the smallest positive MaxHealthPct value among registered ordinary shipped stars. Authors cannot override route effects or costs.

Anchor a memory cluster's entrance to a generated route star. Other stars inherit its placement; declare their graph connections in `edges`. Edges must be symmetric and stay within the cluster. `requires` needs every listed star, while `requiresAny` needs at least one. Other regions are `bridge` for receiver bridges, `outer`, and `keystone`. Outer clusters enter from a route, a memory region, or an effectful Stat root.

## Effects and limits

- All memory references, receivers, and selector candidates must appear in the traveler's declared `memories`, including shipped memories. Selectors such as `@Q` are restricted to those declarations when candidates are omitted.
- Supported kinds are MemoryDamage, MemoryHaste, GimmickBoost, GimmickParam, Notable, Choice, Stat, and Keystone. Ordinary stars have one rank and cost one point. Choice has exactly two options; nested choices and Keystone options are unsupported. Stat stars belong in outer regions.
- Notables support ordinary Gimmicks, DirectedRecharge, MemoryPrimed, and AlliedWard. Keystones support typed transforms and Grants. Dedicated pair combinations, traveler-specific scaling and RunGrowth, Power, native memory adapters, and keystone downsides are outside this external format's supported subset.
- Receiver modifiers use `memory: null` and a declared `receiver`. Sender-filtered receiver modifiers are unsupported. Broad modifiers resolve to concrete compatible mechanisms; a modifier without a recipient is omitted.
- Numbers may be literals or `valueRef` references to registered shipped stars. Supported paths include top-level `value`, choice `options/N/value`, `stat/perRank`, supported mechanism `gimmick/value`, `arg`, `cooldown`, `everyN`, and `valuesByRank/N` fields, and numeric `keystone/upsideSpec/N` fields on transform-only keystones. Unsupported paths are reported and rejected. Power, legacy native sources, and mixed Grant reference paths are unsupported.
- MemoryDamage uses exact thousandths. Integer fields require integers. Limits follow matching shipped kinds, triggers, recipients, and timing. Inputs requiring runtime clamping are rejected. Combined same-kind transforms and Grants within one keystone are also bounded.
- Accepted stars, including generated routes, cannot exceed `floor(largest shipped tree count × 1.2)`. More stars do not grant more allocation points. Existing wire and effective-channel capacity checks apply.
- Each file is limited to 4 MiB and JSON depth to 64. Duplicate object keys, absolute paths, `.`, `..`, empty path components, and symbolic links are rejected.

## Diagnostics and saved progress

Invalid stars are omitted with a warning; dangling references are pruned repeatedly. An unreadable file, malformed whole document, or zero accepted stars falls back to that traveler's `starTree`, then to the generic tree if needed. Check `[Dreamforge/Connector]` lines in `Player.log`.

The repository also includes an offline registration command:

```sh
dotnet run --project tools/hero-connector -- --manifest "<source-MOD-folder>"
```

`Authored: true` indicates an authored tree was registered; `Stars` includes generated routes. Exit code 0 can also mean a successful fallback, so inspect warnings and `Authored`.

Purchases, choices, keystones, and loadouts use the existing save format. Removing a traveler mod leaves its external records dormant and effectless. Restoring the same IDs makes the saved records usable again. They do not become allocations for another traveler.

Co-op declaration hashes include normalized accepted stars and generated routes. Rejected stars do not contribute. Use matching traveler mods and star manifests on the host and participants.
