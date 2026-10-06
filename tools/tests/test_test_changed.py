"""Unit tests for the change -> test mapping logic of tools/test_changed.py.

Uses small temporary fake repo layouts and the repository's catalog/test corpus;
never invokes git or dotnet.
Run from the repository root:

    python -m unittest discover -s tools/tests -p "test_test_changed.py"
"""

import sys
import tempfile
import unittest
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import test_changed as tc  # noqa: E402


def fake_repo(files: dict, root: Path) -> Path:
    """Write {relative posix path: content} under root and return root."""
    for rel, content in files.items():
        path = root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
    return root


def test_file(namespace, *classes):
    body = "\n".join(f"    public class {c} {{}}" for c in classes)
    return f"namespace {namespace}\n{{\n{body}\n}}\n"


CORE = "src/SodRpg.Core/Game/StarSummary.cs"
CORE_CONTENT = """using System;

namespace SodRpg.Core.Game
{
    public sealed class StarSummaryLine { public string Text; }
    public sealed partial class StarSummary { public int Spent; }
    public sealed partial class StarSummary { public int Rank; }
    public enum StarKind { Plain, Keystone }
    public interface IStar {} 
    public record StarStats(int Count);
    public struct StarWeight { public int W; }
}
"""

USING_TESTS = test_file("SodRpg.Core.Tests", "StarSummaryTests") + (
    "// uses StarSummary and StarSummaryLine elsewhere\n")
PLAIN_TESTS = test_file("SodRpg.Core.Tests", "UnrelatedTests")
PARTIAL_WORD_TESTS = test_file("SodRpg.Core.Tests", "PartialWordTests") + (
    "// only mentions StarSummaryLine, not StarSummary\n")


class DeclaredTypesTests(unittest.TestCase):
    def test_collects_all_type_kinds_and_partial_once(self):
        types = tc.declared_types(CORE_CONTENT)
        self.assertEqual(
            types,
            ["StarSummaryLine", "StarSummary", "StarKind", "IStar", "StarStats", "StarWeight"],
        )

    def test_comments_are_ignored(self):
        types = tc.declared_types("// class NotReal { }\n/* struct AlsoFake */\nclass Real {}\n")
        self.assertEqual(types, ["Real"])

    def test_record_class_form_yields_single_name(self):
        self.assertEqual(tc.declared_types("public record class Point(int X);\n"), ["Point"])


class DeclaredTestClassesTests(unittest.TestCase):
    def test_block_namespace(self):
        self.assertEqual(
            tc.declared_test_classes(test_file("N.T", "ATests", "BTests")),
            ["N.T.ATests", "N.T.BTests"],
        )

    def test_file_scoped_namespace_and_no_namespace(self):
        self.assertEqual(
            tc.declared_test_classes("namespace N.T;\npublic class CTests {}\n"),
            ["N.T.CTests"],
        )
        self.assertEqual(tc.declared_test_classes("public class DTests {}\n"), ["DTests"])


