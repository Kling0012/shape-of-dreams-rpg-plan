using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>中国語（簡体字）の用語集（#877）。英語の文字列を鍵にして中国語へ訳す。
    /// ここにない文字列と、数値を埋め込んだ呼び出し側で組み立てた文字列は英語のまま表示する
    /// （未訳は英語へ戻す方針。呼び出し元の Loc.T / Txt の英語引数と完全一致が条件）。
    /// 主な用語の対応: 遺物→遗物、欠片→碎片、調律石→调律石、保管庫→仓库、確保→存入仓库、
    /// 潜る→下潜、夢の圧→梦境之压、星図→星图、鍛冶→锻造、工房→工坊、記憶→记忆、
    /// エッセンス→精华、遠征→远征、道標→路标、契約→契约、依頼→委托、図鑑→图鉴、遺失物→遗失物。
    /// #939 で追加した主な用語: 偉業→成就、覚醒→觉醒、限界突破→极限突破、ドリームダスト→梦境之尘、
    /// 再調律→再调律、洗い直し→重洗、特性→词条、禁忌→禁忌、製作→制作、変種→变种、兆し→预兆。
    /// 辞書は最初の参照で組み立て、読み取り専用で使い回す（表示のたびに作り直さない）。
    /// </summary>
    internal static class LocZhGlossary
    {
        private static Dictionary<string, string> _map;

        /// <summary>英語の文字列から中国語を引く。なければそのまま英語を返す（割り当てなし）。</summary>
        public static string Lookup(string en)
        {
            var map = _map ??= Build();
            return map.TryGetValue(en, out string zh) ? zh : en;
        }

        private static Dictionary<string, string> Build()
        {
            return new Dictionary<string, string>
            {
                // ウィンドウとタブ
                ["Dreamforge ─ Relics of the Dream"] = "Dreamforge ─ 梦之遗物",
                ["Gear"] = "装备",
                ["Forge"] = "锻造",
                ["Star Map"] = "星图",
                ["Workshop"] = "工坊",
                ["Records"] = "记录",
                ["Trade"] = "交易",
                ["Trade ●"] = "交易 ●",
                ["Close"] = "关闭",
                ["Close selection"] = "关闭选择",
                ["How to play"] = "玩法说明",
                ["Settings"] = "设置",
                ["Show tips again"] = "再次显示提示",

                // HUD
                ["<b>Dreamforge</b>  Dream Lv "] = "<b>Dreamforge</b>  梦境等级 ",
                ["Dream depth "] = "梦境深度 ",
                [" · Pressure "] = " · 梦境之压 ",
                [" · Shared pressure "] = " · 共享梦境之压 ",
                [" · ATK×"] = " · 攻×",
                ["awaiting host"] = "等待主机确认",
                ["Infinity · cleared "] = "无限 · 已通关 ",
                [" rooms · cycle "] = " 房间 · 周期 ",
                [" · pressure stage "] = " · 压力阶段 ",
                [" (cap)"] = "（上限）",
                ["Save mismatch: progression paused; recovery required"] = "存档不一致：进度暂停，等待恢复",
                ["Waypoint: "] = "路标：",
                ["Today: "] = "今日之梦：",
                ["Delve "] = "下潜 ",
                ["Pacts: "] = "契约：",
                ["Gear bonuses not applied yet (the host may not have this mod)"] = "装备效果尚未生效（主机可能未安装本MOD）",
                ["] Menu"] = "] 菜单",

                // 確保地点のパネル
                ["Secure Point ─ take your loot home, or delve deeper"] = "存仓点 ─ 选择带回战利品，或继续深入",
                ["Secure: "] = "存入仓库：",
                ["Delve: "] = "下潜：",
                ["Finish without a pact"] = "不结契约，完成选择",
                ["Skip (no choices)"] = "跳过（不作选择）",
                ["Skip the waypoint"] = "不选路标",
                ["No waypoint"] = "无路标",
                ["The host chooses the waypoint."] = "路标由主机选择。",
                ["Waiting for the host to confirm a waypoint and choose Secure or Delve."] = "请等待主机确定路标并选择存入仓库或下潜。",

                // 装備・鍛冶の画面でよく出る語
                ["All"] = "全部",
                ["None"] = "无",
                ["Auto"] = "自动",
                ["Solo"] = "单人",
                ["Multi"] = "多人",
                ["Clear"] = "清除",
                ["Equip"] = "装备",
                [" equipped"] = "（已装备）",
                ["Lock"] = "锁定",
                ["Unlock"] = "解锁",
                ["Unlock to salvage"] = "解锁后可分解",
                ["Search name"] = "按名称搜索",
                ["Stats"] = "属性",
                ["Powers"] = "固有效果",
                ["Named"] = "铭品",
                ["Traveler"] = "旅人",
                ["Base: "] = "基底：",
                ["Rarity: "] = "稀有度：",
                ["Slot: "] = "部位：",
                ["Selected: "] = "已选：",
                ["Selected relics"] = "已选遗物",
                ["No relics selected."] = "未选择遗物。",
                ["Tuning"] = "调律石",
                ["Mini set"] = "小套装",
                ["Mini sets"] = "小套装",
                ["Choice stars"] = "二选一星",
                ["Memory essence slots"] = "记忆精华槽",
                ["Effects by memory"] = "各记忆的效果",
                ["Edit slot"] = "编辑部位",
                ["Satchel (unequipped only)"] = "背包（仅未装备）",
                ["World "] = "世界 ",
                [" (chosen)"] = "（已选）",
                [" (unselected)"] = "（未选）",
                [" (maximum)"] = "（上限）",
                ["(Reason: "] = "（原因：",
                [" ("] = "（",
                [")"] = "）",
                ["% less)"] = "%）",
                ["Expedition training"] = "远征锻炼",

                // 交換
                ["Your offer"] = "你付出的物品",
                ["You: "] = "自己：",
                ["Waiting for the trade to complete."] = "正在等待交易完成。",
                ["Relic reserved for this trade."] = "该遗物已为本次交易预留。",

                // 状態・通知で繰り返し出る文
                ["Could not save settings."] = "无法保存设置。",
                ["Only during an expedition."] = "仅限远征中。",
                ["Not connected to a game."] = "未连接到游戏。",
                ["Waiting for save to finish"] = "正在等待保存完成",
                ["Pick a material"] = "选择材料",
                ["Pick a material:"] = "选择材料：",
                ["Enter a nonnegative whole number for each material."] = "请为每种材料输入0以上的整数。",
                ["Cannot change during an expedition"] = "远征中无法更改",
                ["Cannot change during an expedition. Switching is free after returning."] = "远征中无法更改。返回后可免费切换。",
                ["The workshop is closed during expeditions."] = "远征中工坊不可用。请从远征返回后再使用。",
                ["Fusion requires a secure point or being outside an expedition"] = "融合需要在存仓点或远征之外进行",
                ["Not enough materials for fusion"] = "融合材料不足",
                ["Unrecorded difficulty"] = "难度未记录",
                ["Developer grant: "] = "开发者授予：",
                ["Choose a waypoint at a secure point."] = "可在存仓点选择路标。",
                ["Preparing to continue"] = "正在准备继续",
                ["The profile will switch after the expedition."] = "远征结束后切换档案。",
                ["Your choices are complete."] = "你的选择已完成。",
                ["Melee co-op effects"] = "近战协作效果",
                ["Melee co-op effects (while the basic attack is melee)"] = "近战协作效果（通常攻击为近战时）",
                ["Automatic casting"] = "自动施放",
            };
        }
    }
}
