PR ごとの更新履歴。リリースで CHANGELOG.md にまとめ、このフォルダからは消す。

- PR では `CHANGELOG.md` を変更しない。
- ファイル名は `<issue か PR の番号>-<短い英字>.md`（例: `123-coop-fix.md`）。
- 1行目は次のいずれかの分類にする。
  - `### 新機能 / New features`
  - `### 不具合の修正 / Bug fixes`
  - `### 改善 / Improvements`
- 続けて、`- ` で始まる項目を日英で書く。
- 書くのは新機能と不具合の修正だけ。バランスの変更は書かない。
- 複数の分類が必要なら、分類ごとに見出しと項目を続ける。
- リリース時は分類ごとに集約し、取り込んだ断片ファイルをリリースの commit で削除する。この README は残す。

例:

```markdown
### 不具合の修正 / Bug fixes
- 協力プレイで再接続したときに装備表示が更新されない問題を直しました。 / Fixed equipment displays not updating after reconnecting in co-op.
```