class SelectTestsFromContents(unittest.TestCase):
    def corpus(self, extra=None):
        files = {
            "tests/A/StarSummaryTests.cs": USING_TESTS,
            "tests/A/UnrelatedTests.cs": PLAIN_TESTS,
            "tests/A/Unrelated2Tests.cs": PLAIN_TESTS,
            "tests/A/Unrelated3Tests.cs": PLAIN_TESTS,
            "tests/A/PartialWordTests.cs": PARTIAL_WORD_TESTS,
            CORE: CORE_CONTENT,
        }
        files.update(extra or {})
        return files

    def test_changed_test_file_selects_its_own_classes(self):
        selection = tc.select_tests(["tests/A/UnrelatedTests.cs"], self.corpus())
        self.assertFalse(selection.run_all)
        self.assertEqual(selection.classes, {"SodRpg.Core.Tests.UnrelatedTests"})

    def test_changed_production_file_selects_mentioning_tests(self):
        selection = tc.select_tests([CORE], self.corpus())
        # StarSummaryLine appears in PartialWordTests too; UnrelatedTests stays out.
        self.assertEqual(
            selection.classes,
            {"SodRpg.Core.Tests.StarSummaryTests", "SodRpg.Core.Tests.PartialWordTests"},
        )

    def test_whole_word_match_required(self):
        # A type used only as a substring of another name does not match.
        content = "namespace N.T\n{\n    public class StarSummaryXTests {}\n}\n"
        files = self.corpus({"tests/A/StarSummaryXTests.cs": content,
                             "tests/A/StarSummaryTests.cs":
                                 test_file("SodRpg.Core.Tests", "StarSummaryTests")})
        selection = tc.select_tests([CORE], files)
        self.assertNotIn("SodRpg.Core.Tests.StarSummaryXTests", selection.classes)

    def test_widely_used_type_runs_everything(self):
        # StarSummary mentioned by 2 of 3 test files (> 60%) -> full run.
        files = {
            "tests/A/T1.cs": test_file("T", "T1") + "// uses StarSummary\n",
            "tests/A/T2.cs": test_file("T", "T2") + "// uses StarSummary\n",
            "tests/A/T3.cs": test_file("T", "T3"),
            "src/SodRpg.Core/S.cs": "public class StarSummary {}\n",
        }
        selection = tc.select_tests(["src/SodRpg.Core/S.cs"], files)
        self.assertTrue(selection.run_all)
        self.assertEqual(selection.classes, {"T.T1", "T.T2", "T.T3"})
        self.assertTrue(any("used in 2/3" in r for r in selection.reasons))

    def test_exactly_60_percent_does_not_trigger_full_run(self):
        # 3 of 5 files is exactly 60% -> not "more than", partial run.
        files = {f"tests/A/T{i}.cs": test_file("T", f"T{i}") + "// uses StarSummary\n"
                 for i in (1, 2, 3)}
        files.update({f"tests/A/T{i}.cs": test_file("T", f"T{i}") for i in (4, 5)})
        files["src/SodRpg.Core/S.cs"] = "public class StarSummary {}\n"
        selection = tc.select_tests(["src/SodRpg.Core/S.cs"], files)
        self.assertFalse(selection.run_all)
        self.assertEqual(selection.classes, {"T.T1", "T.T2", "T.T3"})

    def test_build_files_run_everything(self):
        for path in ("src/SodRpg.Core/SodRpg.Core.csproj", "Directory.Build.props",
                     "tests/A/Directory.Build.targets"):
            with self.subTest(path=path):
                selection = tc.select_tests([path], self.corpus())
                self.assertTrue(selection.run_all)
                self.assertTrue(selection.reasons)

    def test_data_json_selects_path_referencing_tests(self):
        content = (
            'namespace T\n{\n    public class ManifestTests\n    {\n'
            '        // reads Path.Combine(root, "tools", "star-manifest", "outer.json")\n'
            '    }\n}\n'
        )
        files = self.corpus({"tests/A/ManifestTests.cs": content})
        selection = tc.select_tests(["tools/star-manifest/outer.json"], files)
        self.assertIn("T.ManifestTests", selection.classes)
        self.assertNotIn("SodRpg.Core.Tests.UnrelatedTests", selection.classes)

    def test_generated_cs_selects_type_referencing_tests(self):
        gen = "namespace G\n{\n    public class AurenaGenerated { }\n}\n"
        user = test_file("T", "AurenaTests") + "// AurenaGenerated rows\n"
        files = self.corpus({"src/SodRpg.Core/Game/StarClusters/Aurena.Generated.cs": gen,
                             "tests/A/AurenaTests.cs": user})
        selection = tc.select_tests(["src/SodRpg.Core/Game/StarClusters/Aurena.Generated.cs"], files)
        self.assertIn("T.AurenaTests", selection.classes)

    def test_nothing_relevant_changed(self):
        selection = tc.select_tests(["docs/specs/plan.md", "README.md"], self.corpus())
        self.assertFalse(selection.run_all)
        self.assertEqual(selection.classes, set())


