### 不具合の修正 / Bug fixes
- 同じMODが宣言ファイルとランタイムAPIを併用したとき、旅人連携の報告とキャッシュが上書きされる問題を直しました。ファイル全体の登録に失敗した結果は再利用せず次回に再試行し、同じ失敗が続くときは警告を1回に抑えます。 / Fixed hero connector reports and caches overwriting each other when a MOD uses both a declaration file and the runtime API. File-level registration failures are retried on later scans instead of being reused, and a repeating failure is warned about only once.
