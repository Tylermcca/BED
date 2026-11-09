"""INVENTED scratch-only publish/metadata witness. Never execute a published file.

AUTHORING_PUBLISH_SEPARATION_CONFIG supplies private game_root/client_project/
host_project and optional fallback_packages. External game/SDK absence skips
clearly. Public code contains no private client name/path or game fixture body.
"""
from __future__ import annotations

import fnmatch
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

import pytest

FAMILY = "Brutal.Authoring"
PROBE_NAME = FAMILY + ".InspectionProbe"
PRUNED = {"bin", "obj", ".git", "codex", ".gate", "artifacts", ".vs", ".idea", "__pycache__"}
PROBE_SOURCE = r'''
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

internal static class Program
{
    public static int Main(string[] args)
    {
        var root=Path.GetFullPath(args[0]);var assemblies=new List<object>();var native=new List<string>();var invalid=new List<string>();
        foreach(var file in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal))
        {
            var extension=Path.GetExtension(file);
            if(!extension.Equals(".dll",StringComparison.OrdinalIgnoreCase) && !extension.Equals(".exe",StringComparison.OrdinalIgnoreCase))continue;
            var relative=Path.GetRelativePath(root,file).Replace('\\','/');
            try
            {
                using var stream=File.OpenRead(file);using var pe=new PEReader(stream);
                if(!pe.HasMetadata){native.Add(relative);continue;}
                var metadata=pe.GetMetadataReader();
                if(!metadata.IsAssembly){invalid.Add(relative);continue;}
                var name=metadata.GetString(metadata.GetAssemblyDefinition().Name);
                var refs=metadata.AssemblyReferences.Select(h=>metadata.GetString(metadata.GetAssemblyReference(h).Name)).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
                var spaces=metadata.TypeDefinitions.Select(h=>metadata.GetString(metadata.GetTypeDefinition(h).Namespace)).Where(x=>x.Length>0).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
                assemblies.Add(new {file=relative,name,references=refs,namespaces=spaces});
            }
            catch(BadImageFormatException){invalid.Add(relative);}
        }
        Console.WriteLine(JsonSerializer.Serialize(new {assemblies,native,invalid}));return 0;
    }
}
'''


def _linked(p):
    return p.is_symlink() or (hasattr(p, "is_junction") and p.is_junction())


def _under(p, root):
    return p == root or root in p.parents


def _sha(data):
    return hashlib.sha256(data).hexdigest()


def _canonical(data):
    if data.lstrip()[:1] not in (b"{", b"["):
        return None
    try:
        value = json.loads(data.decode("utf-8-sig"))
    except (UnicodeError, ValueError):
        return None
    return _sha(json.dumps(value, sort_keys=True, ensure_ascii=True, separators=(",", ":")).encode())