class DiffNarrowingTests(unittest.TestCase):
    """select_tests with unified diffs narrows src/ changes to touched tokens."""

    def corpus(self):
        files = {
            "tests/A/StarSummaryTests.cs": USING_TESTS,
            "tests/A/UnrelatedTests.cs": PLAIN_TESTS,
            "tests/A/Unrelated2Tests.cs": PLAIN_TESTS,
            "tests/A/Unrelated3Tests.cs": PLAIN_TESTS,
            "tests/A/PartialWordTests.cs": PARTIAL_WORD_TESTS,
            CORE: CORE_CONTENT,
        }
        return files

    def diff(self, plus=(), minus=()):
        body = "\n".join("+" + line for line in plus) + "\n" + \
               "\n".join("-" + line for line in minus)
        return f"--- a/{CORE}\n+++ b/{CORE}\n@@ -1,1 +1,1 @@\n{body}\n"

    def test_member_change_selects_tests_mentioning_the_member(self):
        # Only the Compute signature changed; tests naming Compute run.
        files = self.corpus()
        files["tests/A/ComputeTests.cs"] = test_file("T", "ComputeTests") + "// calls StarSummary.Compute\n"
        selection = tc.select_tests([CORE], files, {CORE: self.diff(
            plus=["    public StarSummaryLine Compute(int mode) => null;"],
            minus=["    public StarSummaryLine Compute() => null;"])})
        self.assertIn("T.ComputeTests", selection.classes)
        self.assertFalse(selection.run_all)

    def test_data_row_change_selects_tests_naming_the_id(self):
        files = self.corpus()
        files["tests/A/VigilTests.cs"] = test_file("T", "VigilTests") + '// asserts "unique.vigil_coif"\n'
        selection = tc.select_tests([CORE], files, {CORE: self.diff(
            plus=['            new UniqueDef("unique.vigil_coif", "head.iron_coif"),'],
            minus=['            new UniqueDef("unique.old_coif", "head.iron_coif"),'])})
        self.assertIn("T.VigilTests", selection.classes)
        self.assertNotIn("SodRpg.Core.Tests.UnrelatedTests", selection.classes)

    def test_catalog_name_typo_selects_table_reader(self):
        path = "src/SodRpg.Core/Game/Content.cs"
        reader = "tests/SodRpg.Core.Tests/ContentTableV129Tests.cs"
        content = tc.read_file(tc.REPO_ROOT, path)
        old = next(line for line in content.splitlines() if '"unique.w_palmcannon"' in line)
        new = old.replace("Boulderburst Hammer", "Boulderburst Hammeq")
        minimal = {
            path: content,
            reader: tc.read_file(tc.REPO_ROOT, reader),
            **{f"tests/A/Other{i}.cs": test_file("T", f"Other{i}") for i in range(4)},
        }
        for name, files in (("minimal", minimal),
                            ("repository", tc.collect_files(tc.REPO_ROOT, [path]))):
            with self.subTest(corpus=name):
                files[path] = content.replace(old, new)
                selection = tc.select_tests([path], files, {
                    path: self.diff(plus=[new], minus=[old]),
                })
                self.assertIn("SodRpg.Core.Tests.ContentTableV129Tests", selection.classes)
                self.assertFalse(selection.run_all)
                self.assertNotIn("T.Other0", selection.classes)

    def test_comment_only_change_selects_nothing(self):
        selection = tc.select_tests([CORE], self.corpus(), {CORE: self.diff(
            plus=["    // fixed a typo"], minus=["    // fixed a tpyo"])})
        self.assertFalse(selection.run_all)
        self.assertEqual(selection.classes, set())

    def test_unparsable_hunks_fall_back_to_file_types(self):
        # A reformatted body without recognizable tokens keeps the file-level mapping.
        selection = tc.select_tests([CORE], self.corpus(), {CORE: self.diff(
            plus=["    ;"], minus=[";"])})
        self.assertEqual(
            selection.classes,
            {"SodRpg.Core.Tests.StarSummaryTests", "SodRpg.Core.Tests.PartialWordTests"},
        )

    def test_wide_declared_type_change_still_runs_everything(self):
        files = {
            "tests/A/T1.cs": test_file("T", "T1") + "// uses StarSummary\n",
            "tests/A/T2.cs": test_file("T", "T2") + "// uses StarSummary\n",
            "tests/A/T3.cs": test_file("T", "T3"),
            CORE: CORE_CONTENT,
        }
        selection = tc.select_tests([CORE], files, {CORE: self.diff(
            plus=["public class StarSummary {"], minus=["public class StarSummary {"])})
        self.assertTrue(selection.run_all)

    def test_wide_member_token_is_dropped_not_escalated(self):
        # "Compute" is ubiquitous in test files; as a member token it must be
        # dropped (no full run) while the specific token still selects its tests.
        files = {
            "tests/A/T1.cs": test_file("T", "T1") + "// uses Compute\n",
            "tests/A/T2.cs": test_file("T", "T2") + "// uses Compute\n",
            "tests/A/T3.cs": test_file("T", "T3") + "// uses Compute\n",
            "tests/A/SpecificTests.cs": test_file("T", "SpecificTests") + '// uses unique.vigil_coif\n',
            CORE: CORE_CONTENT,
        }
        selection = tc.select_tests([CORE], files, {CORE: self.diff(
            plus=['            new UniqueDef("unique.vigil_coif"),  // Compute'],
            minus=['            new UniqueDef("unique.old"),  // Compute'])})
        self.assertFalse(selection.run_all)
        self.assertEqual(selection.classes, {"T.SpecificTests"})



