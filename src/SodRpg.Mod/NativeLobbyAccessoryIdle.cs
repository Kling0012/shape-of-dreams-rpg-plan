using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;

namespace SodRpg.Mod
{
    // PlayLobby_Character.LogicUpdate (30 Hz logic tick, one instance per lobby player)
    // rebuilds the filtered accessory list from scratch every tick:
    // p.selectedAccessories.Where(p.IsAllowedToUseItem).ToListNonAlloc + SequenceEqual
    // against model.accessories. That costs every tick one captured-lambda closure, one
    // delegate, one Where iterator and two boxed List<string>.Enumerator instances inside
    // Enumerable.SequenceEqual, plus an IsAllowedToUseItem call per accessory (StartsWith
    // chain, DewResources dictionary probe, O(n) ownedItems.Contains) - even though the
    // skin and accessory set only change when the player picks something.
    //
    // The native body is provably a no-op whenever every input it reads matches the frame
    // the native code last ran on: the skin name comes only from forceSetup / the indexed
    // DewPlayer (isEveryInfoSet, selectedSkin, selectedHeroType) / startSettings.continueData,
    // the accessory filter only from selectedAccessories' element references plus
    // IsAllowedToUseItem, whose only mutable inputs are the ownedItems element references
    // (the DewResources name dictionary and Accessory.generatedFromServer are fixed after
    // startup - nothing in the decompiled tree writes them at runtime, and this mod does
    // not register resources). The prefix skips the tick only when all of those inputs,
    // plus the model fields the native body would have assigned (skinType value and the
    // accessories list reference), still match; anything else runs the native code and the
    // postfix re-records the inputs. Element comparison uses reference equality on purpose:
    // strings are immutable, so a reference match proves the value, while a mismatch (e.g.
    // a fresh string arriving over the network with the same content) merely falls back to
    // the native code - never the other way round.
    internal static class NativeLobbyAccessoryIdle
    {
        private static readonly string[] EmptyRefs = Array.Empty<string>();

        private sealed class State
        {
            internal bool Skip;
            internal string ForceSetup;
            internal int PlayerIndex;
            internal int PlayerCount;
            internal DewPlayer Player;
            internal bool EveryInfoSet;
            internal string SelectedSkin;
            internal string SelectedHeroType;
            internal DewNetworkStartSettings StartSettings;
            internal object ContinueData;
            internal string SkinType;
            internal List<string> CurrentAccessories;
            internal SyncList<string> SelectedList;
            internal string[] SelectedRefs;
            internal List<string> Owned;
            internal string[] OwnedRefs;
        }

        private static readonly ConditionalWeakTable<PlayLobby_Character, State> States =
            new ConditionalWeakTable<PlayLobby_Character, State>();
        private static readonly ConditionalWeakTable<PlayLobby_Character, State>.CreateValueCallback CreateState =
            _ => new State { SelectedRefs = EmptyRefs, OwnedRefs = EmptyRefs };

        private static bool _disabled;
        private static bool _warned;

        // Returns true when the native LogicUpdate would do nothing this tick.
        private static bool ShouldSkip(PlayLobby_Character instance)
        {
            if (instance.model == null) return false;
            if (!States.TryGetValue(instance, out State s) || !s.Skip) return false;
            if (s.ForceSetup != instance.forceSetup) return false;
            if (s.PlayerIndex != instance.playerIndex) return false;
            if (instance.model.skinType != s.SkinType) return false;
            if (instance.model.accessories != s.CurrentAccessories) return false;
            List<DewPlayer> players = DewPlayer.lobbyPlayers;
            if (s.PlayerCount != players.Count) return false;
            DewPlayer p = s.PlayerIndex >= 0 && s.PlayerIndex < players.Count ? players[s.PlayerIndex] : null;
            if (p != s.Player) return false;
            DewNetworkStartSettings startSettings = DewNetworkManager.startSettings;
            if (startSettings != s.StartSettings) return false;
            if ((startSettings != null ? startSettings.continueData : null) != s.ContinueData) return false;
            if (p == null) return true; // the skin/accessory section is guarded by the same index check natively
            if (p.isEveryInfoSet != s.EveryInfoSet) return false;
            if (p.selectedSkin != s.SelectedSkin) return false;
            if (p.selectedHeroType != s.SelectedHeroType) return false;
            if (!SameList(p.selectedAccessories, s.SelectedList, s.SelectedRefs)) return false;
            if (!SameList(p.ownedItems, s.Owned, s.OwnedRefs)) return false;
            return true;
        }