def _env(work, config=None):
    config = config or {}
    work.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ)
    for key in ("BrutalAuthoringRoot", "BaseIntermediateOutputPath", "OutputPath", "PublishDir"):
        env.pop(key, None)
    for key, folder in {"TEMP":"tmp", "TMP":"tmp", "DOTNET_CLI_HOME":"dotnet-home", "NUGET_PACKAGES":"packages", "NUGET_HTTP_CACHE_PATH":"http", "NUGET_PLUGINS_CACHE_PATH":"plugins", "ArtifactsPath":"artifacts"}.items():
        p=work/folder;p.mkdir(exist_ok=True);env[key]=str(p)
    fallback=Path(config.get("fallback_packages", str(Path.home()/".nuget/packages")))
    source=fallback if fallback.is_dir() else work/"empty-feed"
    source.mkdir(exist_ok=True) if source==work/"empty-feed" else None
    env.update(RestoreSources=str(source), RestoreFallbackFolders=str(source), NuGetAudit="false", UseSharedCompilation="false", MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0", UseArtifactsOutput="false", IncludeProjectNameInArtifactsPaths="true", EnableSourceControlManagerQueries="false", EnableSourceLink="false", PublishRepositoryUrl="false", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_ADD_GLOBAL_TOOLS_TO_PATH="false")
    return env


def _run(command, cwd, env, log, timeout=600):
    result=subprocess.run(command,cwd=cwd,env=env,capture_output=True,text=True,encoding="utf-8",errors="replace",timeout=timeout,creationflags=subprocess.CREATE_NO_WINDOW if os.name=="nt" else 0)
    log.write_text(result.stdout+result.stderr,encoding="utf-8")
    assert result.returncode==0,"command failed; inspect "+str(log)
    return result.stdout


@pytest.fixture(scope="module")
def probe(tmp_path_factory):
    dotnet=shutil.which("dotnet")
    if not dotnet:
        pytest.skip(".NET SDK absent; metadata probe/publish cannot run")
    work=tmp_path_factory.mktemp("metadata-probe");env=_env(work)
    sdks=_run([dotnet,"--list-sdks"],work,env,work/"sdks.log")
    if not sdks.strip():
        pytest.skip(".NET SDK absent; installed runtimes alone cannot publish")
    version=_run([dotnet,"--version"],work,env,work/"version.log").strip()
    match=re.fullmatch(r"(\d+)\.\d+\.\d+(?:[-+].*)?",version);assert match,"unrecognized SDK version"
    target="net"+match[1]+".0";project=work/"probe.csproj"
    (work/"Directory.Build.props").write_text('<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>',encoding="utf-8")
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>'+target+'</TargetFramework><AssemblyName>'+PROBE_NAME+'</AssemblyName></PropertyGroup></Project>',encoding="utf-8")
    (work/"Program.cs").write_text(PROBE_SOURCE,encoding="utf-8")
    command=[dotnet,"build",str(project),"-c","Release","--output",str(work/"built")]
    _run(command,work,env,work/"build.log")
    dll=work/"built"/(PROBE_NAME+".dll");assert dll.is_file()
    return {"dotnet":dotnet,"dll":dll,"work":work,"env":env,"sdk":version,"build_command":command}


def _metadata(probe, output, log):
    helper=probe["dll"].resolve(strict=True);output=output.resolve(strict=True)
    assert not _under(helper,output),"the built inspection helper must be outside published output"
    command=[probe["dotnet"],str(helper),str(output)]
    value=json.loads(_run(command,probe["work"],probe["env"],log))
    return value,command


def _copy_source(root, shadow):
    pins={}
    paths=[root/"src"]+[root/n for n in ("Directory.Build.props","Directory.Build.targets","Directory.Packages.props","global.json","NuGet.Config","nuget.config") if (root/n).is_file()]
    seen=set()
    for source in paths:
        files=[]
        if source.is_dir():
            for directory,dirs,names in os.walk(source,topdown=True,followlinks=False):
                dirs[:]=[x for x in dirs if x.casefold() not in PRUNED]
                for name in dirs:assert not _linked(Path(directory)/name),"linked source directory"
                files.extend(Path(directory)/n for n in names)
        else:
            files=[source]
        for p in files:
            if p in seen:continue
            seen.add(p);assert p.is_file() and not _linked(p)
            relative=p.relative_to(root);q=shadow/relative;q.parent.mkdir(parents=True,exist_ok=True)
            data=p.read_bytes();q.write_bytes(data);pins[str(p)]=_sha(data)
            if p.suffix.casefold() in {".csproj",".props",".targets"}:
                tree=ET.fromstring(data)
                assert not any(n.tag.rsplit("}",1)[-1] in {"Target","UsingTask","Exec"} for n in tree.iter()),"custom source build actions need review before an unattended publish"
    return pins


def _tool_inputs(root, host_relative):
    host=(root/host_relative).resolve(strict=True);assert _under(host,root)
    raw=host.read_bytes();tree=ET.fromstring(raw)
    values={n.tag.rsplit("}",1)[-1]:(n.text or "").strip() for n in tree.iter()}
    names={values.get("AssemblyName") or host.stem};spaces={values.get("RootNamespace") or host.stem}
    paths=list((root/"schemas").rglob("*.json"));copied_names={host.name.casefold()}
    for n in tree.iter():
        tag=n.tag.rsplit("}",1)[-1]
        copy=n.get("CopyToOutputDirectory") or next((c.text for c in n if c.tag.rsplit("}",1)[-1]=="CopyToOutputDirectory"),None)
        if tag in {"None","Content"} and copy and copy!="Never":
            include=n.get("Include","");assert include and not re.search(r"[$@%]\(|[?*;]",include),"unresolved host content"
            p=(host.parent/include.replace("\\",os.sep)).resolve(strict=True);assert _under(p,root);paths.append(p);copied_names.add(p.name.casefold())
            for child in n:
                if child.tag.rsplit("}",1)[-1] in {"Link","TargetPath"}:copied_names.add(Path(child.text or "").name.casefold())
    pins={str(host):_sha(raw)};exact={_sha(raw)};canonical=set()
    for p in paths:
        assert p.is_file() and not _linked(p);data=p.read_bytes();digest=_sha(data);pins[str(p)]=digest;exact.add(digest)
        c=_canonical(data)
        if c:canonical.add(c)
    return {"hosts":names,"host_namespaces":spaces,"tool_names":copied_names,"exact":exact,"canonical":canonical,"inputs":pins}


def _blocked(name, hosts):
    value=name.split("/",1)[0].split(",",1)[0].casefold()
    for suffix in (".dll",".exe"):
        if value.endswith(suffix):value=value[:-len(suffix)]
    return value.startswith(FAMILY.casefold()) or any(value==x.casefold() or value.startswith(x.casefold()+".") for x in hosts)


def _manifest(output):
    rows=[]
    for p in sorted(output.rglob("*")):
        assert not _linked(p),"linked publish output"
        if p.is_file():
            data=p.read_bytes();rows.append({"path":p.relative_to(output).as_posix(),"bytes":len(data),"sha256":_sha(data)})
    assert rows,"empty published output"
    return rows


def _inspect(output, metadata, policy, client):
    rows=_manifest(output);assert not metadata["invalid"],"unreadable PE metadata"
    managed=metadata["assemblies"];assert any(x["name"]==client for x in managed),"client managed assembly absent; bundled/single-file output needs a different inspection"
    dependencies=[]
    for row in rows:
        rel=Path(row["path"]);name=rel.name.casefold()
        assert not _blocked(rel.name,policy["hosts"]),"forbidden assembly/host filename: "+rel.as_posix()
        assert "schemas" not in [x.casefold() for x in rel.parts] and not name.endswith(".schema.json") and name not in policy["tool_names"] and name!=".editorconfig","tool-only schema/editor file: "+rel.as_posix()
        data=(output/rel).read_bytes();c=_canonical(data)
        assert row["sha256"] not in policy["exact"] and (c is None or c not in policy["canonical"]),"renamed/reformatted tool-only content: "+rel.as_posix()
        if name.endswith(".deps.json"):
            doc=json.loads(data.decode("utf-8-sig"));dependencies.append(rel.as_posix())
            for key in doc.get("libraries",{}):assert not _blocked(key,policy["hosts"]),"forbidden deps library"
            for target in doc.get("targets",{}).values():
                for key,entry in target.items():
                    assert not _blocked(key,policy["hosts"]),"forbidden deps target"
                    for key in entry.get("dependencies",{}):assert not _blocked(key,policy["hosts"]),"forbidden deps dependency"
                    for kind in ("runtime","runtimeTargets","compile"):
                        for asset in entry.get(kind,{}):assert not _blocked(Path(asset.replace("\\","/")).name,policy["hosts"]),"forbidden deps asset"
    assert dependencies,"no dependency manifest; cannot inspect this publish mode"
    for row in managed:
        rel=Path(row["file"]);assert not rel.is_absolute() and ".." not in rel.parts and (output/rel).is_file(),"unsafe metadata file"
        assert not _blocked(row["name"],policy["hosts"]),"forbidden managed identity"
        assert not any(_blocked(x,policy["hosts"]) for x in row["references"]),"forbidden managed AssemblyRef"
        assert not any(x==p or x.startswith(p+".") for x in row["namespaces"] for p in {FAMILY,*policy["host_namespaces"]}),"forbidden managed namespace"
    return {"files":rows,"metadata":metadata,"dependency_manifests":dependencies}


def test_publish_configured_client_and_inspect_without_running_it(tmp_path,request):
    config_name=os.environ.get("AUTHORING_PUBLISH_SEPARATION_CONFIG")
    if not config_name:pytest.skip("external game absent; provide AUTHORING_PUBLISH_SEPARATION_CONFIG with private project paths")
    config=json.loads(Path(config_name).read_text(encoding="utf-8-sig"));root=Path(config["game_root"])
    if not root.exists():pytest.skip("external game tree absent; source-publish witness unavailable")
    root=root.resolve(strict=True);probe=request.getfixturevalue("probe");work=tmp_path/"publish-witness";work.mkdir()
    shadow=work/"source";shadow.mkdir();source_pins=_copy_source(root,shadow);policy=_tool_inputs(root,config["host_project"])
    relative=Path(config["client_project"]);assert not relative.is_absolute() and ".." not in relative.parts
    project=(shadow/relative).resolve(strict=True);assert _under(project,shadow)
    tree=ET.parse(project);client=next((n.text for n in tree.getroot().iter() if n.tag.rsplit("}",1)[-1]=="AssemblyName"),None) or project.stem
    output=work/"published";env=_env(work,config)
    command=[probe["dotnet"],"publish",str(project),"--output",str(output)]
    _run(command,shadow,env,work/"publish.log",timeout=900)
    before=_manifest(output);metadata,metadata_command=_metadata(probe,output,work/"metadata.log")
    inspection=_inspect(output,metadata,policy,client);assert before==_manifest(output),"inspection modified published output"
    for name,digest in {**source_pins,**policy["inputs"]}.items():assert _sha(Path(name).read_bytes())==digest,"original input changed"
    evidence={"sdk":probe["sdk"],"publish_command":command,"metadata_command":metadata_command,"helper_build_command":probe["build_command"],"client":client,"source_inputs":source_pins,"tool_inputs":policy["inputs"],"host_names":sorted(policy["hosts"]),"host_namespaces":sorted(policy["host_namespaces"]),"tool_only_names":sorted(policy["tool_names"]),"output":str(output),"inspection":inspection,"published_executables_executed":0,"original_inputs_unchanged":True,"publish_profile_files":[str(p.relative_to(shadow)) for p in shadow.rglob("*.pubxml")]}
    (work/"publish-evidence.json").write_text(json.dumps(evidence,indent=2)+"\n",encoding="utf-8")


def _fixture(tmp_path):
    output=tmp_path/"out";output.mkdir()
    (output/"Neutral.Client.dll").write_bytes(b"self-authored logical guard fixture")
    (output/"Neutral.Client.deps.json").write_text(json.dumps({"libraries":{"Neutral.Client/1.0":{}},"targets":{}}),encoding="utf-8")
    policy={"hosts":{"Native.Host"},"host_namespaces":{"Native.Host"},"tool_names":{"host-map.json"},"exact":set(),"canonical":set()}
    metadata={"assemblies":[{"file":"Neutral.Client.dll","name":"Neutral.Client","references":["System.Runtime"],"namespaces":["Neutral.Runtime"]}],"native":[],"invalid":[]}
    return output,policy,metadata


def test_neutral_runtime_and_legacy_editor_names_are_not_the_authoring_family(tmp_path):
    output,policy,meta=_fixture(tmp_path);meta["assemblies"][0]["references"].append("Legacy.Editor")
    assert len(_inspect(output,meta,policy,"Neutral.Client")["files"])==2


@pytest.mark.parametrize("name",["Brutal.Authoring.dll","BRUTAL.AUTHORING.EDITOR.EXE","Native.Host.exe","Native.Host.runtimeconfig.json","Native.Host.deps.json","Native.Host.pdb","schemas/table.json","table.SCHEMA.JSON","folder/host-map.json",".editorconfig"])
def test_filename_and_tool_content_guards_refuse_contamination(tmp_path,name):
    output,policy,meta=_fixture(tmp_path);p=output/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b"original control")
    with pytest.raises(AssertionError):_inspect(output,meta,policy,"Neutral.Client")


