"""Lower the compiler's typed expressions to positional data and a shared typed reader.

Only expressions emitted by gen_cs are accepted. The generated reader calls the same
constructors and helpers directly (no reflection, runtime compilation or JSON dependency).
"""
import json
import re

TOKEN = re.compile(r'\s*("(?:\\.|[^"\\])*"|-?\d+(?:\.\d+)?[mf]?|[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*|=>|[^\s])')
RETURNS = {
    "MemorySelector.Parse": "MemorySelector", "ClusterRegion.Memory": "ClusterRegion", "ClusterRegion.Bridge": "ClusterRegion",
    "ModifierUnits.FromPercent": "ModifierUnits", "ProbabilityUnits.FromPercent": "ProbabilityUnits",
    "AuthoredKeystoneCompiler.Compile": "KeystoneDefinition", "ManifestKeystone": "KeystoneDefinition",
    "ManifestRetained": "AuthoredStarDef", "ManifestBaselinePair": "AuthoredStarDef", "ManifestRouteEntry": "AuthoredStarEdge",
    "ManifestPair": "AuthoredMechanismSpec", "ManifestNewPair": "AuthoredMechanismSpec", "ManifestNewDirectRechargePair": "AuthoredMechanismSpec",
}


class Node:
    def __init__(self, kind, name, args=(), names=(), type_name=None):
        self.kind, self.name, self.args, self.names = kind, name, list(args), list(names)
        self.type = type_name


class Parser:
    def __init__(self, expression):
        self.tokens = TOKEN.findall(expression)
        self.at = 0

    def peek(self):
        return self.tokens[self.at] if self.at < len(self.tokens) else None

    def take(self, expected=None):
        token = self.peek()
        if expected is not None and token != expected:
            raise ValueError(f"expected {expected}, got {token}: {self.tokens[self.at:self.at + 8]}")
        self.at += 1
        return token

    def sequence(self, end, named=False):
        args, names = [], []
        while self.peek() != end:
            name = None
            if named and self.at + 1 < len(self.tokens) and self.tokens[self.at + 1] in (":", "="):
                name = self.take() + self.take()
            args.append(self.value())
            names.append(name)
            if self.peek() != end:
                self.take(",")
        self.take(end)
        return args, names

    def value(self):
        token = self.take()
        if token.startswith('"'):
            return Node("literal", json.loads(token), type_name="string")
        if re.fullmatch(r'-?\d+(?:\.\d+)?[mf]?', token):
            return Node("literal", token.rstrip("mf"), type_name="decimal" if token.endswith("m") else "float" if token.endswith("f") else "int")
        if token in ("true", "false"):
            return Node("literal", token, type_name="bool")
        if token == "null":
            return Node("null", token)
        if token == "baselineLayout":
            return Node("layout", token, type_name="HeroTreeLayout")
        if token == "new":
            name = self.take()
            if name == "[":
                self.take("]")
                self.take("{")
                args, _ = self.sequence("}")
                return Node("array", args[0].type, args, type_name=args[0].type + "[]")
            if name == "Dictionary":
                self.take("<"); self.take("string"); self.take(","); self.take("MemoryEventKind"); self.take(">")
                self.take("("); self.take("StringComparer.Ordinal"); self.take(")"); self.take("{")
                args = []
                while self.peek() != "}":
                    self.take("{"); args.append(self.value()); self.take(","); args.append(self.value()); self.take("}")
                    if self.peek() != "}": self.take(",")
                self.take("}")
                return Node("dictionary", name, args, type_name="Dictionary<string, MemoryEventKind>")
            if self.peek() == "[":
                self.take("["); self.take("]"); self.take("{")
                args, _ = self.sequence("}")
                return Node("array", name, args, type_name=name + "[]")
            if self.peek() == "{":
                self.take("{")
                args, names = self.sequence("}", True)
                return Node("object", name, args, names, name)
            self.take("(")
            args, names = self.sequence(")", True)
            return Node("constructor", name, args, names, name)
        if token == "Array.Empty":
            self.take("<"); name = self.take(); self.take(">"); self.take("("); self.take(")")
            return Node("array", name, type_name=name + "[]")
        if self.peek() == "(":
            self.take("(")
            args, names = self.sequence(")", True)
            result = RETURNS.get(token)
            if result is None and token.startswith("IdentityStrikeDefinition."): result = "IdentityStrikeDefinition"
            if result is None and token.startswith("MemoryTuningDefinition."): result = "MemoryTuningDefinition"
            if result is None: raise ValueError("Unknown generated call " + token)
            return Node("call", token, args, names, result)
        if "." in token:
            return Node("enum", token, type_name="int" if token == "Content.KeystoneCost" else token.split(".")[0])
        raise ValueError("Unknown generated expression " + str(token))


def parse(expression):
    parser = Parser(expression)
    node = parser.value()
    if parser.peek() is not None: raise ValueError("Trailing expression: " + str(parser.tokens[parser.at:]))
    return node


def escape(value):
    return str(value).replace("\\", "\\\\").replace("|", "\\p").replace("\r", "\\r").replace("\n", "\\n")


