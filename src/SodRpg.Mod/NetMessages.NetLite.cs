using System;
using UnityEngine;

namespace SodRpg.Mod
{
    // Optional native JSON RPCs, used only after the existing capability exchange.
    // Floats and clocks retain their precision; short field names remove JSON overhead.
    [Serializable]
    public sealed class DreamforgeBossLiteMsg
    {
        public int p;
        public string c, r;
        public ulong a;
        public uint o;
        public int z, n;
        public long e, v, q;
        public bool s;
        public double t, u;
        public DreamforgeBossLiteEffect[] f;
    }

    [Serializable]
    public sealed class DreamforgeBossLiteEffect
    {
        public long i;
        public byte k, l, s, c;
        public uint t;
        public float x, y, z, X, Y, Z, r, g, w, a, b, f;
        public double d, e;
        public bool m;

        internal void CopyFrom(DreamforgeBossEffect source)
        {
            i = source.id; k = (byte)source.kind; l = (byte)source.element;
            s = (byte)source.shape; c = (byte)source.count; t = source.targetNetId;
            x = source.center.x; y = source.center.y; z = source.center.z;
            X = source.end.x; Y = source.end.y; Z = source.end.z;
            r = source.radius; g = source.range; w = source.width; a = source.angle;
            b = source.budget; f = source.finalRadius; d = source.due; e = source.expires; m = source.removed;
        }
        internal void CopyTo(DreamforgeBossEffect target)
        {
            target.id = i; target.kind = k; target.element = l; target.shape = s; target.count = c;
            target.targetNetId = t; target.center = new Vector3(x, y, z); target.end = new Vector3(X, Y, Z);
            target.radius = r; target.range = g; target.width = w; target.angle = a;
            target.budget = b; target.finalRadius = f; target.due = d; target.expires = e; target.removed = m;
        }
    }
}
