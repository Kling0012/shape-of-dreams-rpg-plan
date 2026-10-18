### 不具合の修正 / Bug fixes
- 味方への効果で、10m未満の範囲にも距離減衰と猶予が付く問題を修正しました。回復・吸収の範囲が変わっても、それぞれの猶予の履歴を引き継ぐようにしました。 / Fixed allied effects with radii below 10m receiving falloff and grace. Heal and Siphon now retain their respective grace histories when their radii change.
- 元の調理上限が不正な料理素材が複数ある場合に、最初の素材しか警告されない問題を修正しました。警告は素材のスタックごとに一度表示します。 / Fixed warnings identifying only the first ingredient when multiple stacks have unsafe native cooking limits. Each ingredient stack now receives one warning.
- 専用の星図がない旅人に、汎用の星図が専用の配置として生成される問題を修正しました。追加登録された星図を優先し、それ以外は汎用の配置を使います。 / Fixed travelers without a dedicated tree generating the generic tree with a hero layout. Registered trees retain priority; other travelers without a dedicated tree use the generic layout.
