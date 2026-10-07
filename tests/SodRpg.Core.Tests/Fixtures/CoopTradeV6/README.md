# Actual version-6 cooperative trade fixtures

These files were emitted by the repository's unmodified Core sources at commit `34d3b18b5147d63a316f722ce23fdacde944e65b`, the base of PR #269 (profile format 6). Every source file was checked against its Git blob SHA before compilation.

Two profiles (seeds 17 and 19) each contain a common relic with IDs `first-relic` and `second-relic`, an active `run`, and a native Continue checkpoint. They have 100 and 50 permanent shards, plus 9 satchel shards and 3 satchel tuning each. The first offers its relic and 20 shards; the second offers its relic and 7 shards. `CoopTradeRules.Prepare` was called for each profile, then `CoopTradeJournal.Commit` wrote the journal. Both escrow profiles were saved before Resolve.

The same version-6 code successfully reopened the journal and resolved it to 87/63 permanent shards with exchanged relics. This exercises migration of real saved journal evidence, including embedded Continue checkpoints, rather than changing a current-version fixture's version number.
