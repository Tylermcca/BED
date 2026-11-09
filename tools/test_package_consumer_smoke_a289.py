"""Pure synthetic checks. Never run dotnet or read actual packages."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

spec = importlib.util.spec_from_file_location('package_smoke', Path(__file__).with_name('package_consumer_smoke.py'))
s = importlib.util.module_from_spec(spec)
spec.loader.exec_module(s)


def example():
    # Synthetic IDs/URL only; this is not an adopted release manifest.
    notice = b'Synthetic notice\n'
    return {'format': 1, 'review': 'synthetic fixture only', 'source_revision': 'a' * 40,
            'sdk': '10.0.100', 'repository_url': 'https://invalid.test/synthetic/library', 'version': '1.2.3',
            'source_files': {'src/A.csproj': 'b' * 64, 'tests/T.csproj': 'c' * 64, 'sample/Program.cs': 'd' * 64},
            'dependency_files': {'test-only.1.0.0.nupkg': 'e' * 64},
            'packages': [{'id': 'Synthetic.Library', 'project': 'src/A.csproj',
                          'entries': ['Synthetic.Library.nuspec', 'LICENSE.txt', 'README.md', 'lib/net10.0/A.dll', 'lib/net10.0/A.xml'],
                          'notices': {'LICENSE.txt': s.sha(notice)}}],
            'tests': [{'project': 'tests/T.csproj', 'filter': 'FullyQualifiedName~SyntheticTest'}],
            'consumer': {'files': ['sample/Program.cs'], 'framework': 'net10.0'}}


def archive(m):
    p = m['packages'][0]
    return {'Synthetic.Library.nuspec': f'''<package><metadata><id>{p['id']}</id><version>{m['version']}</version>
<authors>Fixture</authors><description>Synthetic library</description>
<repository url="{m['repository_url']}" commit="{m['source_revision']}" />
<readme>README.md</readme><license type="file">LICENSE.txt</license></metadata></package>'''.encode(),
            'LICENSE.txt': b'Synthetic notice\n', 'README.md': b'Fixture readme',
            'lib/net10.0/A.dll': b'not a real DLL', 'lib/net10.0/A.xml': b'<doc />'}


class PackageSmokeTests(unittest.TestCase):
    def test_manifest_valid(self):
        self.assertEqual(s.validate(example())['format'], 1)

    def test_manifest_missing_fields(self):
        m = example(); del m['review']
        with self.assertRaises(s.Refusal): s.validate(m)

    def test_placeholder_metadata(self):
        m = example(); m['version'] = '0.0.0-local.a194'
        with self.assertRaises(s.Refusal): s.validate(m)

    def test_no_invented_defaults(self):
        m = example(); m['packages'] = []
        with self.assertRaises(s.Refusal): s.validate(m)

    def test_case_collision(self):
        m = example(); m['source_files']['src/a.csproj'] = 'b' * 64
        with self.assertRaises(s.Refusal): s.validate(m)

    def test_unsafe_paths(self):
        for name in ('../a', 'C:/a', '/a', 'a\\b', 'a//b', 'a/./b', 'a./b'):
            with self.subTest(name=name), self.assertRaises(s.Refusal): s.relative(name)

    def test_no_source_binary(self):
        m = example(); m['source_files']['a.dll'] = 'a' * 64
        with self.assertRaises(s.Refusal): s.validate(m)

    def test_scoped_tests_required(self):
        m = example(); m['tests'][0]['filter'] = ''
        with self.assertRaises(s.Refusal): s.validate(m)

    def test_reviewed_notice_required(self):
        m = example(); m['packages'][0]['notices'] = {}
        with self.assertRaises(s.Refusal): s.validate(m)

    def test_package_reference_only(self):
        xml = s.consumer_project(example())
        self.assertNotIn('ProjectReference', xml)
        self.assertIn('Version="[1.2.3]"', xml)
        self.assertIn('EnableDefaultCompileItems', xml)

    def test_no_execution_without_flag(self):
        with patch.object(s.subprocess, 'run', side_effect=AssertionError('SDK invoked')) as mocked:
            with self.assertRaises(s.Refusal):
                s.run(example(), Path('absent'), Path('absent'), Path('absent'))
            mocked.assert_not_called()

    def inspect(self, change=None, mutate_manifest=None):
        m = example(); blobs = archive(m)
        if change: change(blobs)
        if mutate_manifest: mutate_manifest(m)
        with tempfile.TemporaryDirectory(prefix='a289-') as folder:
            path = Path(folder) / 'synthetic.nupkg'
            with zipfile.ZipFile(path, 'w') as z:
                for name, data in blobs.items(): z.writestr(name, data)
            return s.inspect_package(path, m['packages'][0], m)

    def test_archive_inventory(self):
        self.assertEqual(len(self.inspect()), 5)

    def test_missing_notice(self):
        with self.assertRaises(s.Refusal): self.inspect(lambda b: b.pop('LICENSE.txt'))

    def test_changed_notice(self):
        with self.assertRaises(s.Refusal): self.inspect(lambda b: b.update({'LICENSE.txt': b'changed'}))

    def test_unlisted_entry(self):
        with self.assertRaises(s.Refusal): self.inspect(lambda b: b.update({'internal.txt': b'no'}))

    def test_archive_metadata_mismatch(self):
        with self.assertRaises(s.Refusal): self.inspect(mutate_manifest=lambda m: m.update(version='1.2.4'))

    def test_missing_readme(self):
        with self.assertRaises(s.Refusal): self.inspect(lambda b: b.update({'README.md': b''}))

    def test_pinned_inputs(self):
        with tempfile.TemporaryDirectory(prefix='a289-') as folder:
            root = Path(folder); (root / 'a.txt').write_bytes(b'hello')
            self.assertEqual(s.pinned_files(root, {'a.txt': s.sha(b'hello')}), {'a.txt': b'hello'})
            with self.assertRaises(s.Refusal): s.pinned_files(root, {'a.txt': '0' * 64})

    def test_consumer_assets(self):
        with tempfile.TemporaryDirectory(prefix='a289-') as folder:
            path = Path(folder) / 'assets.json'
            path.write_text(json.dumps({'libraries': {'Synthetic.Library/1.2.3': {'type': 'package'}}}))
            self.assertEqual(s.verify_assets(path, ['Synthetic.Library'], '1.2.3')['packages'], ['Synthetic.Library/1.2.3'])
            path.write_text(json.dumps({'libraries': {'Source/1.0': {'type': 'project'}}}))
            with self.assertRaises(s.Refusal): s.verify_assets(path, ['Synthetic.Library'], '1.2.3')


if __name__ == '__main__':
    unittest.main()
