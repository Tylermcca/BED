"""Explicit, local-feed-only package verification. No SDK call without --execute.

MIXED: local-feed isolation from docs/release/packaging_dry_run.md; reviewed
manifest and archive checks are original. Trusted build inputs, not a sandbox.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zipfile


class Refusal(ValueError):
    pass


def require(ok, message):
    if not ok:
        raise Refusal(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def relative(value):
    require(isinstance(value, str) and value and '\\' not in value and ':' not in value,
            'Expected a POSIX relative file path')
    p = PurePosixPath(value)
    require(not p.is_absolute() and all(x not in ('', '.', '..') for x in value.split('/')),
            'Unsafe relative path: ' + value)
    require(not any(x.rstrip(' .') != x for x in p.parts), 'Ambiguous path: ' + value)
    return value


def digest(value):
    require(isinstance(value, str) and re.fullmatch('[0-9a-f]{64}', value), 'Expected lowercase SHA256')
    return value


def text(value, label):
    require(isinstance(value, str) and bool(value.strip()), 'Missing ' + label)
    require(not any(s in value.lower() for s in ('replace', 'example.invalid', 'placeholder', 'todo', '0.0.0-local')),
            'Unresolved placeholder: ' + label)
    return value


def file_map(value):
    require(isinstance(value, dict) and value, 'Empty input file map')
    require(len({p.casefold() for p in value}) == len(value), 'Case-colliding paths')
    for p, h in value.items():
        relative(p)
        digest(h)
    return value


def validate(m):
    require(isinstance(m, dict) and m.get('format') == 1, 'Manifest format must be 1')
    require(set(m) == {'format', 'review', 'source_revision', 'sdk', 'repository_url', 'version',
                       'source_files', 'dependency_files', 'packages', 'tests', 'consumer'},
            'Manifest fields differ from documented contract')
    text(m['review'], 'review reference')
    require(re.fullmatch('[0-9a-f]{40}', m['source_revision'] or ''), 'Exact source revision required')
    require(re.fullmatch(r'\d+\.\d+\.\d+', m['sdk'] or ''), 'Exact installed release SDK required')
    url = text(m['repository_url'], 'repository URL')
    require(re.fullmatch(r'https://[A-Za-z0-9.-]+/[A-Za-z0-9._~/-]+', url), 'Public HTTPS repository URL required')
    version = text(m['version'], 'version')
    require(re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*)?', version),
            'Exact SemVer required')
    sources = file_map(m['source_files'])
    file_map(m['dependency_files'])
    for p in m['dependency_files']:
        require('/' not in p and p.endswith('.nupkg'), 'Dependency feed must list flat nupkg files')
    for p in sources:
        require(not any(x.lower() in ('art', 'reference', '.git', 'bin', 'obj', 'codex', 'node_modules') for x in PurePosixPath(p).parts),
                'Source closure contains excluded directory')
        require(PurePosixPath(p).suffix.lower() not in ('.dll', '.exe', '.pdb', '.nupkg'), 'Source closure contains binary')
    require(isinstance(m['packages'], list) and m['packages'], 'Explicit package selection required')
    ids, projects = set(), set()
    for p in m['packages']:
        require(set(p) == {'id', 'project', 'entries', 'notices'}, 'Invalid package contract')
        pid = text(p['id'], 'package id')
        require(re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]*', pid), 'Invalid package id')
        require(pid.casefold() not in ids, 'Duplicate package id')
        ids.add(pid.casefold())
        project = relative(p['project'])
        require(project in sources and project.endswith('.csproj') and project not in projects, 'Unpinned/duplicate project')
        projects.add(project)
        require(isinstance(p['entries'], list) and p['entries'], 'Exact archive entry allowlist required')
        require(len({x.casefold() for x in p['entries']}) == len(p['entries']), 'Duplicate archive entries')
        for entry in p['entries']:
            relative(entry)
        notices = file_map(p['notices'])
        require(set(notices) <= set(p['entries']), 'Notice missing from archive allowlist')
        require(any(x.lower().endswith('.dll') for x in p['entries']), 'Package has no library')
        require(any(x.lower().endswith('.xml') for x in p['entries']), 'Package has no XML documentation')
    require(isinstance(m['tests'], list) and m['tests'], 'Explicit scoped test list required')
    for t in m['tests']:
        require(set(t) == {'project', 'filter'}, 'Invalid test contract')
        require(relative(t['project']) in sources and t['project'].endswith('.csproj'), 'Unpinned test project')
        f = text(t['filter'], 'test filter')
        require('FullyQualifiedName' in f and not any(x in f for x in ('\n', '\r')), 'Named scoped test filter required')
    c = m['consumer']
    require(isinstance(c, dict) and set(c) == {'files', 'framework'}, 'Invalid consumer contract')
    require(re.fullmatch(r'net\d+\.\d+', c['framework'] or ''), 'Managed consumer framework required')
    require(isinstance(c['files'], list) and c['files'], 'Pinned original consumer C# required')
    for p in c['files']:
        require(relative(p) in sources and p.endswith('.cs'), 'Unpinned consumer C#')
    return m


def pinned_files(root, entries):
    """Read only named files; do not enumerate a checkout or machine cache."""
    result = {}
    root = root.resolve(strict=True)
    require(not str(root).startswith(('\\\\', '//')), 'Local inputs only')
    for name, expected in entries.items():
        path = root / relative(name)
        current = path
        while current != root:
            require(not current.is_symlink() and not getattr(current, 'is_junction', lambda: False)(), 'Linked input refused')
            current = current.parent
        require(path.is_file() and path.resolve().is_relative_to(root), 'Missing/nonlocal input: ' + name)
        data = path.read_bytes()
        require(sha(data) == expected, 'Input hash mismatch: ' + name)
        result[name] = data
    return result


def inspect_package(path, package, manifest):
    """Exact file allowlist and pinned notices, not a licence compatibility opinion."""
    with zipfile.ZipFile(path) as archive:
        info = archive.infolist()
        names = [i.filename for i in info]
        require(len({x.casefold() for x in names}) == len(names), 'Duplicate archive entries')
        for i in info:
            relative(i.filename)
            require(not i.is_dir() and i.file_size <= 128 * 1024 * 1024, 'Unexpected directory or oversized entry')
            require((i.external_attr >> 16) & 0o170000 != 0o120000, 'Archive symlink refused')
        require(sum(i.file_size for i in info) <= 512 * 1024 * 1024, 'Archive too large')
        require(set(names) == set(package['entries']), 'Package file set differs from reviewed allowlist')
        blobs = {name: archive.read(name) for name in names}
    nuspecs = [n for n in names if n.endswith('.nuspec')]
    require(len(nuspecs) == 1, 'Exactly one nuspec required')
    raw = blobs[nuspecs[0]]
    require(b'<!DOCTYPE' not in raw.upper() and b'<!ENTITY' not in raw.upper(), 'Nuspec DTD refused')
    tree = ET.fromstring(raw)
    metadata = next((e for e in tree if e.tag.split('}')[-1] == 'metadata'), None)
    require(metadata is not None, 'Missing nuspec metadata')
    children = {e.tag.split('}')[-1]: e for e in metadata}
    for field, expected in [('id', package['id']), ('version', manifest['version'])]:
        require(field in children and children[field].text == expected, 'Wrong nuspec ' + field)
    for field in ('authors', 'description'):
        require(field in children, 'Missing ' + field)
        text(children[field].text, field)
    repository = children.get('repository')
    require(repository is not None and repository.get('url') == manifest['repository_url'] and
            repository.get('commit') == manifest['source_revision'], 'Repository provenance mismatch')
    readme = children.get('readme')
    require(readme is not None and readme.text in blobs and blobs[readme.text].strip(), 'Missing package README')
    licence = children.get('license')
    require(licence is not None and licence.text, 'Missing package licence metadata')
    if licence.get('type') == 'file':
        require(licence.text in package['notices'], 'Licence file is not pinned')
    else:
        require(licence.get('type') == 'expression', 'Unknown licence metadata')
        text(licence.text, 'licence expression')
    for name, expected in package['notices'].items():
        require(sha(blobs[name]) == expected, 'Notice hash mismatch: ' + name)
    return {name: {'size': len(data), 'sha256': sha(data)} for name, data in sorted(blobs.items())}


def consumer_project(manifest):
    root = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = ET.SubElement(root, 'PropertyGroup')
    for key, value in {'TargetFramework': manifest['consumer']['framework'], 'OutputType': 'Exe',
                       'ImplicitUsings': 'enable', 'Nullable': 'enable', 'EnableDefaultCompileItems': 'false'}.items():
        ET.SubElement(props, key).text = value
    items = ET.SubElement(root, 'ItemGroup')
    for p in manifest['packages']:
        ET.SubElement(items, 'PackageReference', Include=p['id'], Version='[' + manifest['version'] + ']')
    for i, _ in enumerate(manifest['consumer']['files']):
        ET.SubElement(items, 'Compile', Include=f'Input{i}.cs')
    return ET.tostring(root, encoding='unicode')


def verify_assets(path, package_ids, version):
    assets = json.loads(path.read_text(encoding='utf-8'))
    libraries = assets.get('libraries', {})
    require(libraries and all(x.get('type') == 'package' for x in libraries.values()), 'Consumer has non-package libraries')
    keys = {x.casefold() for x in libraries}
    require(all((pid + '/' + version).casefold() in keys for pid in package_ids), 'Consumer did not resolve exact produced packages')
    return {'sha256': sha(path.read_bytes()), 'packages': sorted(libraries)}


def run(manifest, repo, feed, work, execute=False):
    validate(manifest)
    # Validation without explicit execution never reads packages, creates work or invokes an SDK.
    require(execute, 'Preparation only: --execute and separate execution authorization are required')
    repo, feed, work = repo.resolve(strict=True), feed.resolve(strict=True), work.resolve()
    require(not work.exists(), 'Work path must not already exist')
    require(not work.is_relative_to(repo) and not repo.is_relative_to(work) and
            not work.is_relative_to(feed) and not feed.is_relative_to(work), 'Work must be outside source and feed')
    sources = pinned_files(repo, manifest['source_files'])
    dependencies = pinned_files(feed, manifest['dependency_files'])
    work.mkdir(parents=False)
    receipts = {'manifest': manifest, 'commands': [], 'status': 'incomplete'}
    def save():
        (work / 'receipt.json').write_text(json.dumps(receipts, indent=2) + '\n', encoding='utf-8')
    env = {k: v for k, v in os.environ.items() if k.upper() in
           {'PATH', 'SYSTEMROOT', 'WINDIR', 'COMSPEC', 'PATHEXT', 'PROCESSOR_ARCHITECTURE'}}
    env.update({'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1', 'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
                'DOTNET_GENERATE_ASPNET_CERTIFICATE': 'false', 'DOTNET_NOLOGO': '1',
                'MSBUILDDISABLENODEREUSE': '1', 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE': 'true'})
    for key, directory in {'DOTNET_CLI_HOME': 'cli', 'NUGET_PACKAGES': 'cache', 'NUGET_HTTP_CACHE_PATH': 'http',
                           'NUGET_PLUGINS_CACHE_PATH': 'plugins', 'TEMP': 'tmp', 'TMP': 'tmp',
                           'HOME': 'home', 'USERPROFILE': 'home', 'APPDATA': 'appdata', 'LOCALAPPDATA': 'localappdata'}.items():
        dest = work / directory
        dest.mkdir(exist_ok=True)
        env[key] = str(dest)
    def command(args, cwd, timeout=600):
        record = {'argv': args, 'cwd': str(cwd), 'timeout_seconds': timeout}
        receipts['commands'].append(record)
        save()
        started = time.monotonic()
        try:
            done = subprocess.run(args, cwd=cwd, env=env, stdin=subprocess.DEVNULL,
                                  stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout, check=False)
            record['exit_code'] = done.returncode
            output = done.stdout
        except subprocess.TimeoutExpired as exc:
            record['timed_out'] = True
            output = exc.stdout or b''
            raise Refusal('Command timed out; inspect owned execution before any retry') from exc
        finally:
            record['elapsed_seconds'] = round(time.monotonic() - started, 3)
            if 'output' in locals():
                log = f'command-{len(receipts["commands"]):02}.log'
                (work / log).write_bytes(output)
                record['log'] = log
            save()
        require(done.returncode == 0, 'Command failed: ' + args[0])
        return output.decode('utf-8', errors='replace').strip()
    try:
        src, local_feed, consumer, packages = (work / x for x in ('source', 'feed', 'consumer', 'packages'))
        for d in (src, local_feed, consumer, packages):
            d.mkdir()
        for name, data in sources.items():
            dest = src / name
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(data)
        for name, data in dependencies.items():
            (local_feed / name).write_bytes(data)
        # Outer repository configuration never leaks into the new workspace.
        for name in ('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props'):
            (work / name).write_text('<Project />\n', encoding='utf-8')
        sdk = json.dumps({'sdk': {'version': manifest['sdk'], 'rollForward': 'disable', 'allowPrerelease': False}})
        (work / 'global.json').write_text(sdk, encoding='utf-8')
        (src / 'global.json').write_text(sdk, encoding='utf-8')
        config = ET.Element('configuration')
        package_sources = ET.SubElement(config, 'packageSources')
        ET.SubElement(package_sources, 'clear')
        ET.SubElement(package_sources, 'add', key='reviewed-local', value=str(local_feed))
        ET.SubElement(ET.SubElement(config, 'fallbackPackageFolders'), 'clear')
        config_path = work / 'NuGet.Config'
        config_path.write_text(ET.tostring(config, encoding='unicode'), encoding='utf-8')
        require(command(['dotnet', '--version'], work, 30) == manifest['sdk'], 'Installed SDK differs')
        flags = ['-p:NuGetAudit=false', '-p:UseSharedCompilation=false', '-p:EnableSourceControlManagerQueries=false',
                 '-p:EnableSourceLink=false', '-p:RestoreSources=' + str(local_feed), '-p:RestoreFallbackFolders=']
        def restore(project, cwd):
            command(['dotnet', 'restore', project, '--configfile', str(config_path), '--source', str(local_feed),
                     '--no-http-cache', '--packages', env['NUGET_PACKAGES']] + flags, cwd)
        for package in manifest['packages']:
            project = package['project']
            restore(project, src)
            command(['dotnet', 'build', project, '-c', 'Release', '--no-restore', '--disable-build-servers'] + flags, src)
        for test in manifest['tests']:
            restore(test['project'], src)
            command(['dotnet', 'test', test['project'], '-c', 'Release', '--no-restore', '--filter', test['filter'],
                     '--blame-hang-timeout', '10m'] + flags, src, 720)
        inventory = {}
        for i, package in enumerate(manifest['packages']):
            output = packages / str(i)
            output.mkdir()
            command(['dotnet', 'pack', package['project'], '-c', 'Release', '--no-build', '--no-restore',
                     '--output', str(output)] + flags, src)
            archives = list(output.iterdir())
            require(len(archives) == 1 and archives[0].suffix == '.nupkg',
                    'Expected exactly one ordinary nupkg; symbols need a separate reviewed route')
            archive = archives[0]
            inventory[package['id']] = {'archive_sha256': sha(archive.read_bytes()),
                                       'entries': inspect_package(archive, package, manifest)}
            destination = local_feed / archive.name
            require(not destination.exists(), 'Produced package collides with dependency feed')
            shutil.copyfile(archive, destination)
        receipts['archives'] = inventory
        (consumer / 'Consumer.csproj').write_text(consumer_project(manifest), encoding='utf-8')
        for name in ('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props'):
            (consumer / name).write_text('<Project />\n', encoding='utf-8')
        for i, name in enumerate(manifest['consumer']['files']):
            (consumer / f'Input{i}.cs').write_bytes(sources[name])
        env['NUGET_PACKAGES'] = str(work / 'consumer-cache')
        Path(env['NUGET_PACKAGES']).mkdir()
        restore('Consumer.csproj', consumer)
        receipts['consumer_assets'] = verify_assets(consumer / 'obj/project.assets.json',
                                                     [p['id'] for p in manifest['packages']], manifest['version'])
        command(['dotnet', 'build', 'Consumer.csproj', '-c', 'Release', '--no-restore', '--disable-build-servers'] + flags, consumer)
        # Deliberately no app/consumer launch; compilation is the bounded smoke assertion.
        receipts['status'] = 'passed-build-only-smoke'
    except Exception as exc:
        receipts['status'] = 'failed'
        receipts['error'] = str(exc)
        raise
    finally:
        save()
    return receipts


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--repo', type=Path)
    parser.add_argument('--feed', type=Path)
    parser.add_argument('--work', type=Path)
    parser.add_argument('--execute', action='store_true')
    a = parser.parse_args(argv)
    try:
        manifest = validate(json.loads(a.manifest.read_text(encoding='utf-8')))
        if not a.execute:
            print('Manifest structure accepted. No SDK, package verification or release approval performed.')
            return 0
        require(all((a.repo, a.feed, a.work)), '--execute needs --repo, --feed and --work')
        run(manifest, a.repo, a.feed, a.work, True)
        print('Local build-only package smoke passed. Not publication or runtime approval.')
        return 0
    except (Refusal, OSError, ValueError, ET.ParseError, zipfile.BadZipFile) as exc:
        print('REFUSED:', exc, file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
