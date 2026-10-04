using System.Collections.Generic;

namespace SodRpg.Core.Game
{
    /// <summary>
    /// 星図・装備の操作でプレイヤーに見える拒否メッセージの表。
    /// 画面の赤い警告にそのまま出るので、必ず日本語と英語を両方持つ（<see cref="All"/> を全件検査するテストがある）。
    /// </summary>
    public static class RuleMessages
    {
        public static readonly Txt RegistryChanged = new Txt(
            "払い戻しの確認のあとで、星・装備・仕掛けの内容が変わりました。もう一度確認してください。",
            "The allocation, equipment or shared mechanism registry changed after this refund preview. Preview again.");
        public static readonly Txt NotEnoughStarPoints = new Txt(
            "この振り方には、星のポイントが足りません。",
            "Not enough star points for the proposed allocation.");
        public static readonly Txt RelicNotInStash = new Txt(
            "その遺物は保管庫にありません。",
            "The relic is not in the stash.");
        public static readonly Txt RelicWrongSlot = new Txt(
            "その遺物は、この装備枠には入れられません。",
            "The relic does not belong to that slot.");
        public static readonly Txt KeystoneNotReady = new Txt(
            "この刻印はまだ選べません。取得済みの星とのつながり、取得した段数、熟練度の条件を満たしてください。",
            "This keystone is not available yet: connect it to your acquired stars and meet the rank and mastery requirements.");
        public static readonly Txt UnknownStar = new Txt(
            "この旅人の星図にない星です。",
            "That star is not in this Traveler's tree.");
        public static readonly Txt KeystoneNotAllocated = new Txt(
            "その刻印は選ばれていません。",
            "That keystone is not selected.");
        public static readonly Txt StarNotAllocated = new Txt(
            "その星はまだ取得していません。",
            "That star is not acquired.");
        public static readonly Txt UseKeystoneChange = new Txt(
            "刻印は、刻印の選択操作で変更してください。",
            "Use the keystone selection to change a keystone.");
        public static readonly Txt AlreadyMaxRank = new Txt(
            "これ以上は上げられません（最大段です）。",
            "Already at max rank.");
        public static readonly Txt NeedConnectedStars = new Txt(
            "先に、線でつながっている手前の星を取得してください。",
            "Allocate the connected prerequisite stars first.");
        public static readonly Txt ChoiceNotAllocated = new Txt(
            "その選択の星は、まだ取得していません。",
            "That choice star is not allocated.");
        public static readonly Txt SelectOneEffect = new Txt(
            "二つの効果のうち、1つを選んでください。",
            "Explicitly select one of the two effects.");
        public static readonly Txt SameOptionOnly = new Txt(
            "選択の星は、全ての段で同じ効果になります。先に効果を切り替えてください。",
            "All ranks of a choice must use the same option. Change the choice explicitly first.");
        public static readonly Txt NotChoiceStar = new Txt(
            "この星には選択肢がありません。",
            "That is not a choice star.");
        public static readonly Txt MissingDisableRule = new Txt(
            "星の無効化ルールが見つかりません（内部エラー）。",
            "Missing permanent-disable rule.");
        public static readonly Txt UnknownStarId = new Txt(
            "星の定義が見つかりません（内部エラー）：",
            "Unknown star: ");
        public static readonly Txt NotEnoughMaterial = new Txt(
            "素材が足りません：",
            "Not enough materials: ");

        /// <summary>表の全件。日本語に仮名か漢字が含まれ、英語が空でないことを検査する。</summary>
        public static IReadOnlyList<Txt> All { get; } = new List<Txt>
        {
            RegistryChanged, NotEnoughStarPoints, RelicNotInStash, RelicWrongSlot, KeystoneNotReady, UnknownStar,
            KeystoneNotAllocated, StarNotAllocated, UseKeystoneChange, AlreadyMaxRank, NeedConnectedStars,
            ChoiceNotAllocated, SelectOneEffect, SameOptionOnly, NotChoiceStar, MissingDisableRule, UnknownStarId,
            NotEnoughMaterial,
        }.AsReadOnly();
    }
}