@pytest.mark.parametrize("kind",["assembly","reference","namespace","host","host-reference","host-namespace"])
def test_metadata_guards_refuse_renamed_or_embedded_authoring(tmp_path,kind):
    output,policy,meta=_fixture(tmp_path);row=meta["assemblies"][0]
    if kind=="assembly":meta["assemblies"].append({"file":"Neutral.Client.dll","name":FAMILY+".Hidden","references":[],"namespaces":[]})
    elif kind=="reference":row["references"].append(FAMILY+".Data")
    elif kind=="namespace":row["namespaces"].append(FAMILY+".Embedded")
    elif kind=="host":meta["assemblies"].append({"file":"Neutral.Client.dll","name":"Native.Host","references":[],"namespaces":[]})
    elif kind=="host-reference":row["references"].append("Native.Host")
    else:row["namespaces"].append("Native.Host.Embedded")
    with pytest.raises(AssertionError):_inspect(output,meta,policy,"Neutral.Client")


@pytest.mark.parametrize("kind",["library","target","dependency","asset"])
def test_dependency_manifest_guards_refuse_authoring(tmp_path,kind):
    output,policy,meta=_fixture(tmp_path);doc={"libraries":{},"targets":{"framework":{"Neutral.Client/1":{}}}}
    if kind=="library":doc["libraries"][FAMILY+"/1"]={}
    elif kind=="target":doc["targets"]["framework"]["Native.Host/1"]={}
    elif kind=="dependency":doc["targets"]["framework"]["Neutral.Client/1"]={"dependencies":{FAMILY+".Data":"1"}}
    else:doc["targets"]["framework"]["Neutral.Client/1"]={"runtime":{"elsewhere/Brutal.Authoring.dll":{}}}
    (output/"Neutral.Client.deps.json").write_text(json.dumps(doc),encoding="utf-8")
    with pytest.raises(AssertionError):_inspect(output,meta,policy,"Neutral.Client")


