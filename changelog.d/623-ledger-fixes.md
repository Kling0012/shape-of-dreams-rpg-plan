### 不具合の修正 / Bug fixes
- 装飾品の顔への追従を軽くする処理で例外が起きたあと、同じ処理を毎フレーム再試行する問題を直しました。失敗後はこの軽量化だけを止め、本体の追従処理を使います。 / Fixed the face-attachment optimization retrying every frame after an exception. A failure now disables only this optimization and uses the game's attachment behavior.
- 鍛錬の数値表で必須キーが欠けたとき、KeyError ではなく入力形式を示す ValueError を返すように直しました。 / Fixed missing required keys in run-growth balance tables raising KeyError instead of a descriptive ValueError.