class FilterChunkTests(unittest.TestCase):
    def test_chunks_preserve_terms_and_respect_limit(self):
        classes = [f"N.T.ClassNumber{i}Tests" for i in range(500)]
        chunks = tc.build_filters(classes, max_chars=1500)
        self.assertGreater(len(chunks), 1)
        self.assertTrue(all(len(c) <= 1500 for c in chunks))
        terms = "|".join(chunks).split("|")
        self.assertEqual(sorted(terms),
                         sorted(f"FullyQualifiedName~{c}" for c in classes))



class FakeRepoLayoutTests(unittest.TestCase):
    """scan_test_files + collect_files + select_tests on a temp directory."""

    def test_end_to_end_mapping_from_disk(self):
        files = {
            "src/SodRpg.Core/Game/Bonds.cs":
                "namespace C\n{\n    public class Bonds { }\n}\n",
            "tests/A/BondsTests.cs":
                "namespace T\n{\n    public class BondsTests { } // Bonds\n}\n",
            "tests/A/OtherTests.cs": test_file("T", "OtherTests"),
            "tests/A/obj/SkipMeTests.cs": test_file("T", "SkipMeTests"),
        }
        with tempfile.TemporaryDirectory() as tmp:
            root = fake_repo(files, Path(tmp) / "repo")
            corpus = tc.scan_test_files(root)
            self.assertEqual(
                set(corpus),
                {"tests/A/BondsTests.cs", "tests/A/OtherTests.cs"},
            )
            changed = ["src/SodRpg.Core/Game/Bonds.cs"]
            merged = tc.collect_files(root, changed)
            selection = tc.select_tests(changed, merged)
            self.assertFalse(selection.run_all)
            self.assertEqual(selection.classes, {"T.BondsTests"})

    def test_missing_changed_file_is_tolerated(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = fake_repo({"tests/A/OnlyTests.cs": test_file("T", "Only")}, Path(tmp) / "repo")
            merged = tc.collect_files(root, ["src/Gone.cs"])
            selection = tc.select_tests(["src/Gone.cs"], merged)
            self.assertEqual(selection.classes, set())
            self.assertFalse(selection.run_all)


class SummaryParsingTests(unittest.TestCase):
    def test_parse_dotnet_summary(self):
        output = "Some test output\nPassed!  - Failed:     2, Passed:    41, Skipped: 0\n"
        self.assertEqual(tc.parse_summary(output), (2, 41))
        fullwidth = "Passed! - Failed:     1，Passed:    30, Skipped: 0\n"
        self.assertEqual(tc.parse_summary(fullwidth), (1, 30))


if __name__ == "__main__":
    unittest.main()
