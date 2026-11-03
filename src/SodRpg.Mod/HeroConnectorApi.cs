using System;

namespace Dreamforge.Api
{
    /// <summary>
    /// ヒーローコネクタのランタイムAPI（docs/specs/hero-connector.md §8）。
    /// JSONだけで足りる作者は使わなくてよい。名前と引数は apiVersion の間は変えない（§11）。
    /// どのメンバーも例外を外へ投げない（中で受け止めて false や空を返し、警告1回）。
    /// 呼び出し側はアセンブリを走査してこの型を探す（本体がアセンブリ名に時刻印を付けるため、ビルド時の参照は不要）。
    /// </summary>
    public static class HeroConnector
    {
        /// <summary>このAPIの版。仕様 apiVersion 1。</summary>
        public static int ApiVersion
        {
            get { try { return SodRpg.Core.Game.ExtendedHeroes.ApiVersion; } catch (Exception) { return 1; } }
        }

        /// <summary>Dreamforge の版（metadata.json の modVer）。</summary>
        public static string DreamforgeVersion
        {
            get { try { return SodRpg.Mod.HostAuthority.ModVersion; } catch (Exception) { return "?"; } }
        }

        /// <summary>
        /// §3-2 と同じJSONの宣言を登録する。戻り値は全旅人が受理されたか。拒否があれば false になり、
        /// <paramref name="error"/> に最初の理由を返す（部分受理の分は登録簿に残る）。
        /// 遠征中は保留して true を返し、<paramref name="error"/> に保留の旨を入れる。
        /// </summary>
        public static bool RegisterHeroes(string json, string sourceModId, out string error)
        {
            try { return SodRpg.Mod.DreamforgeMod.RegisterHeroesThroughApi(json, sourceModId, out error); }
            catch (Exception ex)
            {
                SodRpg.Mod.Log.Warn("[Dreamforge/Connector] RegisterHeroes failed: " + ex.Message);
                error = "unexpected failure: " + ex.Message;
                return false;
            }
        }

        /// <summary>登録簿にあるか。</summary>
        public static bool IsHeroRegistered(string heroKey)
        {
            try { return SodRpg.Core.Game.ExtendedHeroes.IsRegistered(heroKey); }
            catch (Exception) { return false; }
        }

        /// <summary>拡張旅人のキーの一覧。</summary>
        public static string[] GetRegisteredHeroes()
        {
            try { return SodRpg.Mod.DreamforgeMod.RegisteredHeroKeys(); }
            catch (Exception) { return new string[0]; }
        }

        /// <summary>§7 と同じ内容（報告ファイル）の文字列。</summary>
        public static string GetReport()
        {
            try { return SodRpg.Mod.DreamforgeMod.GetConnectorReport(); }
            catch (Exception) { return ""; }
        }
    }
}
