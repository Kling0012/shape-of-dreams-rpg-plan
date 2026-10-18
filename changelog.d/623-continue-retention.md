### 不具合の修正 / Bug fixes

- 本体の再開保存を確認した地点の直前にあるチェックポイントを、未確定地点の件数制限とは別に保持するようにしました。 / Retain the checkpoint immediately before a confirmed native resume save independently of the pending checkpoint limit.
- プロフィールの読み込みや退避で権限エラーを扱うようにしました。スロットの読み込みでは、元のデータを変更する前に読み込みを中止します。 / Handle access errors during profile loading and preservation. Slot loading aborts before modifying source data.
