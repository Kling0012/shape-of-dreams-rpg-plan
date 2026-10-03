using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SodRpg.Core.Game
{
    public static class AuthoredKeystoneCodec
    {
        public static string Encode(KeystoneDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    // Grammar 1 is byte-identical to every keystone without a retained Power; grammar 2 appends it.
                    bool retained = definition.RetainedPower != Power.None;
                    writer.Write((byte)(retained ? 2 : 1)); writer.Write(definition.KeystoneId); writer.Write(definition.Cost);
                    Strings(writer, definition.RequiredMemories); Strings(writer, definition.Prerequisites);
                    writer.Write(definition.Payloads.Count); foreach (var payload in definition.Payloads) writer.Write((int)payload);
                    Transforms(writer, definition.Upside); Transforms(writer, definition.Downside);
                    writer.Write(definition.Grants.Count);
                    foreach (var grant in definition.Grants) writer.Write(AuthoredMechanismCodec.Encode(new AuthoredMechanismEntry
                        { StarId = definition.KeystoneId, ContributorIds = new[] { definition.KeystoneId }, Spec = grant }));
                    if (retained) { writer.Write((int)definition.RetainedPower); writer.Write(definition.RetainedPowerValue); }
                }
                if (stream.Length > 65536) throw new InvalidOperationException("Keystone exceeds wire budget.");
                return Convert.ToBase64String(stream.ToArray());
            }
        }
        public static KeystoneDefinition Decode(string encoded)
        {
            if (encoded == null || encoded.Length > 87384) throw new FormatException("Keystone exceeds wire budget.");
            using (var stream = new MemoryStream(Convert.FromBase64String(encoded), false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, false))
            {
                byte grammar = reader.ReadByte();
                if (grammar != 1 && grammar != 2) throw new FormatException("Unknown keystone grammar.");
                string id = Token(reader); int cost = reader.ReadInt32(); var memories = Strings(reader); var prerequisites = Strings(reader);
                var kinds = new List<KeystonePayloadKind>(); int count = Count(reader);
                for (int i = 0; i < count; i++) kinds.Add((KeystonePayloadKind)reader.ReadInt32());
                var up = Transforms(reader); var down = Transforms(reader);
                var grants = new List<AuthoredMechanismSpec>(); count = Count(reader);
                for (int i = 0; i < count; i++)
                {
                    var entry = AuthoredMechanismCodec.Decode(reader.ReadString());
                    if (entry.StarId != id || entry.ContributorIds.Length != 1 || entry.ContributorIds[0] != id)
                        throw new FormatException("Invalid keystone grant ownership.");
                    grants.Add(entry.Spec);
                }
                var power = Power.None; int powerValue = 0;
                if (grammar == 2)
                {
                    int rawPower = reader.ReadInt32(); powerValue = reader.ReadInt32();
                    if (!Enum.IsDefined(typeof(Power), rawPower) || rawPower == (int)Power.None || powerValue <= 0)
                        throw new FormatException("Invalid retained keystone Power.");
                    power = (Power)rawPower;
                }
                if (stream.Position != stream.Length) throw new FormatException("Trailing keystone data.");
                return new KeystoneDefinition(id, memories, up, down, prerequisites, kinds, cost, grants, power, powerValue);
            }
        }
        private static void Strings(BinaryWriter writer, IReadOnlyList<string> values)
        { writer.Write(values.Count); foreach (var value in values) writer.Write(value); }
        private static string[] Strings(BinaryReader reader)
        { int count = Count(reader); var values = new string[count]; for (int i = 0; i < count; i++) values[i] = Token(reader); return values; }
        private static string Token(BinaryReader reader)
        { string value = reader.ReadString(); KeystoneValidation.Token(value); return value; }
        private static int Count(BinaryReader reader)
        { int count = reader.ReadInt32(); if (count < 0 || count > StarProgression.MaxSpendablePoints) throw new FormatException("Invalid keystone collection size."); return count; }
        private static void Selectors(BinaryWriter writer, IReadOnlyList<MemorySelector> selectors)
        { writer.Write(selectors.Count); foreach (var selector in selectors) writer.Write(selector.Expression); }
        private static MemorySelector[] Selectors(BinaryReader reader)
        {
            int count = Count(reader); var selectors = new MemorySelector[count];
            for (int i = 0; i < count; i++) selectors[i] = MemorySelector.Parse(reader.ReadString());
            return selectors;
        }
        private static void Transforms(BinaryWriter writer, IReadOnlyList<KeystoneTransform> transforms)
        {
            writer.Write(transforms.Count);
            foreach (var t in transforms)
            {
                writer.Write((int)t.TargetLayer); writer.Write((int)t.Field); writer.Write((int)t.Operation);
                writer.Write(t.MagnitudeUnits.Units); writer.Write(t.WoundDurationUnits.Units); writer.Write(t.Seconds); writer.Write(t.Count);
                writer.Write(t.Maximum.HasValue); if (t.Maximum.HasValue) writer.Write(t.Maximum.Value);
                writer.Write(t.ExpectedFrom.HasValue); if (t.ExpectedFrom.HasValue) writer.Write(t.ExpectedFrom.Value);
                var s = t.Scope; Strings(writer, s.TargetMemorySet); Strings(writer, s.TargetEffectIds); Strings(writer, s.ReceiverMemorySet);
                writer.Write(s.TargetEffectSet.Count); foreach (var effect in s.TargetEffectSet) writer.Write((int)effect);
                writer.Write((int)s.Recipient); writer.Write((int)s.PayloadKind);
                writer.Write(s.Argument.HasValue); if (s.Argument.HasValue) writer.Write(s.Argument.Value);
                writer.Write(s.SourceKind.HasValue); if (s.SourceKind.HasValue) writer.Write((int)s.SourceKind.Value);
                Selectors(writer, s.SourceSelectors); Selectors(writer, s.ReceiverSelectors);
            }
        }
        private static KeystoneTransform[] Transforms(BinaryReader reader)
        {
            int count = Count(reader); var result = new KeystoneTransform[count];
            for (int i = 0; i < count; i++)
            {
                var layer = (KeystoneLayer)reader.ReadInt32(); var field = (KeystoneField)reader.ReadInt32(); var operation = (KeystoneOperation)reader.ReadInt32();
                var magnitude = new KeystoneMagnitude(reader.ReadInt32()); var duration = new KeystoneMagnitude(reader.ReadInt32());
                decimal seconds = reader.ReadDecimal(); int targets = reader.ReadInt32();
                decimal? maximum = reader.ReadBoolean() ? reader.ReadDecimal() : (decimal?)null;
                decimal? from = reader.ReadBoolean() ? reader.ReadDecimal() : (decimal?)null;
                if (!Enum.IsDefined(typeof(KeystoneLayer), layer) || !Enum.IsDefined(typeof(KeystoneField), field)
                    || !Enum.IsDefined(typeof(KeystoneOperation), operation)
                    || operation != KeystoneOperation.RedistributeWound && duration.Units != 0
                    || operation != KeystoneOperation.Scale && operation != KeystoneOperation.RedistributeWound && magnitude.Units != 0
                    || operation != KeystoneOperation.SetSeconds && operation != KeystoneOperation.Set && operation != KeystoneOperation.Add && seconds != 0
                    || operation != KeystoneOperation.AddTargets && operation != KeystoneOperation.SetEveryN && targets != 0
                    || operation != KeystoneOperation.Add && maximum.HasValue || operation != KeystoneOperation.Set && from.HasValue)
                    throw new FormatException("Unexpected keystone operation fields.");
                if ((operation == KeystoneOperation.AddTargets && (layer != KeystoneLayer.ModEffect || field != KeystoneField.TargetCount))
                    || (operation == KeystoneOperation.SetEveryN && (layer != KeystoneLayer.ModEffect || field != KeystoneField.EveryN))
                    || (operation == KeystoneOperation.SetSeconds && layer != KeystoneLayer.ModEffect)
                    || (operation == KeystoneOperation.Disable && field != KeystoneField.Value)
                    || (operation == KeystoneOperation.RedistributeWound && (layer != KeystoneLayer.ModEffect || field != KeystoneField.Value)))
                    throw new FormatException("Contradictory keystone operation layer.");
                var memories = Strings(reader); var ids = Strings(reader); var receivers = Strings(reader);
                int effectsCount = Count(reader); var effects = new GimmickEffect[effectsCount];
                for (int j = 0; j < effectsCount; j++) effects[j] = (GimmickEffect)reader.ReadInt32();
                var recipient = (KeystoneRecipientKind)reader.ReadInt32(); var payload = (KeystonePayloadKind)reader.ReadInt32();
                int? argument = reader.ReadBoolean() ? reader.ReadInt32() : (int?)null;
                KeystoneSourceKind? source = reader.ReadBoolean() ? (KeystoneSourceKind)reader.ReadInt32() : (KeystoneSourceKind?)null;
                var scope = new KeystoneScope(memories, effects, ids, receivers, recipient, payload, argument, source, Selectors(reader), Selectors(reader));
                switch (operation)
                {
                    case KeystoneOperation.Scale: result[i] = KeystoneTransform.Scale(layer, field, magnitude, scope); break;
                    case KeystoneOperation.SetSeconds: result[i] = KeystoneTransform.SetSeconds(field, seconds, scope); break;
                    case KeystoneOperation.AddTargets: result[i] = KeystoneTransform.AddTargets(targets, scope); break;
                    case KeystoneOperation.SetEveryN: result[i] = KeystoneTransform.SetEveryN(targets, scope); break;
                    case KeystoneOperation.Disable: result[i] = KeystoneTransform.Disable(layer, scope); break;
                    case KeystoneOperation.RedistributeWound: result[i] = KeystoneTransform.RedistributeWound(magnitude, duration, scope); break;
                    case KeystoneOperation.Set: result[i] = KeystoneTransform.Set(layer, field, seconds, scope, from); break;
                    case KeystoneOperation.Add: result[i] = KeystoneTransform.Add(layer, field, seconds, scope, maximum); break;
                    default: throw new FormatException("Unknown keystone operation.");
                }
            }
            return result;
        }
    }
}
