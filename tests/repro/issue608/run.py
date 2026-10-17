#!/usr/bin/env python3
"""Run #608 against native multiplicative DamageData without changing shared stubs."""
import datetime
import json
import os
from pathlib import Path
import subprocess
import sys
from xml.sax.saxutils import escape
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
PROJECT = ROOT / "tests/Issue73.Native.Tests"

# Dew.Core/DamageData.cs:47, 51-70, 111-120, 166-170. Keep the harness's
# amount read alias and API types, but use native deferred multiplier arithmetic.
DAMAGE_DATA = """internal struct DamageData
    {
        public enum SourceType { Normal, Pure }
        private float originalAmount, amplificationMultiplier, reductionMultiplier;
        public float amount
        {
            get => currentAmount;
            set { originalAmount = value; amplificationMultiplier = reductionMultiplier = 1f; }
        }
        public Actor actor;
        public DamageAttribute attributes;
        public SourceType source;
        public float damageToShieldMultiplier;
        public bool isBlockedByImmunity;
        public float currentAmount => Math.Max(0f, originalAmount) * amplificationMultiplier * reductionMultiplier;
        public bool HasAttr(DamageAttribute attr) => attr != 0 && (attributes & attr) == attr;
        public DamageData ApplyReduction(float ratio)
        {
            reductionMultiplier *= 1f - Math.Max(0f, Math.Min(1f, ratio));
            return this;
        }
        public DamageData ApplyAmplification(float ratio)
        {
            if (ratio >= 0f) amplificationMultiplier *= 1f + ratio;
            return this;
        }
        public DamageData(float value)
        {
            originalAmount = value;
            amplificationMultiplier = reductionMultiplier = 1f;
            actor = null;
            attributes = 0;
            source = SourceType.Normal;
            damageToShieldMultiplier = 1f;
            isBlockedByImmunity = false;
        }
    }
    """


def main():
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    generated = PROJECT / "obj" / "issue608" / stamp
    artifacts = PROJECT / "TestResults" / ("issue608-" + stamp)
    generated.mkdir(parents=True)
    artifacts.mkdir(parents=True)
    original = PROJECT / "NativeBoundary.cs"
    source = original.read_text(encoding="utf-8")
    start = source.index("internal struct DamageData\n")
    end = source.index("// 本体の回復処理", start)
    boundary = generated / "NativeBoundary.cs"
    boundary.write_text(source[:start] + DAMAGE_DATA + source[end:], encoding="utf-8")
    targets = generated / "Issue608.targets"
    targets.write_text(
        '<Project><Target Name="Issue608NativeDamageBoundary" BeforeTargets="CoreCompile" '
        'Condition="\'$(MSBuildProjectName)\' == \'Issue73.Native.Tests\'">'
        '<ItemGroup><Compile Remove="NativeBoundary.cs;' + escape(str(original)) + '" />'
        '<Compile Include="' + escape(str(boundary)) + '" /></ItemGroup></Target></Project>\n',
        encoding="utf-8",
    )
    command = [
        "dotnet", "test", str(PROJECT / "Issue73.Native.Tests.csproj"), "-c", "Release",
        "--filter", "FullyQualifiedName~Issue608ZeroShareReproductionTests", "--nologo",
        "-v", "quiet", "-p:WarningLevel=0",
        "-p:CustomBeforeMicrosoftCommonTargets=" + str(targets),
        "-p:OutputPath=bin/issue608/" + stamp + "/",
        "--results-directory", str(artifacts),
        "--logger", "trx;LogFileName=repro.trx", "--logger", "console;verbosity=normal",
    ]
    env = os.environ.copy()
    env.setdefault("DOTNET_ROLL_FORWARD", "Major")
    print("Running: " + " ".join(command), flush=True)
    completed = subprocess.run(command, cwd=ROOT, env=env, text=True,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
    (artifacts / "repro.log").write_text(completed.stdout, encoding="utf-8")
    result = {
        "command": command, "exit_code": completed.returncode,
        "boundary": "native deferred multiplication; original production sources linked unchanged",
    }
    trx = artifacts / "repro.trx"
    if trx.exists():
        namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
        document = ET.parse(trx)
        result["cases"] = [
            {"name": case.attrib["testName"], "outcome": case.attrib["outcome"],
             "output": case.findtext("t:Output/t:StdOut", default="", namespaces=namespace)}
            for case in document.findall(".//t:UnitTestResult", namespace)
        ]
    else:
        result["harness_error"] = "No TRX was produced; do not treat this as bug reproduction."
    (artifacts / "result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(completed.stdout, end="", flush=True)
    print(f"exit_code={completed.returncode}; artifacts={artifacts}", flush=True)
    return completed.returncode if "harness_error" not in result else 2


if __name__ == "__main__":
    sys.exit(main())
