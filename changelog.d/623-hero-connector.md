### 不具合の修正 / Bug fixes
- 旅人MODの連携報告に、同じ供給元の結果が重複して載る問題を直しました。 / Fixed duplicate results for the same source mod in the traveler connector report.
- マニフェストを使わない旅人宣言の名前の既定値、同一内容の再登録、エラーの説明を従来の動作に戻しました。 / Restored legacy name defaults, identical declaration registration, and error details for travelers without a star manifest.
- 星図の読み込みで不正な星を除外するとき、IDの前半が同じ別の星まで除外する問題を直しました。 / Fixed manifest validation omitting unrelated stars whose IDs share a prefix with an invalid star.
