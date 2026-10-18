### 不具合の修正 / Bug fixes

- 保存ファイルの存在確認に失敗したときは、既存データを保護するために保存を止めるようにしました。 / Saving now pauses when a save file's existence cannot be checked, protecting existing data.
- カメラ角度が負のゼロ（`-0f`）のときも、元の角度を使って回転を計算するようにしました。 / Camera rotation now uses the original angle when it is negative zero (`-0f`).
