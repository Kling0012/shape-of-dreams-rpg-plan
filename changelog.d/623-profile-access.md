### 不具合の修正 / Bug fixes
- プロフィールの読み込みや復旧用の写し先の確認でアクセスに失敗したとき、例外が復旧処理の外へ漏れる経路を修正しました。プロフィール切替中の存在確認の失敗も、読み取り失敗と同じく切替前に検出します。 / Fixed access failures escaping profile recovery while checking save files or choosing recovery copy paths. File-existence failures during a profile switch are now detected before switching, like read failures.
