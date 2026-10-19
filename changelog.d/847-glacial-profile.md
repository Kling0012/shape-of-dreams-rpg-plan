### 不具合の修正 / Bug fixes
- 雪嶺の核の報酬プロファイルで段階やアクションが不足している場合は、原因を初回だけ警告に残し、本体の射撃間隔を使うようにしました。段階が3件未満の場合に、別の段階のアクションを射撃間隔に使う問題も直しました。 / Glacial Core now logs a warning once and uses the native shooting interval when reward profile stages or actions are missing. Fixed interval selection using an action from another stage when the profile has fewer than three stages.
