using System;
using UnityEngine;

namespace SodRpg.Mod
{
    [Serializable]
    public sealed class DreamforgeBossEffect
    {
        public long id;
        public int kind;
        public uint targetNetId;
        public Vector3 center, end;
        public float radius;
        public int element, shape, count;
        public float range, width, angle, budget;
        public float finalRadius = -1f;
        public double due, expires;
        public bool removed;
    }

    /// <summary>Host-authored display only; clients never submit combat commands.</summary>
    [Serializable]
    public sealed class DreamforgeBossEffectsMsg
    {
        public int protocol;
        public string content, runId;
        public ulong authorityGeneration;
        public uint ownerNetId;
        public int zone, room;
        public long epoch, revision;
        public long equipmentEpoch;
        public bool snapshot;
        public double hostTime, sentAt;
        public DreamforgeBossEffect[] effects;
    }
}
