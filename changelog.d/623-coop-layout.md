### 不具合の修正 / Bug fixes
- 味方への効果で、10m未満の範囲にも距離減衰と猶予が付く問題を修正しました。回復・吸収の範囲が変わっても、それぞれの猶予の履歴を引き継ぐようにしました。 / Fixed allied effects with radii below 10m receiving falloff and grace. Heal and Siphon now retain their respective grace histories when their radii change.
- 元の調理上限が不正な料理素材が複数ある場合に、最初の素材しか警告されない問題を修正しました。警告は素材のスタックごとに一度表示します。 / Fixed warnings identifying only the first ingredient when multiple stacks have unsafe native cooking limits. Each ingredient stack now receives one warning.
