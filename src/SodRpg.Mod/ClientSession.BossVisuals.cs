using System;
using System.Collections.Generic;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private sealed class BossOwnerVisual
        {
            internal long Epoch, EquipmentEpoch, Revision = -1;
            internal readonly Dictionary<long, DreamforgeBossEffect> Effects = new Dictionary<long, DreamforgeBossEffect>();
            internal readonly List<long> Expired = new List<long>();
        }
        private readonly Dictionary<uint, BossOwnerVisual> _bossOwnerVisuals = new Dictionary<uint, BossOwnerVisual>();
        private Action<DreamforgeBossEffectsMsg> _onBossEffects;
        private string _bossVisualRun;
        private int _bossVisualZone = -1, _bossVisualRoom = -1;

        private void RegisterBossVisuals(Actor actor)
        {
            if (_onBossEffects == null) _onBossEffects = OnBossEffects;
            actor.CustomRpc_RegisterClientMessageHandler<DreamforgeBossEffectsMsg>(_onBossEffects);
        }
        private void UnregisterBossVisuals(Actor actor)
        {
            if (_onBossEffects != null)
                actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeBossEffectsMsg>(_onBossEffects);
        }
        private void ClearBossDisplay()
        {
            _bossOwnerVisuals.Clear();
            _bossVisualRun = null;
            _bossVisualZone = _bossVisualRoom = -1;
        }
        private void RefreshBossDisplayRoom()
        {
            string run = NetworkedManagerBase<GameManager>.softInstance?.runId;
            int zone = _zone?.currentZoneIndex ?? -1, room = _zone?.currentRoomIndex ?? -1;
            if (run == _bossVisualRun && zone == _bossVisualZone && room == _bossVisualRoom) return;
            ClearBossDisplay();
            _bossVisualRun = run;
            _bossVisualZone = zone;
            _bossVisualRoom = room;
        }
        private static bool BossFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool BossFinite(Vector3 v) => BossFinite(v.x) && BossFinite(v.y) && BossFinite(v.z);
        private static bool BossFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private void OnBossEffects(DreamforgeBossEffectsMsg msg)
        {
            if (msg == null || !MechanismHandshakeAccepted
                || !ContentFingerprint.Matches(msg.protocol, msg.content, Protocol.Version)
                || msg.ownerNetId == 0 || msg.epoch <= 0 || msg.revision < 0 || msg.equipmentEpoch < 0
                || !BossFinite(msg.hostTime) || !BossFinite(msg.sentAt)
                || msg.effects == null || msg.effects.Length > 64) return;
            RefreshBossDisplayRoom();
            if (string.IsNullOrEmpty(_bossVisualRun) || msg.runId != _bossVisualRun
                || msg.zone != _bossVisualZone || msg.room != _bossVisualRoom
                || !ObserveMonsterAuthority(msg.authorityGeneration)) return;
            // ObserveMonsterAuthority may clear displays on a new host lifetime.
            RefreshBossDisplayRoom();
            for (int i = 0; i < msg.effects.Length; i++)
            {
                var effect = msg.effects[i];
                if (effect == null || effect.id <= 0 || effect.kind < 1 || effect.kind > 9
                    || !BossFinite(effect.center) || !BossFinite(effect.end) || !BossFinite(effect.radius)
                    || effect.radius < 0f || effect.radius > 32f || !BossFinite(effect.due)
                    || !BossFinite(effect.expires) || effect.expires - msg.hostTime > 60d) return;
                for (int j = 0; j < i; j++) if (msg.effects[j].id == effect.id) return;
            }
            if (!_bossOwnerVisuals.TryGetValue(msg.ownerNetId, out var state))
            {
                if (_bossOwnerVisuals.Count >= 64) return;
                state = new BossOwnerVisual();
                _bossOwnerVisuals.Add(msg.ownerNetId, state);
            }
            if (msg.epoch < state.Epoch || msg.epoch == state.Epoch && msg.revision < state.Revision) return;
            if (msg.epoch == state.Epoch && msg.equipmentEpoch != state.EquipmentEpoch) return;
            if (msg.epoch > state.Epoch || msg.snapshot) state.Effects.Clear();
            else if (msg.revision == state.Revision) return;
            state.Epoch = msg.epoch;
            state.EquipmentEpoch = msg.equipmentEpoch;
            state.Revision = msg.revision;
            double now = Time.time;
            double estimatedHostTime = msg.hostTime + Math.Max(0d, NetworkTime.time - msg.sentAt) * Time.timeScale;
            double clockOffset = now - estimatedHostTime;
            foreach (var effect in msg.effects)
            {
                double expires = effect.expires + clockOffset;
                if (effect.removed || expires <= now) state.Effects.Remove(effect.id);
                else if (state.Effects.ContainsKey(effect.id) || state.Effects.Count < 64)
                    state.Effects[effect.id] = new DreamforgeBossEffect
                    {
                        id = effect.id, kind = effect.kind, center = effect.center, end = effect.end,
                        radius = effect.radius, due = effect.due + clockOffset, expires = expires,
                    };
            }
        }

        private void TickBossDisplay()
        {
            RefreshBossDisplayRoom();
            double now = Time.time;
            foreach (var pair in _bossOwnerVisuals)
            {
                var state = pair.Value;
                if (NetworkClient.spawned.TryGetValue(pair.Key, out var identity) && identity != null)
                {
                    var hero = identity.GetComponent<Hero>();
                    if (hero == null || !hero.isActive || !hero.isAlive || hero.isKnockedOut) state.Effects.Clear();
                }
                state.Expired.Clear();
                foreach (var effect in state.Effects)
                    if (effect.Value.expires <= now) state.Expired.Add(effect.Key);
                foreach (long id in state.Expired) state.Effects.Remove(id);
            }
        }

        internal void DrawBossEffects()
        {
            if (Event.current.type != EventType.Repaint || _bossOwnerVisuals.Count == 0) return;
            var camera = Camera.main;
            if (camera == null) return;
            var oldColor = GUI.color;
            var oldMatrix = GUI.matrix;
            // This world projection must not inherit the inventory UI's logical-canvas transform.
            GUI.matrix = Matrix4x4.identity;
            double now = Time.time;
            foreach (var owner in _bossOwnerVisuals.Values)
                foreach (var effect in owner.Effects.Values)
                {
                    if (effect.expires <= now) continue;
                    bool pending = effect.due > now;
                    GUI.color = BossEffectColor(effect.kind, pending);
                    if (effect.kind == 5)
                    {
                        float progress = Mathf.Clamp01((float)((now - effect.due) / Math.Max(.001d, effect.expires - effect.due)));
                        var tip = Vector3.Lerp(effect.center, effect.end, progress);
                        var direction = (effect.end - effect.center).normalized;
                        DrawBossWorldLine(camera, tip - direction * .5f, tip, 4f);
                    }
                    else if (effect.kind == 6)
                        DrawBossWorldLine(camera, effect.center, effect.end, pending ? 2f : 4f);
                    if (effect.kind != 5)
                        DrawBossWorldRing(camera, effect.center, effect.radius, pending ? 2f : 3f);
                    if (effect.kind == 2 || effect.kind == 3)
                    {
                        // A stem distinguishes timber from a missile and from the stomp's plain ring.
                        float height = pending ? .45f : 1.7f;
                        DrawBossWorldLine(camera, effect.center, effect.center + Vector3.up * height, 4f);
                        DrawBossWorldLine(camera, effect.center + Vector3.up * height * .65f,
                            effect.center + Vector3.up * height + Vector3.right * .45f, 3f);
                    }
                    if (effect.kind == 4)
                    {
                        DrawBossWorldLine(camera, effect.center - Vector3.right * .45f,
                            effect.center + Vector3.right * .45f, 2f);
                        DrawBossWorldLine(camera, effect.center - Vector3.forward * .45f,
                            effect.center + Vector3.forward * .45f, 2f);
                    }
                }
            GUI.color = oldColor;
            GUI.matrix = oldMatrix;
        }
        private static Color BossEffectColor(int kind, bool pending)
        {
            switch (kind)
            {
                case 2: return new Color(.2f, 1f, .35f, pending ? .65f : 1f);
                case 3: return new Color(.25f, .65f, .15f, pending ? .6f : 1f);
                case 4: return new Color(1f, .65f, .15f, pending ? .7f : 1f);
                case 5: return new Color(.25f, 1f, .65f, .95f);
                case 7: return new Color(.35f, .75f, 1f, .8f);
                default: return new Color(.75f, .95f, .35f, pending ? .65f : 1f);
            }
        }
        private static void DrawBossWorldRing(Camera camera, Vector3 center, float radius, float width)
        {
            const int segments = 32;
            Vector3 previous = center + Vector3.right * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / segments);
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, .03f, Mathf.Sin(angle) * radius);
                DrawBossWorldLine(camera, previous, next, width);
                previous = next;
            }
        }
        private static void DrawBossWorldLine(Camera camera, Vector3 from, Vector3 to, float width)
        {
            Vector3 a = camera.WorldToScreenPoint(from), b = camera.WorldToScreenPoint(to);
            if (a.z <= 0f || b.z <= 0f) return;
            var start = new Vector2(a.x, Screen.height - a.y);
            var difference = new Vector2(b.x - a.x, a.y - b.y);
            float length = difference.magnitude;
            if (length < .1f) return;
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y - width * .5f, length, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;
        }
    }
}
