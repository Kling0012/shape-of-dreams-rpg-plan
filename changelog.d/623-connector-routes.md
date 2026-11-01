### 不具合の修正 / Bug fixes
- 同じMODが宣言ファイルとランタイムAPIを併用したとき、旅人連携の報告とキャッシュが上書きされる問題を直しました。ファイル全体の登録に失敗した結果はキャッシュせず、次回に再試行します。 / Fixed hero connector reports and caches overwriting each other when a MOD uses both a declaration file and the runtime API. File-level registration failures are no longer cached, allowing retries on subsequent calls.
