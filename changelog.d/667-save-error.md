### 不具合の修正 / Bug fixes

- 保存に失敗したときの理由の表示が、表示言語を切り替えたあとも現在の言語で出るようになりました。また、保存中に例外が出たあとでさらに書き込みに失敗したときは、古い例外の文が残り続けず、新しい書き込み失敗の表示に切り替わります。 / Save failure reasons on screen now follow the display language after you switch it. When a write fails again after an exception during saving, the newer write failure replaces the lingering older exception message.
- 保存に失敗したときのログ（`Player.log` の "Save failed" 行）は、表示言語に関係なく英文で出るようになりました。 / The "Save failed" log entry is now written in English regardless of the display language.
