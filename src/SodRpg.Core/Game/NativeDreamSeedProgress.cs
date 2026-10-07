using System.Globalization;

namespace SodRpg.Core.Game
{
    /// <summary>芽吹きの種の、本体の文字列辞書へ保存する撃破数。発動前は0〜4。</summary>
    public static class NativeDreamSeedProgress
    {
        public const string Key = "Dreamforge.NativeDream.Seeds.v1";

        public static string OnKill(string saved, out bool heal)
        {
            if (!int.TryParse(saved, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 0 || count > 4)
                count = 0;
            heal = count == 4;
            return heal ? "0" : (count + 1).ToString(CultureInfo.InvariantCulture);
        }
    }
}
