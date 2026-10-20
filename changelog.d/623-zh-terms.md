### 不具合の修正 / Bug fixes

- 図鑑の中国語の固有効果名を既存の明示訳から取得し、一般語に固有名の訳が適用される問題と、中国語での図鑑生成後に表示言語が英語へ変わる問題を直しました。 / Fixed Chinese codex power names to use their explicit translations, prevented power-specific translations from affecting generic text, and preserved Chinese as the display language after codex generation.
- 中国語の奥義・共演・疑心の表記ゆれと英語残りを直しました。 / Fixed inconsistent Chinese terms and untranslated Ultimate references in power, waypoint and equipment text.
- 保存エラーの英文が null の場合も、エラーメッセージの取得中に例外が起きないようにしました。 / Prevented retrieving a save error message from throwing when its English text is null.
