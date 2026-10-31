### 不具合の修正 / Bug fixes
- 長く遊ぶほどメモリが増えて部屋の移動が重くなる問題について、MOD 側で破棄済みオブジェクトを参照し続けていた箇所を、部屋の区切りとホスト権限の終了時に解放するようにしました。 / For the issue where memory grew over long sessions and room changes slowed down, the MOD now releases its references to destroyed objects at room boundaries and when host authority ends.
