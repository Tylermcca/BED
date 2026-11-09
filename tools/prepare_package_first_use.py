"""Prepare a package-first source workspace; NEVER invoke an SDK or consumer.

Uses the existing package_consumer_smoke.validate/consumer_project contract.
Does not follow manifest source paths or read package/cache bytes or remote feeds.
INVENTED starter preparation; existing manifest/project rendering is reused.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib
import json
from pathlib import Path
import re
import sys
from urllib.parse import urlsplit
import xml.etree.ElementTree as ET

class Refusal(ValueError):
    pass

def need(ok, why):
    if not ok: raise Refusal(why)

def sha(data): return hashlib.sha256(data).hexdigest()

def reviewed(value, label):
    need(isinstance(value,str) and value.strip(), "Missing reviewed " + label)
    need(not re.search(r"\{\{|(?:^|[^A-Za-z])(?:APPROVED|REVIEWED|REQUIRED|PENDING|YOUR)_[A-Z0-9_]+",value),
         "Unfilled input marker: " + label)
    need(not any(x in value.casefold() for x in ["placeholder","example.invalid","replace-me","todo"]),
         "Unfilled input marker: " + label)
    return value

def tutorial_url(value):
    reviewed(value,"tutorial URL")
    url=urlsplit(value)
    need(url.scheme=="https" and url.hostname and not url.username and not url.password
         and not url.query and not url.fragment and url.path.startswith("/"),"Provide a reviewed public HTTPS tutorial URL")
    return value

def prepare(manifest, program, readme, data_package_id, feed, out,
            start_tutorial_url, adapter_tutorial_url, contract=None):
    contract = contract or importlib.import_module("package_consumer_smoke")
    # Pure validation/project rendering only; never contract.run or inspect_package.
    contract.validate(manifest)
    role=reviewed(data_package_id,"Data-role package ID")
    need(role in [p["id"] for p in manifest["packages"]],"Data-role ID must exactly match a reviewed selected package")
    need(manifest["consumer"]["framework"]=="net10.0","This prepared API example targets net10.0")
    need(int(manifest["sdk"].split(".")[0])>=10,"A .NET 10-compatible exact installed SDK is required")
    need(len(manifest["consumer"]["files"])==1,"This starter composes with exactly one pinned consumer source")
    source_name=manifest["consumer"]["files"][0]
    need(Path(program).suffix==".cs","Explicit C# starter source required")
    code=Path(program).read_bytes()
    need(len(code)<=64*1024,"Starter source exceeds its small-example bound")
    need(sha(code)==manifest["source_files"][source_name],"Program source hash differs from reviewed consumer input")
    code_text=code.decode("utf-8-sig")
    need(Path(readme).suffix==".md","Explicit Markdown template required")
    template=Path(readme).read_text(encoding="utf-8")
    replacements={"DATA_PACKAGE_ID":role,"VERSION":manifest["version"],"SDK":manifest["sdk"],
                  "FRAMEWORK":manifest["consumer"]["framework"],"START_TUTORIAL_URL":tutorial_url(start_tutorial_url),
                  "ADAPTER_TUTORIAL_URL":tutorial_url(adapter_tutorial_url),"PROGRAM":code_text.rstrip()}
    for name,value in replacements.items():
        need("{{"+name+"}}" in template,"README template missing required slot: "+name)
        template=template.replace("{{"+name+"}}",value)
    need(re.search(r"\{\{[A-Z][A-Z0-9_]*\}\}",template) is None,"README has an unresolved reviewed-input slot")

    feed=Path(feed)
    need(not str(feed).startswith(("\\\\","//")) and "://" not in str(feed),"Local directory feed required")
    feed=feed.resolve(strict=True);need(feed.is_dir(),"Local feed directory is missing")
    out=Path(out)
    need(not out.exists() and not out.is_symlink(),"Output must be a NEW directory; keep existing evidence")
    parent=out.parent.resolve(strict=True);need(parent.is_dir(),"Output parent must exist")
    out=parent/out.name
    need(not out.is_relative_to(feed) and not feed.is_relative_to(out),"Output and local feed must not overlap")

    project=contract.consumer_project(manifest)
    xml=ET.Element("configuration")
    sources=ET.SubElement(xml,"packageSources");ET.SubElement(sources,"clear")
    ET.SubElement(sources,"add",key="reviewed-local",value=str(feed))
    ET.SubElement(ET.SubElement(xml,"fallbackPackageFolders"),"clear")
    environment={
        "DOTNET_GENERATE_ASPNET_CERTIFICATE":"false","DOTNET_SKIP_FIRST_TIME_EXPERIENCE":"1",
        "DOTNET_CLI_TELEMETRY_OPTOUT":"1","DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE":"true",
        "UseSharedCompilation":"false","MSBUILDDISABLENODEREUSE":"1","DOTNET_CLI_USE_MSBUILD_SERVER":"0",
        "NuGetAudit":"false","NUGET_PACKAGES":str(out/".packages"),
        "NUGET_HTTP_CACHE_PATH":str(out/".http"),"NUGET_PLUGINS_CACHE_PATH":str(out/".plugins"),
        "DOTNET_CLI_HOME":str(out/".cli"),"TEMP":str(out/".tmp"),"TMP":str(out/".tmp")
    }
    outputs={"Input0.cs":code,"Consumer.csproj":(project+"\n").encode(),
             "NuGet.Config":(ET.tostring(xml,encoding="unicode")+"\n").encode(),
             "global.json":(json.dumps({"sdk":{"version":manifest["sdk"],"rollForward":"disable","allowPrerelease":False}},indent=2)+"\n").encode(),
             "build-environment.json":(json.dumps(environment,indent=2)+"\n").encode(),
             "README.md":template.encode()}
    for name in ["Directory.Build.props","Directory.Build.targets","Directory.Packages.props"]:
        outputs[name]=b"<Project />\n"
    receipt={"status":"prepared-text-only","SDK_run":False,"packages_read":False,"consumer_compiled":False,"consumer_run":False,
             "approval_inferred":False,"manifest_semantic_sha256":sha(json.dumps(manifest,sort_keys=True,separators=(",",":")).encode()),
             "review_reference":manifest["review"],"consumer_source_file":"Input0.cs","consumer_source_sha256":sha(code),
             "template_sha256":sha(Path(readme).read_bytes()),"data_role_input":role,
             "empty_directories":[".packages",".http",".plugins",".cli",".tmp"],
             "all_reviewed_package_references_preserved":[p["id"] for p in manifest["packages"]],
             "tutorial_links":"syntax only; existence and publication not checked",
             "outputs":{name:sha(data) for name,data in sorted(outputs.items())}}
    # No overwrite, recursive move/delete, package/cache copy or command execution.
    out.mkdir()
    for name in receipt["empty_directories"]:(out/name).mkdir()
    for name,data in outputs.items():(out/name).write_bytes(data)
    (out/"preparation-receipt.json").write_text(json.dumps(receipt,indent=2)+"\n",encoding="utf-8",newline="\n")
    return receipt

def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ["manifest","program","readme","feed","out"]:parser.add_argument("--"+name,required=True,type=Path)
    for name in ["data-package-id","start-tutorial-url","adapter-tutorial-url"]:parser.add_argument("--"+name,required=True)
    args=parser.parse_args(argv)
    try:
        manifest=json.loads(args.manifest.read_text(encoding="utf-8"))
        prepare(manifest,args.program,args.readme,args.data_package_id,args.feed,args.out,
                args.start_tutorial_url,args.adapter_tutorial_url)
        print("PREPARED text only. No SDK, package inspection, compilation, consumer execution or approval.")
        return 0
    except (ValueError,OSError,ImportError) as error:
        print("REFUSED: "+str(error),file=sys.stderr)
        return 2

if __name__=="__main__":raise SystemExit(main())
