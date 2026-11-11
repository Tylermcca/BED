"""Static PE and release-copy checks only: never load or execute the DLL or BED."""
import hashlib
import re
import struct
from pathlib import Path
import pytest
import package_release as pr

ROOT = Path(__file__).resolve().parent.parent
VENDOR = ROOT / 'third_party/accesskit/0.23.2'


def _pe():
    data = (VENDOR / 'accesskit.dll').read_bytes()
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    assert data[pe:pe+4] == b'PE\0\0'
    machine, count = struct.unpack_from('<HH', data, pe+4)
    assert machine == 0x8664, 'only AMD64 is approved'
    optional_size = struct.unpack_from('<H', data, pe+20)[0]
    optional = pe + 24
    assert struct.unpack_from('<H', data, optional)[0] == 0x20b
    sections = [struct.unpack_from('<8sIIII', data, optional+optional_size+i*40) for i in range(count)]
    def offset(rva):
        for _, size, address, raw_size, raw in sections:
            if address <= rva < address + max(size, raw_size):
                return raw + rva - address
        raise AssertionError(f'RVA {rva:x} has no section')
    return data, optional, offset


def test_pinned_native_dll_is_exact_release_bytes():
    assert hashlib.sha256((VENDOR/'accesskit.dll').read_bytes()).hexdigest() == pr.ACCESSKIT_DLL_SHA256
    assert (VENDOR/'accesskit.dll').stat().st_size == 359424
    assert sorted(p.name for p in VENDOR.iterdir()) == ['LICENSE-APACHE', 'LICENSE-MIT', 'accesskit.dll', 'accesskit.h']


def test_every_pinvoke_is_an_export_in_the_actual_amd64_dll():
    data, optional, offset = _pe()
    export_rva = struct.unpack_from('<I', data, optional+112)[0]
    table = offset(export_rva)
    count = struct.unpack_from('<I', data, table+24)[0]
    names_rva = struct.unpack_from('<I', data, table+32)[0]
    names = set()
    for i in range(count):
        rva = struct.unpack_from('<I', data, offset(names_rva)+i*4)[0]
        start = offset(rva)
        names.add(data[start:data.index(b'\0', start)].decode('ascii'))
    source = (ROOT/'src/Bed.Editor.ImGui/AccessKitNativeTreeApi.cs').read_text() + (ROOT/'src/Bed.Editor.App/AccessKitNativeWindowApi.cs').read_text()
    functions = re.findall(r'extern (?:nint|void) (accesskit_\w+)\(', source)
    assert len(functions) == 26  # A11Y01 adds the live-region and atomic setters
    assert set(functions) <= names
    assert source.count('DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)') == 26
    assert source.count('[MarshalAs(UnmanagedType.I1)] bool') == 2
    header = (VENDOR/'accesskit.h').read_text()
    assert all(name+'(' in header for name in functions)


def test_all_role_ordinals_are_checked_against_the_vendored_header():
    header = (VENDOR/'accesskit.h').read_text()
    enum = re.search(r'enum accesskit_role.*?\{(.*?)\};', header, re.S).group(1)
    names = re.findall(r'ACCESSKIT_ROLE_(\w+)', enum)
    mapping = {'Window':'WINDOW','MenuBar':'MENU_BAR','Menu':'MENU','MenuItem':'MENU_ITEM','Button':'BUTTON',
        'Link':'LINK','Label':'LABEL','TextInput':'TEXT_INPUT','CheckBox':'CHECK_BOX','ComboBox':'COMBO_BOX',
        'ListItem':'LIST_ITEM','Table':'TABLE','Row':'ROW','Cell':'CELL','TreeItem':'TREE_ITEM','Tab':'TAB','Dialog':'DIALOG','Status':'STATUS','Canvas':'CANVAS'}
    source = (ROOT/'src/Bed.Editor.ImGui/AccessKitTreeEncoder.cs').read_text()
    actual = {name:int(number) for name,number in re.findall(r'AccessKitProjectedRole\.(\w+) => (\d+)', source)}
    assert actual == {name:names.index(native) for name,native in mapping.items()}


def test_copy_places_only_the_dll_and_licences_in_the_release(tmp_path):
    pr.copy_accesskit(tmp_path, 'win-x64')
    assert (tmp_path/'accesskit.dll').read_bytes() == (VENDOR/'accesskit.dll').read_bytes()
    assert 'accesskit.dll' in pr.REQUIRED and 'accesskit.dll' in pr.TOP_LEVEL
    for name in ('MIT', 'APACHE'):
        rel = 'licenses/AccessKit-LICENSE-'+name+'.txt'
        assert rel in pr.REQUIRED
        assert (tmp_path/rel).read_bytes() == (VENDOR/('LICENSE-'+name)).read_bytes()
    assert sorted(p.relative_to(tmp_path).as_posix() for p in tmp_path.rglob('*') if p.is_file()) == [
        'accesskit.dll','licenses/AccessKit-LICENSE-APACHE.txt','licenses/AccessKit-LICENSE-MIT.txt']
    assert pr.untidy(tmp_path) == []


@pytest.mark.parametrize('rid', ['win-arm64','win-x86','linux-x64','osx-x64'])
def test_other_targets_refuse_without_staging_a_wrong_dll(tmp_path, rid):
    with pytest.raises(RuntimeError, match='only for win-x64'):
        pr.copy_accesskit(tmp_path, rid)
    assert not list(tmp_path.iterdir())


def test_tampered_dll_refuses_before_any_copy(monkeypatch, tmp_path):
    source = tmp_path/'source/third_party/accesskit/0.23.2'
    source.mkdir(parents=True)
    (source/'accesskit.dll').write_bytes(b'not the approved DLL')
    monkeypatch.setattr(pr, 'ROOT', tmp_path/'source')
    destination = tmp_path/'stage'
    with pytest.raises(RuntimeError, match='differs from the approved'):
        pr.copy_accesskit(destination, 'win-x64')
    assert not destination.exists()

@pytest.mark.parametrize('rid', ['win-arm64','win-x86','linux-x64','osx-x64'])
def test_other_targets_refuse_before_any_publish(monkeypatch, tmp_path, rid):
    def forbidden(*args, **kwargs):
        pytest.fail('wrong-target release must not start a publish')
    monkeypatch.setattr(pr, 'publish', forbidden)
    with pytest.raises(RuntimeError, match='only for win-x64'):
        pr.build(tmp_path/'stage', rid)
    assert not (tmp_path/'stage').exists()
