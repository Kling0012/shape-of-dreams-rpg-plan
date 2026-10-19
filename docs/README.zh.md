# Dreamforge RPG（简体中文说明）

本页面是《Shape of Dreams》MOD「Dreamforge RPG」的简体中文说明。
日文说明位于发布页面和仓库的 [README](../README.md)。
英文说明见 [docs/README.en.md](README.en.md)。

- 最新版本与安装用 ZIP：[Releases](https://github.com/Kling0012/shape-of-dreams-rpg-plan/releases)
- 各版本更新内容：[CHANGELOG](../../CHANGELOG.md)

Dreamforge RPG 将每一次远征变成长篇 RPG 旅程的一部分：可以带回来的「遗物」、每个区域都要做出的「存入仓库还是继续下潜」的抉择、每位旅人各自的星图、锻造以及长期目标。支持多人协作游玩。

## 问题反馈与需求

本 MOD 完全由 AI 制作，尚未经过人工测试游玩。非常欢迎玩家积极反馈。

- 问题反馈与需求：[github.com/Kling0012/dreamforge-rpg/issues](https://github.com/Kling0012/dreamforge-rpg/issues)
- 更新日志：[github.com/Kling0012/dreamforge-rpg/releases](https://github.com/Kling0012/dreamforge-rpg/releases)

## 安装方法

1. 从 [Releases](https://github.com/Kling0012/shape-of-dreams-rpg-plan/releases) 下载 `DreamforgeRPG-vX.Y.Z.zip`。
2. 解压后，将其中的 `DreamforgeRPG` 文件夹放入游戏的 `Mods` 文件夹（`<Shape of Dreams>/Mods/DreamforgeRPG/`）。
3. 启动游戏，在 MOD 管理界面中启用 Dreamforge RPG。
4. 更新时，用新文件夹替换 `DreamforgeRPG` 文件夹并重启游戏。存档会继承。

- **多人游玩时，所有参与者需要使用相同版本的 MOD。** 属性加成由主机负责应用。
- 存档保存在 `<persistentDataPath>/QuickSave/Mods/DreamforgeRPG/`（Windows 下为 `%USERPROFILE%/AppData/LocalLow/Lizard Smoothie/Shape of Dreams/QuickSave/Mods/DreamforgeRPG/`）。上一次的存档会以 `.bak` 保留。

## 遗物

- 击败敌人后，每名玩家各自获得独立的遗物。部位有 6 种，稀有度有 5 档，每件遗物都有各自的图案，还包括固有品与套装。
- 稀有遗物带 1 个固有效果，史诗遗物带 2 个。固有效果会与本体机制联动：Q/W/E 的技能轮换、会心、眩晕、超量治疗、精华品质等。
- 固有品在穿戴击杀敌人时逐步觉醒，觉醒共 3 段。

## 存入仓库还是继续下潜

- 每到达一个新区域，可以选择「存入仓库」将战利品带回，或「继续下潜」换取更多更好的掉落，但每深入 1 层受到的伤害增加 6%。
- 存仓点可能出现「梦境事件」，以及用诅咒换取收益的「噩梦契约」。
- 全灭后，未带回的遗物会变成遗失物。下一次远征突破 3 个战斗房间即可取回其中最好的一件。

## 敌人

- 敌人随下潜深度变强，穿戴的装备越强，带有特殊性质的噩梦敌人就越多。
- 「梦境之压」根据队伍平均进度（而非人数）提升敌人的 HP、伤害与数量。不会增加首领数量。
- 梦境变种：以本体敌人为基础，拥有新的外观、能力与更好的战利品。

## 成长

- 每位旅人拥有各自的星图、星之经验与点数。路线星强化特定记忆。洗点免费。
- 锻造：强化、再调律、分解、合成与制造。工坊可以解锁各种便利强化。
- 每次远征的委托、每日更换的「今日之梦」、带奖励的伟业，以及旅人熟练度。

## 性能

可选设置可减轻整个游戏的负担：后台时的 FPS 上限，以及更轻量的画面渲染。

## 操作

- F6：Dreamforge 菜单
- F7：存入仓库，F8：继续下潜（在显示存仓点面板时）
- 按键、语言与界面大小可在 MOD 设置中更改。游戏内语言可在日语、英语、简体中文之间切换；暂无中文翻译的文本会显示为英语。

## 多人游玩

所有参与者需要使用相同版本的 MOD。属性加成由主机应用。每名玩家的存档相互独立，战利品也按玩家分别掉落。

本 MOD 会改变游戏玩法。存档位置：`<persistentDataPath>/QuickSave/Mods/DreamforgeRPG/profile.json`