        private static bool SameList(List<string> list, List<string> knownList, string[] refs)
        {
            if (list != knownList) return false;
            if (list == null) return refs.Length == 0;
            if (list.Count != refs.Length) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (!ReferenceEquals(list[i], refs[i])) return false;
            }
            return true;
        }

        private static bool SameList(SyncList<string> list, SyncList<string> knownList, string[] refs)
        {
            if (list != knownList) return false;
            if (list == null) return refs.Length == 0;
            if (list.Count != refs.Length) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (!ReferenceEquals(list[i], refs[i])) return false;
            }
            return true;
        }

        private static string[] Snapshot(List<string> list)
        {
            if (list == null || list.Count == 0) return EmptyRefs;
            string[] refs = new string[list.Count];
            for (int i = 0; i < refs.Length; i++) refs[i] = list[i];
            return refs;
        }

        private static string[] Snapshot(SyncList<string> list)
        {
            if (list == null || list.Count == 0) return EmptyRefs;
            string[] refs = new string[list.Count];
            for (int i = 0; i < refs.Length; i++) refs[i] = list[i];
            return refs;
        }

        private static void Record(PlayLobby_Character instance)
        {
            State s = States.GetValue(instance, CreateState);
            s.Skip = true;
            s.ForceSetup = instance.forceSetup;
            s.PlayerIndex = instance.playerIndex;
            s.SkinType = instance.model != null ? instance.model.skinType : null;
            s.CurrentAccessories = instance.model != null ? instance.model.accessories : null;
            List<DewPlayer> players = DewPlayer.lobbyPlayers;
            s.PlayerCount = players.Count;
            DewNetworkStartSettings startSettings = DewNetworkManager.startSettings;
            s.StartSettings = startSettings;
            s.ContinueData = startSettings != null ? startSettings.continueData : null;
            DewPlayer p = s.PlayerIndex >= 0 && s.PlayerIndex < players.Count ? players[s.PlayerIndex] : null;
            s.Player = p;
            s.EveryInfoSet = p != null && p.isEveryInfoSet;
            s.SelectedSkin = p != null ? p.selectedSkin : null;
            s.SelectedHeroType = p != null ? p.selectedHeroType : null;
            if (p == null)
            {
                s.SelectedList = null;
                s.SelectedRefs = EmptyRefs;
                s.Owned = null;
                s.OwnedRefs = EmptyRefs;
                return;
            }
            s.SelectedList = p.selectedAccessories;
            s.SelectedRefs = Snapshot(p.selectedAccessories);
            s.Owned = p.ownedItems;
            s.OwnedRefs = Snapshot(p.ownedItems);
        }

        internal static bool BeforeLogicUpdate(PlayLobby_Character instance)
        {
            if (_disabled || !NativePerfOptions.LobbyAccessoryIdle) return true;
            try
            {
                if (ShouldSkip(instance))
                {
                    States.GetValue(instance, CreateState).Skip = true;
                    return false;
                }
                if (instance.model != null) States.GetValue(instance, CreateState).Skip = false;
                return true;
            }
            catch (Exception ex)
            {
                Disable(ex);
                return true;
            }
        }

        // Runs after the native body only when it actually ran (a skipped prefix still gets
        // the postfix, so the Skip flag distinguishes them). The recorded inputs are read
        // after the native assignments, so model.skinType/model.accessories describe the
        // settled state the next skip decision compares against.
        internal static void AfterLogicUpdate(PlayLobby_Character instance)
        {
            if (_disabled || !NativePerfOptions.LobbyAccessoryIdle) return;
            try
            {
                if (States.TryGetValue(instance, out State s) && s.Skip) return;
                Record(instance);
            }
            catch (Exception ex)
            {
                Disable(ex);
            }
        }

        internal static void Disable(Exception ex)
        {
            _disabled = true;
            NativePerfPatch.Warn(ref _warned, "lobby accessory idle", ex.Message);
        }
    }

    [HarmonyPatch]
    internal static class NativeLobbyAccessoryIdlePatch
    {
        private static MethodBase _target;
        private static bool _warned;

        private static bool Prepare()
        {
            _target = NativePerfPatch.Find(typeof(PlayLobby_Character), "LogicUpdate",
                new[] { typeof(float) }, ref _warned);
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        private static bool Prefix(PlayLobby_Character __instance) =>
            NativeLobbyAccessoryIdle.BeforeLogicUpdate(__instance);

        private static void Postfix(PlayLobby_Character __instance) =>
            NativeLobbyAccessoryIdle.AfterLogicUpdate(__instance);
    }
}
