using System.Collections.Generic;
using SodRpg.Mod;
using UnityEngine;

// （#801）本体 NativePerfPatches.cs の NativeCursorIdlePerf のうち、抽出部分
// （ValidateBody・IndexOfLabel・Prefix・Postfix）から参照される範囲の境界。
// NativePerfOptions・NativePerfPatch・UnityEngine.Vector2・Texture2D は既存の境界
// （SleepPositionPerfBoundary.cs・NativeBoundary.cs）をそのまま使うため、ここには置かない。
namespace UnityEngine
{
    internal enum CursorMode { Auto, ForceSoftware }

    // 本体の ApplyCursor の無効分岐が呼ぶ UnityEngine.Cursor.SetCursor の境界。
    // 呼び出しの有無は ValidateBody が IL 命令で確かめるため、中身は空でよい。
    internal static class Cursor
    {
        public static void SetCursor(Texture2D texture, Vector2 hotspot, CursorMode cursorMode) { }
    }
}

namespace SodRpg.Mod
{
    // 本体 CursorManager（グローバル名前空間、public bool disableSoftwareCursor）の境界。
    // Prefix が読むのはこのフラグだけ。
    internal class CursorManager
    {
        public bool disableSoftwareCursor;
    }

    // 本体 DewSave.platformSettings の先の実体。実物はどちらも public フィールドで、
    // ValidateBody はガードの読み出しを ldsfld DewSave::platformSettings → ldfld gameplay
    // → ldfld disableSoftwareCursor の並びで検証するため、境界も同じ並びにする。
    internal class DewPlatformSettings
    {
        public DewGameplaySettings_Platform gameplay = new DewGameplaySettings_Platform();
    }

    internal class DewGameplaySettings_Platform
    {
        public bool disableSoftwareCursor;
    }
}

namespace Issue73.Native.Tests
{
    // 本体 CursorManager.ApplyCursor の無効分岐（ガード・リセット・早期 return）と同じ
    // 読み出し・呼び出し一式の見本。ValidateBody は先頭から最初の ret までの命令列
    // （引数読み出し・インスタンスフラグ・brtrue・プラットフォーム設定の3読み出し・
    // brfalse・ldnull・Vector2.zero・CursorMode.Auto・SetCursor・ret）と両分岐の飛び先を
    // 検証するため、拒否の見本は (1) ガードが index 引数も読む形（将来の本体変更の例）、
    // (2) 無効分岐がほかのメンバーも書く形、(3) ガードがプラットフォーム設定を欠く形、
    // (4) 読み出しの順を入れ替えた形。
    // 見本の IL は実 DLL の ApplyCursor と同じ並びに書いた（2026-10-19 に Dew.Core.dll を
    // ilspycmd で読んで確認。ldarg / ldfld CursorManager::disableSoftwareCursor / brtrue.s /
    // ldsfld DewSave::platformSettings / ldfld gameplay / ldfld disableSoftwareCursor /
    // brfalse.s / ldnull / call Vector2::get_zero / ldc.i4.0 / call Cursor::SetCursor / ret）。
    internal sealed class CursorIlShapes
    {
        private readonly List<int> _touchedIndexes = new List<int>();
        private Texture2D[] _resizedTextures;

        // 本体と同じガードとリセット（有効経路の末尾は検証対象外なので何でもよい）→ 受理。
        internal void SameShape(CursorManager instance, int index)
        {
            if (instance.disableSoftwareCursor || DewSave.platformSettings.gameplay.disableSoftwareCursor)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                return;
            }
            if (_resizedTextures == null) _touchedIndexes.Add(index);
        }

        // ガードが index 引数も読む（引数で分岐が増えた将来の本体の例）→ 拒否。
        internal void GuardAlsoReadsTheIndexArgument(CursorManager instance, int index)
        {
            if (index == 0 && (instance.disableSoftwareCursor || DewSave.platformSettings.gameplay.disableSoftwareCursor))
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                return;
            }
            if (_resizedTextures == null) _touchedIndexes.Add(index);
        }

        // 無効分岐がリセットのほかにメンバーへ書く（副作用が増えた将来の本体の例）→ 拒否。
        internal void DisabledBranchAlsoWritesAnotherMember(CursorManager instance, int index)
        {
            if (instance.disableSoftwareCursor || DewSave.platformSettings.gameplay.disableSoftwareCursor)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                instance.disableSoftwareCursor = false;
                return;
            }
            if (_resizedTextures == null) _touchedIndexes.Add(index);
        }

        // ガードがプラットフォーム設定の読み出しを欠く（条件が狭まった将来の本体の例）→ 拒否。
        internal void GuardMissesThePlatformSetting(CursorManager instance, int index)
        {
            if (instance.disableSoftwareCursor)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                return;
            }
            if (_resizedTextures == null) _touchedIndexes.Add(index);
        }

        // 読み出しの順を入れ替えた（意味は同じだが出荷時の形と違う例）→ 拒否。
        internal void SwappedGuardOperandOrder(CursorManager instance, int index)
        {
            if (DewSave.platformSettings.gameplay.disableSoftwareCursor || instance.disableSoftwareCursor)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                return;
            }
            if (_resizedTextures == null) _touchedIndexes.Add(index);
        }
    }
}
