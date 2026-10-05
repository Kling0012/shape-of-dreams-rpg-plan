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
            internal readonly Dictionary<long, string> Captions = new Dictionary<long, string>();
            internal void Clear() { Effects.Clear(); Captions.Clear(); }
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
                    || !BossFinite(effect.expires) || effect.expires - msg.hostTime > 60d
                    || effect.element < 0 || effect.element > (int)BossElement.Dark || effect.shape < 0 || effect.shape > (int)BossShape.Radial
                    || effect.count < 0 || effect.count > 64 || !BossFinite(effect.budget) || effect.budget < 0
                    || !BossFinite(effect.range) || effect.range < 0 || effect.range > 32
                    || !BossFinite(effect.width) || effect.width < 0 || effect.width > 32
                    || !BossFinite(effect.angle) || effect.angle < 0 || effect.angle > 360
                    || !BossFinite(effect.finalRadius) || effect.finalRadius < -1 || effect.finalRadius > 32) return;
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
            if (msg.epoch > state.Epoch || msg.snapshot) state.Clear();
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
                if (effect.removed || expires <= now) { state.Effects.Remove(effect.id); state.Captions.Remove(effect.id); }
                else if (state.Effects.ContainsKey(effect.id) || state.Effects.Count < 64)
                {
                    state.Effects[effect.id] = new DreamforgeBossEffect
                    {
                        id = effect.id, kind = effect.kind, center = effect.center, end = effect.end,
                        radius = effect.radius, due = effect.due + clockOffset, expires = expires,
                        element = effect.element, shape = effect.shape, count = effect.count, budget = effect.budget,
                        range = effect.range, width = effect.width, angle = effect.angle, finalRadius = effect.finalRadius,
                        targetNetId = effect.targetNetId,
                    };
                    state.Captions[effect.id] = effect.count > 0 && effect.budget > 0
                        ? effect.count + " / " + effect.budget.ToString("0.##")
                        : effect.budget > 0 ? effect.budget.ToString("0.##") : effect.count > 0 ? effect.count.ToString() : "";
                }
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
                    if (hero == null || !hero.isActive || !hero.isAlive || hero.isKnockedOut) state.Clear();
                }
                state.Expired.Clear();
                foreach (var effect in state.Effects)
                    if (effect.Value.expires <= now) state.Expired.Add(effect.Key);
                foreach (long id in state.Expired) { state.Effects.Remove(id); state.Captions.Remove(id); }
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
                    var center = effect.center;
                    if (effect.kind == 7 && effect.targetNetId != 0 && NetworkClient.spawned.TryGetValue(effect.targetNetId, out var shieldTarget)
                        && shieldTarget != null) center = shieldTarget.transform.position;
                    bool pending = effect.due > now;
                    GUI.color = BossEffectColor(effect.kind, effect.element, pending);
                    if (effect.kind == 5)
                    {
                        if (pending) DrawBossWorldLine(camera, effect.center, effect.end, 2f);
                        else
                        {
                            float progress = Mathf.Clamp01((float)((now - effect.due) / Math.Max(.001d, effect.expires - effect.due)));
                            var tip = Vector3.Lerp(effect.center, effect.end, progress);
                            var direction = (effect.end - effect.center).normalized;
                            DrawBossWorldLine(camera, tip - direction * .5f, tip, 4f);
                        }
                    }
                    else if (effect.shape == (int)BossShape.Fan)
                        DrawBossWorldFan(camera, effect, pending ? 2f : 4f);
                    else if (effect.shape == (int)BossShape.Line || effect.shape == (int)BossShape.Column
                        || effect.kind == 6 && effect.shape == (int)BossShape.Circle)
                        DrawBossWorldStrip(camera, effect, pending ? 2f : 4f);
                    else if (effect.shape == (int)BossShape.Radial)
                    {
                        var direction = BossVisualDirection(effect);
                        for (int i = 0; i < effect.count; i++)
                        {
                            var spoke = Quaternion.Euler(0, 360f * i / effect.count, 0) * direction;
                            var tip = effect.center + spoke * effect.range;
                            DrawBossWorldLine(camera, effect.center, tip, 2f);
                            if (effect.kind == 4)
                            {
                                bool inward = effect.angle == 180f;
                                var head = inward ? effect.center + spoke * .4f : tip;
                                var arrow = inward ? -spoke : spoke;
                                DrawBossWorldLine(camera, head, head - Quaternion.Euler(0, 35f, 0) * arrow * .35f, 2f);
                                DrawBossWorldLine(camera, head, head - Quaternion.Euler(0, -35f, 0) * arrow * .35f, 2f);
                            }
                        }
                        if (effect.kind == 4) DrawBossWorldRing(camera, effect.center, effect.radius, 2f);
                    }
                    if (effect.kind != 5 && effect.shape == (int)BossShape.Circle)
                    {
                        float progress = Mathf.Clamp01((float)((now - effect.due) / Math.Max(.001d, effect.expires - effect.due)));
                        float radius = effect.finalRadius < 0 ? effect.radius : Mathf.Lerp(effect.radius, effect.finalRadius, progress);
                        DrawBossWorldRing(camera, center, radius, pending ? 2f : 3f);
                    }
                    if (effect.kind == 2 || effect.kind == 3)
                    {
                        // A stem distinguishes timber from a missile and from the stomp's plain ring.
                        float height = pending ? .45f : 1.7f;
                        DrawBossWorldLine(camera, effect.center, effect.center + Vector3.up * height, 4f);
                        DrawBossWorldLine(camera, effect.center + Vector3.up * height * .65f,
                            effect.center + Vector3.up * height + Vector3.right * .45f, 3f);
                    }
                    if (effect.kind == 8)
                    {
                        var shoulder = effect.center + Vector3.up;
                        DrawBossWorldLine(camera, effect.center, effect.center + Vector3.up * 1.5f, 3f);
                        DrawBossWorldLine(camera, shoulder - Vector3.right * .4f, shoulder + Vector3.right * .4f, 3f);
                    }
                    if (effect.kind == 4)
                    {
                        DrawBossWorldLine(camera, effect.center - Vector3.right * .45f,
                            effect.center + Vector3.right * .45f, 2f);
                        DrawBossWorldLine(camera, effect.center - Vector3.forward * .45f,
                            effect.center + Vector3.forward * .45f, 2f);
                    }
                    if (owner.Captions.TryGetValue(effect.id, out var caption) && caption.Length > 0)
                    {
                        var at = camera.WorldToScreenPoint(center + Vector3.up * .7f);
                        if (at.z > 0) GUI.Label(new Rect(at.x - 40f, Screen.height - at.y - 10f, 100f, 22f), caption);
                    }
                }
            GUI.color = oldColor;
            GUI.matrix = oldMatrix;
        }
        private static Color BossEffectColor(int kind, int element, bool pending)
        {
            float alpha = pending ? .65f : .95f;
            if (kind == 8) alpha *= .5f;
            switch ((BossElement)element)
            {
                case BossElement.Cold: return new Color(.5f, .85f, 1f, alpha);
                case BossElement.Fire: return new Color(1f, .3f, .1f, alpha);
                case BossElement.Light: return new Color(1f, .95f, .65f, alpha);
                case BossElement.Dark: return new Color(.7f, .4f, 1f, alpha);
            }
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
        private static Vector3 BossVisualDirection(DreamforgeBossEffect effect)
        {
            var direction = effect.end - effect.center; direction.y = 0;
            return direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
        }
        private static void DrawBossWorldStrip(Camera camera, DreamforgeBossEffect effect, float width)
        {
            var direction = BossVisualDirection(effect);
            var end = effect.range > 0 ? effect.center + direction * effect.range : effect.end;
            var half = Vector3.Cross(Vector3.up, direction) * (effect.width * .5f);
            DrawBossWorldLine(camera, effect.center - half, end - half, width);
            DrawBossWorldLine(camera, effect.center + half, end + half, width);
            DrawBossWorldLine(camera, effect.center - half, effect.center + half, width);
            DrawBossWorldLine(camera, end - half, end + half, width);
        }
        private static void DrawBossWorldFan(Camera camera, DreamforgeBossEffect effect, float width)
        {
            var direction = BossVisualDirection(effect);
            const int segments = 24;
            var previous = effect.center + Quaternion.Euler(0, -effect.angle * .5f, 0) * direction * effect.range;
            DrawBossWorldLine(camera, effect.center, previous, width);
            for (int i = 1; i <= segments; i++)
            {
                var next = effect.center + Quaternion.Euler(0, -effect.angle * .5f + effect.angle * i / segments, 0) * direction * effect.range;
                DrawBossWorldLine(camera, previous, next, width); previous = next;
            }
            DrawBossWorldLine(camera, previous, effect.center, width);
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
