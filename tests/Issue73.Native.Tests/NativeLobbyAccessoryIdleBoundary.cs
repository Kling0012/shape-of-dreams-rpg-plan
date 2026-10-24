using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SodRpg.Mod;

// （#833）NativeLobbyAccessoryIdle.cs（リンク）を Issue73.Native.Tests で動かすための本体 API 境界。
// DewPlayer・DewNetworkManager は NativeBoundary.cs、CharacterModelDisplay は
// CharacterModelDisplayBoundsBoundary.cs、DreamforgeConfig は NativeEmbracePositionBoundary.cs の定義を使う。
namespace Mirror
{
    // 本体 Mirror SyncList<T> の境界。NativeLobbyAccessoryIdle（リンク）と PlayLobby_Character の
    // 境界は Count・インデクサ・要素の列挙だけを使う。
    internal partial class SyncList<T> : IEnumerable<T>
    {
        private readonly List<T> items = new List<T>();

        public int Count => items.Count;

        public T this[int index] { get => items[index]; set => items[index] = value; }

        public void Add(T item) => items.Add(item);

        public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

// 本体 Dew.Core DewNetworkStartSettings の境界。continueData は実物では public フィールド
// （DewPersistence.GameData）。フェイルソフトの検証で例外を注入できるよう自動プロパティにする。
internal class DewNetworkStartSettings
{
    public virtual DewPersistence.GameData continueData { get; set; }
}

// 本体 Dew.Core Skin.GetDefaultSkin（r1.4.0.13 逆コンパイル）: heroType の先頭 "Hero_" を取り、
// "Skin_" と "_Default" で挟む。
internal static class Skin
{
    public static string GetDefaultSkin(string heroType) => "Skin_" + heroType.Substring(5) + "_Default";
}

namespace SodRpg.Mod
{
    // 本体 Dew.Core PlayLobby_Character.LogicUpdate（r1.4.0.13 逆コンパイル）の転写。
    // 等価性テストの「パッチ無効側」として動く: NativeLobbyAccessoryIdle の Prefix が true を
    // 返したときにこの本体が走り、Postfix がその後の状態を記録する。
    // 本体から省いたもの: model.transform.localRotation = Quaternion.identity（skinType が変わった
    // Tick の初期向き。この模倣の Transform は回転を持たないため、向きのリセットは
    // characterRotation のクリアだけを再現する）と ToListNonAlloc/handle.Return（プール確保と
    // 返却で、model の状態には影響しないため ToList に畳む）。書き換える出力
    // （model.skinType・model.accessories への新しい List の代入・UpdateAccessories の呼び出し）は
    // 本体と同じ式で書く。
    internal class PlayLobby_Character : LogicBehaviour
    {
        public int playerIndex;

        public string forceSetup = "";

        public CharacterModelDisplay model;

        public float? characterRotation;

        public override void LogicUpdate(float dt)
        {
            base.LogicUpdate(dt);
            string text = forceSetup;
            if (forceSetup == "" && playerIndex < DewPlayer.lobbyPlayers.Count && playerIndex >= 0 && DewPlayer.lobbyPlayers[playerIndex].isEveryInfoSet)
            {
                DewPlayer dewPlayer = DewPlayer.lobbyPlayers[playerIndex];
                text = (dewPlayer.IsAllowedToUseItem(dewPlayer.selectedSkin) ? dewPlayer.selectedSkin : Skin.GetDefaultSkin(dewPlayer.selectedHeroType));
            }
            if (DewNetworkManager.startSettings.continueData != null)
            {
                text = "";
            }
            if (model.skinType != text)
            {
                model.skinType = text;
                characterRotation = null;
            }
            if (playerIndex < DewPlayer.lobbyPlayers.Count && playerIndex >= 0)
            {
                DewPlayer p = DewPlayer.lobbyPlayers[playerIndex];
                List<string> list = p.selectedAccessories.Where((string a) => p.IsAllowedToUseItem(a)).ToList();
                if (!model.accessories.SequenceEqual(list))
                {
                    model.accessories = list.ToList();
                    model.UpdateAccessories();
                }
            }
        }
    }
}