class Tables:
    def __init__(self):
        self.operations = {}
        self.targets = {}

    def operation(self, code):
        if code not in self.operations:
            self.operations[code] = len(self.operations)
        return str(self.operations[code])

    def encode(self, node):
        if node.kind == "literal":
            return [self.operation({"string": "r.Text()", "int": "r.Int()", "decimal": "r.Decimal()", "float": "r.Single()", "bool": "r.Bool()"}[node.type]), escape(node.name)]
        if node.kind == "enum": return [self.operation(node.name)]
        if node.kind == "null": return [self.operation("null")]
        if node.kind == "layout": return [self.operation("r.BaselineLayout")]
        if node.kind == "array":
            return [self.operation("r.Array<" + node.name + ">()"), str(len(node.args))] + [v for arg in node.args for v in self.encode(arg)]
        if node.kind == "dictionary":
            return [self.operation("r.Triggers()"), str(len(node.args) // 2)] + [v for arg in node.args for v in self.encode(arg)]
        values, code_args = [], []
        for arg, name in zip(node.args, node.names):
            if name == "TargetEffectIds=":
                key = tuple(a.name for a in arg.args)
                if key not in self.targets: self.targets[key] = len(self.targets)
                code_args.append("TargetEffectIds = ManifestTargetEffectIds[r.Int()]")
                values.append(str(self.targets[key]))
                continue
            value = "null" if arg.kind == "null" else "(" + arg.type + ")r.Read()"
            code_args.append((name or "") + value)
            if arg.kind != "null": values.extend(self.encode(arg))
        if node.kind == "object": code = "new " + node.name + " { " + ", ".join(code_args) + " }"
        else: code = ("new " if node.kind == "constructor" else "") + node.name + "(" + ", ".join(code_args) + ")"
        return [self.operation(code)] + values

    def row(self, expression):
        return "|".join(self.encode(parse(expression)))

    def render(self):
        lines = ["// Generated by tools/star-manifest/gen_cs.py; do not edit.", "using System;", "using System.Collections.Generic;", "using System.Globalization;", "using System.Text;", "", "namespace SodRpg.Core.Game", "{", "    public static partial class StarClusters", "    {"]
        lines += ["        private static readonly string[][] ManifestTargetEffectIds = new string[][]", "        {"]
        for targets in self.targets:
            lines.append("            new string[] { " + ", ".join(json.dumps(v, ensure_ascii=False) for v in targets) + " },")
        lines += ["        };", "", "        private static AuthoredStarDef[] ReadManifest(string data, string hero, bool needsLayout)", "        {", "            var rows = data.Split('\\n');", "            var definitions = new AuthoredStarDef[rows.Length];", "            var reader = new ManifestReader(needsLayout ? HeroTreeLayout.ForTalents(HeroSigils.BaselineTreeFor(hero)) : null);", "            for (int i = 0; i < rows.Length; i++)", "            {", "                reader.Reset(rows[i]);", "                definitions[i] = (AuthoredStarDef)reader.Read();", "                reader.End();", "            }", "            return definitions;", "        }", "", "        // A positional stream of typed constructor records; only trusted generated constants reach this reader.", "        private sealed class ManifestReader", "        {", "            internal readonly HeroTreeLayout BaselineLayout;", "            private string[] parts;", "            private int at;", "            internal ManifestReader(HeroTreeLayout layout) { BaselineLayout = layout; }", "            internal void Reset(string row) { parts = row.Split('|'); at = 0; }", "            internal void End() { if (at != parts.Length) throw new FormatException(\"Trailing generated star data.\"); }", "            internal int Int() => int.Parse(parts[at++], CultureInfo.InvariantCulture);", "            internal decimal Decimal() => decimal.Parse(parts[at++], CultureInfo.InvariantCulture);", "            internal float Single() => float.Parse(parts[at++], CultureInfo.InvariantCulture);", "            internal bool Bool() => bool.Parse(parts[at++]);", "            internal string Text()", "            {", "                string value = parts[at++];", "                if (value.IndexOf('\\\\') < 0) return value;", "                var text = new StringBuilder(value.Length);", "                for (int i = 0; i < value.Length; i++)", "                {", "                    char c = value[i];", "                    if (c == '\\\\')", "                    {", "                        c = value[++i];", "                        c = c == 'p' ? '|' : c == 'r' ? '\\r' : c == 'n' ? '\\n' : c;", "                    }", "                    text.Append(c);", "                }", "                return text.ToString();", "            }", "            internal T[] Array<T>()", "            {", "                int count = Int();", "                if (count == 0) return System.Array.Empty<T>();", "                var values = new T[count];", "                for (int i = 0; i < count; i++) values[i] = (T)Read();", "                return values;", "            }", "            internal Dictionary<string, MemoryEventKind> Triggers()", "            {", "                int count = Int();", "                var values = new Dictionary<string, MemoryEventKind>(count, StringComparer.Ordinal);", "                for (int i = 0; i < count; i++) values.Add((string)Read(), (MemoryEventKind)Read());", "                return values;", "            }", "            internal object Read()", "            {", "                switch (Int())", "                {"]
        for code, operation in self.operations.items():
            lines.append("                    case " + str(operation) + ": return Read" + str(operation) + "(this);")
        lines += ["                    default: throw new FormatException(\"Unknown generated star record.\");", "                }", "            }"]
        for code, operation in self.operations.items():
            lines.append("            private static object Read" + str(operation) + "(ManifestReader r) => " + code + ";")
        return "\n".join(lines + ["        }", "    }", "}", ""])
