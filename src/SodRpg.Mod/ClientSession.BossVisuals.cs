using System;
using Mirror;
using SodRpg.Core.Game;
using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed partial class ClientSession
    {
        private const int BossDisplayOwnerLimit = 16, BossDisplayEffectLimit = 64;
        private const int BossDisplayRenderLimit = 64, BossDisplaySegmentLimit = 2048;
        private const float BossDisplayDistance = 64f;

        private sealed class BossEffectVisual
        {
            internal readonly DreamforgeBossEffect Effect = new DreamforgeBossEffect();
            internal readonly byte[] Caption = new byte[64];
            internal int CaptionLength, CaptionCount = -1;
            internal float CaptionBudget = -1;
        }
        private sealed class BossOwnerVisual
        {
            internal uint NetId;
            internal long Epoch, EquipmentEpoch, Revision = -1;
            internal float LastMessage;
            internal int Count;
            internal readonly BossEffectVisual[] Effects = new BossEffectVisual[BossDisplayEffectLimit];
            internal BossOwnerVisual()
            {
                for (int i = 0; i < Effects.Length; i++) Effects[i] = new BossEffectVisual();
            }
            internal int Find(long id)
            {
                for (int i = 0; i < Count; i++) if (Effects[i].Effect.id == id) return i;
                return -1;
            }
            internal void RemoveAt(int index)
            {
                var free = Effects[index];
                int last = --Count;
                Effects[index] = Effects[last]; Effects[last] = free;
                free.Effect.expires = 0;
            }
            internal void Clear()
            {
                for (int i = 0; i < Count; i++) Effects[i].Effect.expires = 0;
                Count = 0;
            }
            internal void Release()
            {
                Clear(); NetId = 0; Epoch = EquipmentEpoch = 0; Revision = -1; LastMessage = 0;
            }
        }
        private readonly BossOwnerVisual[] _bossOwnerVisuals = CreateBossOwnerVisuals();
        private readonly BossEffectVisual[] _bossRenderVisuals = new BossEffectVisual[BossDisplayRenderLimit];
        private readonly float[] _bossRenderDistances = new float[BossDisplayRenderLimit];
        private readonly Plane[] _bossVisualPlanes = new Plane[6];
        private static readonly GUIContent[] BossCaptionGlyphs = CreateBossCaptionGlyphs();
        private Action<DreamforgeBossEffectsMsg> _onBossEffects;
        private Action<DreamforgeBossLiteMsg> _onBossLite;
        private readonly DreamforgeBossEffectsMsg _bossLiteDecoded = new DreamforgeBossEffectsMsg();
        private readonly DreamforgeBossEffect[] _bossLiteEffects = CreateBossLiteEffects();
        private readonly DreamforgeBossEffect[][] _bossLiteBatches = CreateBossLiteBatches();

        private static DreamforgeBossEffect[] CreateBossLiteEffects()
        {
            var effects = new DreamforgeBossEffect[BossDisplayEffectLimit];
            for (int i = 0; i < effects.Length; i++) effects[i] = new DreamforgeBossEffect();
            return effects;
        }
        private static DreamforgeBossEffect[][] CreateBossLiteBatches()
        {
            var batches = new DreamforgeBossEffect[BossDisplayEffectLimit + 1][];
            for (int i = 0; i < batches.Length; i++) batches[i] = new DreamforgeBossEffect[i];
            return batches;
        }
        private void OnBossLite(DreamforgeBossLiteMsg source)
        {
            if (source == null || source.f == null || source.f.Length > BossDisplayEffectLimit) return;
            var msg = _bossLiteDecoded;
            msg.protocol = source.p; msg.content = source.c; msg.runId = source.r;
            msg.authorityGeneration = source.a; msg.ownerNetId = source.o;
            msg.zone = source.z; msg.room = source.n; msg.epoch = source.e; msg.revision = source.v;
            msg.equipmentEpoch = source.q; msg.snapshot = source.s; msg.hostTime = source.t; msg.sentAt = source.u;
            msg.effects = _bossLiteBatches[source.f.Length];
            for (int i = 0; i < source.f.Length; i++)
            {
                if (source.f[i] == null) return;
                source.f[i].CopyTo(_bossLiteEffects[i]);
                msg.effects[i] = _bossLiteEffects[i];
            }
            OnBossEffects(msg);
        }
        private string _bossVisualRun;
        private int _bossVisualZone = -1, _bossVisualRoom = -1, _bossRenderCount, _bossSegmentsLeft;
        private float _nextBossDisplayPrune, _nextBossRenderRefresh;

        private static BossOwnerVisual[] CreateBossOwnerVisuals()
        {
            var owners = new BossOwnerVisual[BossDisplayOwnerLimit];
            for (int i = 0; i < owners.Length; i++) owners[i] = new BossOwnerVisual();
            return owners;
        }
        private static GUIContent[] CreateBossCaptionGlyphs()
        {
            return new[] { new GUIContent("0"), new GUIContent("1"), new GUIContent("2"), new GUIContent("3"),
                new GUIContent("4"), new GUIContent("5"), new GUIContent("6"), new GUIContent("7"),
                new GUIContent("8"), new GUIContent("9"), new GUIContent("."), new GUIContent(" "), new GUIContent("/") };
        }
        private static void BossAppendNumber(BossEffectVisual visual, double value)
        {
            // Fixed decimal display (two fractional places), written as indices into stable GUIContent glyphs.
            double rounded = Math.Round(value * 100d, MidpointRounding.AwayFromZero);
            double integer = Math.Floor(rounded / 100d);
            int fraction = (int)Math.Max(0d, Math.Min(99d, rounded - integer * 100d));
            double divisor = 1d;
            while (integer / divisor >= 10d) divisor *= 10d;
            do
            {
                int digit = (int)Math.Max(0d, Math.Min(9d, Math.Floor(integer / divisor)));
                visual.Caption[visual.CaptionLength++] = (byte)digit;
                integer = Math.Max(0d, integer - digit * divisor);
                divisor /= 10d;
            } while (divisor >= 1d);
            if (fraction == 0) return;
            visual.Caption[visual.CaptionLength++] = 10;
            visual.Caption[visual.CaptionLength++] = (byte)(fraction / 10);
            if (fraction % 10 != 0) visual.Caption[visual.CaptionLength++] = (byte)(fraction % 10);
        }
        private static void UpdateBossCaption(BossEffectVisual visual, int count, float budget)
        {
            if (visual.CaptionCount == count && visual.CaptionBudget == budget) return;
            visual.CaptionCount = count; visual.CaptionBudget = budget; visual.CaptionLength = 0;
            if (count > 0) BossAppendNumber(visual, count);
            if (count > 0 && budget > 0)
            {
                visual.Caption[visual.CaptionLength++] = 11; visual.Caption[visual.CaptionLength++] = 12;
                visual.Caption[visual.CaptionLength++] = 11;
            }
            if (budget > 0) BossAppendNumber(visual, budget);
        }

        private void RegisterBossVisuals(Actor actor)
        {
            if (_onBossEffects == null) _onBossEffects = OnBossEffects;
            actor.CustomRpc_RegisterClientMessageHandler<DreamforgeBossEffectsMsg>(_onBossEffects);
            _netLiteReceiveReady = false;
            try
            {
                if (_onBossLite == null) _onBossLite = OnBossLite;
                actor.CustomRpc_RegisterClientMessageHandler<DreamforgeBossLiteMsg>(_onBossLite);
                _netLiteReceiveReady = true;
            }
            catch (Exception ex)
            {
                if (!_netLiteReceiveWarned)
                {
                    _netLiteReceiveWarned = true;
                    Log.Warn("NetLite boss receiver unavailable; legacy displays remain active: " + ex.Message);
                }
            }
        }
        private void UnregisterBossVisuals(Actor actor)
        {
            if (_onBossEffects != null)
                actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeBossEffectsMsg>(_onBossEffects);
            _netLiteReceiveReady = false;
            if (_onBossLite != null)
                try { actor.CustomRpc_UnregisterClientMessageHandler<DreamforgeBossLiteMsg>(_onBossLite); } catch (Exception) { }
        }
        private void ClearBossDisplay()
        {
            for (int i = 0; i < _bossOwnerVisuals.Length; i++) _bossOwnerVisuals[i].Release();
            Array.Clear(_bossRenderVisuals, 0, _bossRenderVisuals.Length);
            _bossRenderCount = 0; _nextBossDisplayPrune = _nextBossRenderRefresh = 0;
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
            if (msg == null || msg.ownerNetId == 0 || msg.epoch <= 0 || msg.revision < 0 || msg.equipmentEpoch < 0
                || !BossFinite(msg.hostTime) || !BossFinite(msg.sentAt)
                || msg.effects == null || msg.effects.Length > 64) return;
            RefreshBossDisplayRoom();
            if (string.IsNullOrEmpty(_bossVisualRun) || msg.runId != _bossVisualRun
                || msg.zone != _bossVisualZone || msg.room != _bossVisualRoom
                || !ObserveMonsterAuthority(msg.authorityGeneration)) return;
            Protocol.WarnContentMismatch(msg.protocol, msg.content, nameof(DreamforgeBossEffectsMsg));
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
            if (NetworkClient.spawned.TryGetValue(msg.ownerNetId, out var ownerIdentity) && ownerIdentity != null)
            {
                var hero = ownerIdentity.GetComponent<Hero>();
                if (hero == null || !hero.isActive || !hero.isAlive || hero.isKnockedOut) return;
            }
            BossOwnerVisual state = null, free = null;
            for (int i = 0; i < _bossOwnerVisuals.Length; i++)
            {
                var owner = _bossOwnerVisuals[i];
                if (owner.NetId == msg.ownerNetId) { state = owner; break; }
                if (owner.NetId == 0 && free == null) free = owner;
            }
            if (state == null)
            {
                if (free == null) return;
                state = free; state.NetId = msg.ownerNetId;
            }
            if (msg.epoch < state.Epoch || msg.epoch == state.Epoch && msg.revision < state.Revision) return;
            if (msg.epoch == state.Epoch && msg.equipmentEpoch != state.EquipmentEpoch) return;
            if (msg.epoch > state.Epoch || msg.snapshot) state.Clear();
            else if (msg.revision == state.Revision) return;
            state.Epoch = msg.epoch;
            state.EquipmentEpoch = msg.equipmentEpoch;
            state.Revision = msg.revision;
            state.LastMessage = Time.unscaledTime;
            double now = Time.time;
            double estimatedHostTime = msg.hostTime + Math.Max(0d, NetworkTime.time - msg.sentAt) * Time.timeScale;
            double clockOffset = now - estimatedHostTime;
            for (int i = 0; i < msg.effects.Length; i++)
            {
                var effect = msg.effects[i];
                double expires = effect.expires + clockOffset;
                int index = state.Find(effect.id);
                if (effect.removed || expires <= now)
                {
                    if (index >= 0) state.RemoveAt(index);
                    continue;
                }
                if (index < 0)
                {
                    if (state.Count == BossDisplayEffectLimit) continue;
                    index = state.Count++;
                }
                var visual = state.Effects[index];
                var copy = visual.Effect;
                copy.id = effect.id; copy.kind = effect.kind; copy.center = effect.center; copy.end = effect.end;
                copy.radius = effect.radius; copy.due = effect.due + clockOffset; copy.expires = expires;
                copy.element = effect.element; copy.shape = effect.shape; copy.count = effect.count; copy.budget = effect.budget;
                copy.range = effect.range; copy.width = effect.width; copy.angle = effect.angle; copy.finalRadius = effect.finalRadius;
                copy.targetNetId = effect.targetNetId; copy.removed = false;
                UpdateBossCaption(visual, effect.count, effect.budget);
            }
        }

        private void TickBossDisplay()
        {
            RefreshBossDisplayRoom();
            float clock = Time.unscaledTime;
            if (clock < _nextBossDisplayPrune) return;
            _nextBossDisplayPrune = clock + .25f;
            double now = Time.time;
            for (int i = 0; i < _bossOwnerVisuals.Length; i++)
            {
                var state = _bossOwnerVisuals[i];
                if (state.NetId == 0) continue;
                if (NetworkClient.spawned.TryGetValue(state.NetId, out var identity) && identity != null)
                {
                    var hero = identity.GetComponent<Hero>();
                    if (hero == null || !hero.isActive || !hero.isAlive || hero.isKnockedOut) { state.Release(); continue; }
                }
                else if (clock - state.LastMessage >= 2f) { state.Release(); continue; }
                for (int j = state.Count - 1; j >= 0; j--)
                    if (state.Effects[j].Effect.expires <= now) state.RemoveAt(j);
            }
        }

        private Vector3 BossDisplayCenter(DreamforgeBossEffect effect)
        {
            if (effect.kind == 7 && effect.targetNetId != 0 && NetworkClient.spawned.TryGetValue(effect.targetNetId, out var target)
                && target != null) return target.transform.position;
            return effect.center;
        }
        private bool BossDisplayVisible(Camera camera, DreamforgeBossEffect effect, Vector3 center, out float distance)
        {
            float extent = Mathf.Max(effect.radius, Mathf.Max(effect.finalRadius, effect.range)) + effect.width + 2f;
            var midpoint = (center + effect.end) * .5f;
            extent += (effect.end - center).magnitude * .5f;
            distance = (midpoint - camera.transform.position).sqrMagnitude;
            float far = BossDisplayDistance + extent;
            if (distance > far * far) return false;
            for (int i = 0; i < _bossVisualPlanes.Length; i++)
                if (_bossVisualPlanes[i].GetDistanceToPoint(midpoint) < -extent) return false;
            return true;
        }
        private void RefreshBossRenderVisuals(Camera camera, double now)
        {
            float clock = Time.unscaledTime;
            if (clock < _nextBossRenderRefresh) return;
            _nextBossRenderRefresh = clock + .1f;
            GeometryUtility.CalculateFrustumPlanes(camera, _bossVisualPlanes);
            _bossRenderCount = 0;
            // Admission is nearest-first with counters/glyphs ahead of generic geometry at the render-work cap.
            for (int i = 0; i < _bossOwnerVisuals.Length; i++)
            {
                var owner = _bossOwnerVisuals[i];
                for (int j = 0; j < owner.Count; j++)
                {
                    var visual = owner.Effects[j];
                    var effect = visual.Effect;
                    if (effect.expires <= now || !BossDisplayVisible(camera, effect, BossDisplayCenter(effect), out float distance)) continue;
                    if (effect.kind == 9) distance -= 2000000f;
                    else if (effect.kind == 1) distance -= 1000000f;
                    int index = _bossRenderCount;
                    if (index == BossDisplayRenderLimit)
                    {
                        if (distance >= _bossRenderDistances[index - 1]) continue;
                        index--;
                    }
                    else _bossRenderCount++;
                    while (index > 0 && distance < _bossRenderDistances[index - 1])
                    {
                        _bossRenderVisuals[index] = _bossRenderVisuals[index - 1];
                        _bossRenderDistances[index] = _bossRenderDistances[index - 1]; index--;
                    }
                    _bossRenderVisuals[index] = visual; _bossRenderDistances[index] = distance;
                }
            }
            for (int i = _bossRenderCount; i < _bossRenderVisuals.Length; i++) _bossRenderVisuals[i] = null;
        }

        internal void DrawBossEffects()
        {
            if (Event.current.type != EventType.Repaint) return;
            var camera = Camera.main;
            if (camera == null) return;
            double now = Time.time;
            RefreshBossRenderVisuals(camera, now);
            if (_bossRenderCount == 0) return;
            _bossSegmentsLeft = BossDisplaySegmentLimit;
            int captionGlyphsLeft = 512;
            var oldColor = GUI.color;
            var oldMatrix = GUI.matrix;
            // This world projection must not inherit the inventory UI's logical-canvas transform.
            GUI.matrix = Matrix4x4.identity;
            for (int visualIndex = 0; visualIndex < _bossRenderCount; visualIndex++)
                {
                    var visual = _bossRenderVisuals[visualIndex];
                    var effect = visual.Effect;
                    if (effect.expires <= now) continue;
                    var center = BossDisplayCenter(effect);
                    if (!BossDisplayVisible(camera, effect, center, out float distance)) continue;
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
                        int spokes = Math.Min(effect.count, 16);
                        for (int i = 0; i < spokes && _bossSegmentsLeft >= (effect.kind == 4 ? 3 : 1); i++)
                        {
                            int spokeIndex = i * effect.count / spokes;
                            var spoke = Quaternion.Euler(0, 360f * spokeIndex / effect.count, 0) * direction;
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
                    if (visual.CaptionLength > 0 && captionGlyphsLeft > 0)
                    {
                        var at = camera.WorldToScreenPoint(center + Vector3.up * .7f);
                        if (at.z > 0 && at.x >= 0 && at.x <= Screen.width && at.y >= 0 && at.y <= Screen.height)
                            for (int i = 0; i < visual.CaptionLength && captionGlyphsLeft > 0; i++, captionGlyphsLeft--)
                                GUI.Label(new Rect(at.x - 40f + i * 7f, Screen.height - at.y - 10f, 16f, 22f),
                                    BossCaptionGlyphs[visual.Caption[i]]);
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
        private void DrawBossWorldStrip(Camera camera, DreamforgeBossEffect effect, float width)
        {
            if (_bossSegmentsLeft < 4) return;
            var direction = BossVisualDirection(effect);
            var end = effect.range > 0 ? effect.center + direction * effect.range : effect.end;
            var half = Vector3.Cross(Vector3.up, direction) * (effect.width * .5f);
            DrawBossWorldLine(camera, effect.center - half, end - half, width);
            DrawBossWorldLine(camera, effect.center + half, end + half, width);
            DrawBossWorldLine(camera, effect.center - half, effect.center + half, width);
            DrawBossWorldLine(camera, end - half, end + half, width);
        }
        private void DrawBossWorldFan(Camera camera, DreamforgeBossEffect effect, float width)
        {
            var direction = BossVisualDirection(effect);
            const int segments = 16;
            if (_bossSegmentsLeft < segments + 2) return;
            var previous = effect.center + Quaternion.Euler(0, -effect.angle * .5f, 0) * direction * effect.range;
            DrawBossWorldLine(camera, effect.center, previous, width);
            for (int i = 1; i <= segments; i++)
            {
                var next = effect.center + Quaternion.Euler(0, -effect.angle * .5f + effect.angle * i / segments, 0) * direction * effect.range;
                DrawBossWorldLine(camera, previous, next, width); previous = next;
            }
            DrawBossWorldLine(camera, previous, effect.center, width);
        }
        private void DrawBossWorldRing(Camera camera, Vector3 center, float radius, float width)
        {
            const int segments = 24;
            if (_bossSegmentsLeft < segments) return;
            Vector3 previous = center + Vector3.right * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / segments);
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, .03f, Mathf.Sin(angle) * radius);
                DrawBossWorldLine(camera, previous, next, width);
                previous = next;
            }
        }
        private void DrawBossWorldLine(Camera camera, Vector3 from, Vector3 to, float width)
        {
            if (_bossSegmentsLeft <= 0) return;
            _bossSegmentsLeft--;
            Vector3 a = camera.WorldToScreenPoint(from), b = camera.WorldToScreenPoint(to);
            if (!BossFinite(a) || !BossFinite(b) || a.z <= 0f || b.z <= 0f
                || a.x < 0 && b.x < 0 || a.x > Screen.width && b.x > Screen.width
                || a.y < 0 && b.y < 0 || a.y > Screen.height && b.y > Screen.height) return;
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
