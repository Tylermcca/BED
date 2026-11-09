"""Bounded stdlib tests for the partial source detector; no C# execution."""
import unittest
from check_api_source_baseline import INFO, VERSION, changes, version_prefix


class ApiSourceBaselineTests(unittest.TestCase):
    def fixture(self):
        return {INFO: 'public const string PlanRevision = "worksheet_r2";\n',
                VERSION: 'public readonly record struct ApiVersion(int Major, int Minor, int Patch)\n{\n}\n'}

    def test_same_snapshots_are_unchanged(self):
        self.assertEqual([], changes(self.fixture(), self.fixture()))

    def test_changed_signature_is_reported(self):
        old = self.fixture(); new = dict(old)
        new[VERSION] = new[VERSION].replace("int Patch", "long Patch")
        self.assertTrue(changes(old, new))

    def test_public_constant_change_is_reported(self):
        old = self.fixture(); new = dict(old)
        new[INFO] = new[INFO].replace("worksheet_r2", "worksheet_r3")
        self.assertTrue(changes(old, new))

    def test_missing_scope_is_an_error_not_a_pass(self):
        with self.assertRaises(ValueError):
            changes(self.fixture(), {INFO: self.fixture()[INFO]})

    def test_later_declarations_are_not_consumed(self):
        prefix = self.fixture()[VERSION]
        def lines():
            yield from prefix.splitlines(keepends=True)
            raise AssertionError("must not consume the next declaration")
        self.assertEqual(prefix, version_prefix(lines()))

    def test_unrecognized_boundary_fails_closed(self):
        with self.assertRaises(ValueError):
            version_prefix(["public class Other {}\n"])
        with self.assertRaises(ValueError):
            version_prefix(["public readonly record struct ApiVersion(int Major)\n", "{\n"])


if __name__ == "__main__":
    unittest.main()