@pytest.mark.parametrize("canonical",[False,True])
def test_renamed_tool_only_json_is_refused(tmp_path,canonical):
    output,policy,meta=_fixture(tmp_path);original=b'{"original_schema_control":[1,2]}'
    if canonical:policy["canonical"].add(_canonical(original));content=b'{ "original_schema_control": [1, 2] }'
    else:policy["exact"].add(_sha(original));content=original
    (output/"neutral-data.json").write_bytes(content)
    with pytest.raises(AssertionError):_inspect(output,meta,policy,"Neutral.Client")


@pytest.mark.parametrize("kind",["unreadable","bundled","no-deps","unsafe"])
def test_unsupported_or_unreadable_artifact_modes_fail_closed(tmp_path,kind):
    output,policy,meta=_fixture(tmp_path)
    if kind=="unreadable":meta["invalid"]=["Neutral.Client.dll"]
    elif kind=="bundled":meta["assemblies"]=[]
    elif kind=="no-deps":(output/"Neutral.Client.deps.json").unlink()
    else:meta["assemblies"][0]["file"]="../elsewhere.dll"
    with pytest.raises(AssertionError):_inspect(output,meta,policy,"Neutral.Client")


def test_real_metadata_reader_finds_forbidden_identity_even_after_rename(tmp_path,probe):
    output=tmp_path/"control";output.mkdir();shutil.copyfile(probe["dll"],output/"Neutral.Client.dll")
    metadata,command=_metadata(probe,output,tmp_path/"renamed-metadata.log")
    assert metadata["assemblies"][0]["name"]==PROBE_NAME
    assert not _under(Path(command[1]).resolve(),output.resolve())
    policy={"hosts":set(),"host_namespaces":set(),"tool_names":set(),"exact":set(),"canonical":set()}
    # Client identity is intentionally the probe here: the actual identity guard must still reject it.
    (output/"Neutral.Client.deps.json").write_text('{"libraries":{},"targets":{}}',encoding="utf-8")
    with pytest.raises(AssertionError,match="forbidden managed identity"):_inspect(output,metadata,policy,PROBE_NAME)


def test_renamed_host_project_is_also_tool_only_content(tmp_path):
    root=tmp_path/"source";root.mkdir()
    host=root/"Host.csproj";host.write_text('<Project><PropertyGroup><AssemblyName>Native.Host</AssemblyName></PropertyGroup></Project>',encoding="utf-8")
    policy=_tool_inputs(root,"Host.csproj")
    output,_,metadata=_fixture(tmp_path)
    (output/"neutral.bin").write_bytes(host.read_bytes())
    with pytest.raises(AssertionError,match="renamed/reformatted tool-only content"):_inspect(output,metadata,policy,"Neutral.Client")
