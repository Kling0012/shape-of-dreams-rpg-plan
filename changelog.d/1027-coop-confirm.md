### 不具合の修正 / Bug fixes
- 協力取引で、保管庫が大きくて確認データの送信が数フレームに分かれるとき、その送信の途中に相手の確認が届くと、提示が変わっていないのに取引が取り消されていた問題を直しました。 / Fixed co-op trades being cancelled even though the offer had not changed, when the partner's confirmation arrived while the player's own confirmation data was still being sent over several frames.
