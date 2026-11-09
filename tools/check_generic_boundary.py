"""Known-consumer source boundary check; not a compiler, package or rights audit.

INVENTED predicates/fixtures, following AGENTS isolation and A282 item 8.
Reads only selected public C#/project/config text. Never opens resource contents,
DLLs, packages, game folders or external configuration. Explicit --repo/--scope.
"""
from __future__ import annotations
import argparse
from dataclasses import asdict, dataclass
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

@dataclass(frozen=True)
class Finding:
    code: str
    file: str
    line: int
    message: str

class ReviewNeeded(ValueError):
    pass

def identity(name, roots):
    """Known root/qualified/CamelCase identity, never a raw prose substring."""
    words = []
    for part in re.split(r"[^A-Za-z0-9]+", name):
        words.extend(re.findall(r"[A-Z]+(?=[A-Z][a-z]|[0-9]|$)|[A-Z]?[a-z]+|[0-9]+", part))
    folded = [w.casefold() for w in words]
    for root in roots:
        sequence = [w.casefold() for w in re.findall(r"[A-Z]+(?=[A-Z][a-z]|[0-9]|$)|[A-Z]?[a-z]+|[0-9]+",root)]
        if any(folded[i:i+len(sequence)] == sequence for i in range(len(folded)-len(sequence)+1)):
            return root
    return None

def _tokens(text):
    """Small lexical census: ignores comments/literal text, preserves interpolations.

    Unsupported interpolated raw strings or escaped identifiers stop for review.
    All conditional branches are scanned. No macro/MSBuild/semantic evaluation.
    """
    tokens=[]; n=len(text)
    def emit(kind,value,start):tokens.append((kind,value,start))
    def code(i=0,closing=False,depth=0):
        if depth>64:raise ReviewNeeded("source nesting exceeds lexical review bound")
        while i<n:
            start=i; ch=text[i]
            if closing and ch=="}":return i+1
            if ch.isspace():i+=1;continue
            if text.startswith("//",i):
                end=text.find("\n",i+2);i=n if end<0 else end;continue
            if text.startswith("/*",i):
                end=text.find("*/",i+2)
                if end<0:raise ReviewNeeded("unterminated block comment")
                i=end+2;continue
            if ch=="'":
                i+=1
                while i<n:
                    if text[i]=="\\":i+=2;continue
                    if text[i]=="'":i+=1;break
                    i+=1
                else:raise ReviewNeeded("unterminated character literal")
                continue
            match=re.match(r'(\$@|@\$|@|\$)?("+)',text[i:])
            if match:
                prefix=match.group(1) or ""; quotes=len(match.group(2)); q=i+len(prefix)
                if quotes>=3 and "@" not in prefix:
                    if "$" in prefix:raise ReviewNeeded("interpolated raw string needs compiler-backed review")
                    end=text.find('"'*quotes,q+quotes)
                    if end<0:raise ReviewNeeded("unterminated raw string")
                    emit("literal",text[q+quotes:end],start);i=end+quotes;continue
                i=q+1; chars=[]
                while i<n:
                    if text[i]=='"':
                        if "@" in prefix and text.startswith('""',i):chars.append('"');i+=2;continue
                        emit("literal","".join(chars),start);i+=1;break
                    if text[i]=="\\" and "@" not in prefix:
                        # Literal value is used only for identity slots, not C# evaluation.
                        escape=re.match(r'\\(?:u([0-9a-fA-F]{4})|U([0-9a-fA-F]{8})|x([0-9a-fA-F]{1,4}))',text[i:])
                        if escape:
                            number=int(next(x for x in escape.groups() if x is not None),16)
                            if number>0x10ffff:raise ReviewNeeded("invalid escaped code point")
                            chars.append(chr(number));i+=len(escape.group(0));continue
                        if i+1>=n:raise ReviewNeeded("unterminated string escape")
                        chars.append({"n":"\n","r":"\r","t":"\t"}.get(text[i+1],text[i+1]));i+=2;continue
                    if "$" in prefix and text[i]=="{":
                        if text.startswith("{{",i):chars.append("{");i+=2;continue
                        i=code(i+1,True,depth+1);chars.append("{expression}");continue
                    if "$" in prefix and text.startswith("}}",i):chars.append("}");i+=2;continue
                    chars.append(text[i]);i+=1
                else:raise ReviewNeeded("unterminated string")
                continue
            if ch=="\\" and i+1<n and text[i+1] in "uU":
                raise ReviewNeeded("escaped C# identifier needs compiler-backed review")
            if ch.isalpha() or ch=="_":
                i+=1
                while i<n and (text[i].isalnum() or text[i]=="_"):i+=1
                emit("id",text[start:i],start);continue
            if ch=="{":
                emit("symbol",ch,start);i=code(i+1,True,depth+1);continue
            if text[i:i+2] in {"==","!=","=>"}:emit("symbol",text[i:i+2],i);i+=2
            else:emit("symbol",ch,i);i+=1
        if closing:raise ReviewNeeded("unclosed code/interpolation block")
        return i
    code();return sorted(tokens,key=lambda t:t[2])

def scan_source(text, file, roots):
    findings=[]; advisories=[]
    try:tokens=_tokens(text)
    except ReviewNeeded as error:
        return [Finding("GB900",file,1,str(error))],[]
    for i,(kind,value,start) in enumerate(tokens):
        root=identity(value,roots)
        if root is None:continue
        line=text.count("\n",0,start)+1
        if kind=="id":findings.append(Finding("GB001",file,line,f"known consumer identifier '{value}' ({root})"))
        elif kind=="literal":
            before=[v for _,v,_ in tokens[max(0,i-7):i]]
            after=[v for _,v,_ in tokens[i+1:i+3]]
            context=bool(before and before[-1] in {"case","is","==","!="}) or bool(after and after[0] in {"==","!="})
            context |= bool(len(before)>1 and before[-2:]==["is","not"])
            identity_field = bool(before and before[-1]=="=" and any(v in {"GameName","GameId","GameKind","GameAssembly"} for v in before))
            context = (context and re.fullmatch(r"[A-Za-z0-9_.+/-]+",value) is not None) or identity_field
            row=Finding("GB006",file,line,f"known consumer identity literal '{value}'")
            if context: findings.append(row)
            elif re.fullmatch(r"[A-Za-z0-9_.+/-]+",value): advisories.append(row)
    return findings,advisories

def _linked(path):
    return path.is_symlink() or (hasattr(path,"is_junction") and path.is_junction())

def _local(tag):return tag.rsplit("}",1)[-1]

def load_scope(path):
    scope=json.loads(Path(path).read_text(encoding="utf-8-sig"))
    if scope.get("version")!=1:raise ValueError("boundary scope version must be 1")
    projects=scope.get("projects",[])
    if not projects or len({p["path"] for p in projects})!=len(projects):raise ValueError("explicit nonempty unique project scope required")
    if not scope.get("consumer_roots"):raise ValueError("explicit known-consumer roots required")
    local=Path(path).with_name("generic_boundary_local.json")   # optional, untracked: a maintainer's own game names
    if local.is_file():
        extra=json.loads(local.read_text(encoding="utf-8-sig")).get("consumer_roots",[])
        scope["consumer_roots"]=list(scope["consumer_roots"])+[r for r in extra if r not in scope["consumer_roots"]]
    for rel in [p["path"] for p in projects]+scope.get("support_projects",[]):
        if Path(rel).is_absolute() or ".." in Path(rel.replace("\\","/")).parts:raise ValueError("scope paths must be repository-relative")
    return scope

def inspect_repo(repo,scope,include_sources=False):
    root=Path(repo).resolve(strict=True)
    roots=scope["consumer_roots"]
    selected={row["path"]:row["assembly"] for row in scope["projects"]}
    allowed=set(selected)|set(scope.get("support_projects",[]))
    findings=[];advisories=[];inputs={};edges=[];seen=set();source_count=0;omitted=[]
    def issue(code,where,why):findings.append(Finding(code,where,1,why))
    def read(path):
        if not path.is_relative_to(root) or any(_linked(p) for p in [path,*[x for x in path.parents if x.is_relative_to(root)]]) or not path.is_file():
            raise ReviewNeeded("missing, linked or out-of-scope source input: "+str(path))
        data=path.read_bytes();inputs[path.relative_to(root).as_posix()]=hashlib.sha256(data).hexdigest()
        return data.decode("utf-8-sig")
    def xml(path,owner,visited):
        rel=path.relative_to(root).as_posix()
        if rel in visited:return
        visited.add(rel)
        try:tree=ET.fromstring(read(path))
        except (ReviewNeeded,ET.ParseError,UnicodeError) as error:issue("GB900",rel,str(error));return
        for node in tree.iter():
            tag=_local(node.tag);text=(node.text or "").strip()
            sdk=node.get("Sdk") or (node.get("Name") if tag=="Sdk" else None)
            if sdk and identity(sdk,roots):issue("GB101",rel,"known consumer SDK identity: "+sdk)
            if tag=="Using" and identity(node.get("Include", ""),roots):issue("GB101",rel,"known consumer generated using identity")
            if tag in {"AssemblyName","RootNamespace","PackageId"}:
                if "$" in text:issue("GB900",rel,"unresolved assembly/namespace identity needs evaluated review")
                elif identity(text,roots):issue("GB101",rel,"known consumer assembly/namespace identity: "+text)
            if tag in {"Reference","PackageReference"}:
                name=node.get("Include") or node.get("Update") or ""
                if identity(name,roots):issue("GB101",rel,"known consumer reference: "+name)
                if re.search(r"[$@%]\(",name):issue("GB900",rel,"unresolved reference identity")
            if tag=="EnableDefaultCompileItems" and text.casefold()=="false":issue("GB900",rel,"non-default compile set needs evaluated review")
            if tag=="Compile":issue("GB103",rel,"explicit compile item/mutation needs reviewed source-selection support")
            if tag=="ProjectReference":
                include=node.get("Include","")
                if not include or any(k in node.attrib for k in ["Remove","Update"]) or re.search(r"[$@%]\(|[?*]",include):
                    issue("GB900",rel,"nonliteral project reference/mutation needs evaluated review");continue
                for part in include.split(";"):
                    target=(owner.parent/part.replace("\\",os.sep)).resolve()
                    if not target.is_relative_to(root):issue("GB102",rel,"external project refused without reading: "+part);continue
                    target_rel=target.relative_to(root).as_posix();edges.append([owner.relative_to(root).as_posix(),target_rel])
                    if target_rel not in allowed:issue("GB102",rel,"unlisted project refused without reading: "+target_rel)
                    else:visit(target_rel)
            if tag=="Import":
                value=node.get("Project","")
                if not value or re.search(r"[$@%]\(|[?*;]",value):issue("GB900",rel,"nonliteral import needs evaluated review");continue
                target=(path.parent/value.replace("\\",os.sep)).resolve()
                if not target.is_relative_to(root) or target.suffix not in {".props",".targets"} or (target.parent!=root and root/"build" not in target.parents):
                    issue("GB102",rel,"import outside declared root/build text scope, not read");continue
                xml(target,owner,visited)
            if tag in {"EmbeddedResource","Content","None"} and node.get("Include"):
                name=node.get("LogicalName") or node.get("Include","")
                leaf=name.replace("\\","/").rsplit("/",1)[-1]
                legal=re.search(r"(?:^|[._-])(?:licen[cs]e|notice|credits)(?:[._-]|$)",leaf,re.I)
                if not legal and identity(name,roots):issue("GB104",rel,"known consumer resource/content name (contents not opened): "+name)
    def visit(rel):
        nonlocal source_count
        if rel in seen:return
        seen.add(rel);project=root/rel
        # Only explicit selected/support project paths are read; never follow a game reference.
        if rel not in allowed:issue("GB102",rel,"project not in explicit scope");return
        inherited=set()
        for name in ["Directory.Build.props","Directory.Packages.props","Directory.Build.targets"]:
            for parent in [project.parent,*project.parent.parents]:
                if not parent.is_relative_to(root):break
                candidate=parent/name
                if candidate.is_file():xml(candidate,project,inherited);break
        xml(project,project,inherited)
        if rel not in selected:return # support graph metadata only; no vendored source/content census
        for directory,dirs,files in os.walk(project.parent,followlinks=False):
            kept=[]
            for child in dirs:
                p=Path(directory)/child
                if child in {"bin","obj",".git",".gate"}:continue
                if _linked(p):issue("GB900",rel,"linked source directory not followed");continue
                kept.append(child)
            dirs[:]=kept
            for name in sorted(files):
                if not name.casefold().endswith(".cs"):continue
                p=Path(directory)/name;source_rel=p.relative_to(root).as_posix()
                if not include_sources or any(source_rel==x.rstrip("/") or source_rel.startswith(x.rstrip("/")+"/") for x in scope.get("source_exclusions",[])):
                    omitted.append(source_rel);continue
                if identity(p.relative_to(project.parent).as_posix(),roots):issue("GB001",source_rel,"known consumer source filename/path")
                try:text=read(p)
                except (ReviewNeeded,UnicodeError) as error:issue("GB900",source_rel,str(error));continue
                found,notes=scan_source(text,source_rel,roots);findings.extend(found);advisories.extend(notes);source_count+=1
    for rel in selected:visit(rel)
    return {"kind":"source-only known-consumer boundary census; not MSBuild, binary or package verification",
            "scope_status":scope.get("status","unspecified"),"selected_projects":list(selected),"visited_project_metadata":sorted(seen),
            "source_files":source_count,"coverage_complete":include_sources and not omitted and not any(f.code in {"GB900","GB102","GB103"} for f in findings),"omitted_sources":omitted,"inputs":inputs,"edges":edges,"findings":[asdict(f) for f in findings],
            "advisories":[asdict(f) for f in advisories],"binary_inspected":False,"package_outputs_inspected":False,
            "limitations":["known consumer identities, not arbitrary domain semantics","literal source graph, not evaluated MSBuild/generated or conditional outputs",
                           "comments/docs/prose and legal credit text deliberately not forbidden","non-identity strings only reviewed in explicit identity/comparison slots; concatenated/dynamic dispatch unproved",
                           "resource contents and actual package files never opened","support projects metadata only; no vendored source audit"]}

def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo",required=True,type=Path);parser.add_argument("--scope",required=True,type=Path)
    parser.add_argument("--report",type=Path)
    parser.add_argument("--tracked-files",type=Path,help="optional owner-supplied tracked list for the public-text guard; never invokes Git")
    parser.add_argument("--tracked-format",choices=["nul","lines"],default="nul")
    parser.add_argument("--public-text-allowlist",type=Path)
    parser.add_argument("--scan-reviewed-sources",action="store_true",help="lexical census of reviewed source scope only; grants no access permission")
    args=parser.parse_args(argv)
    public_code,public_result=0,None
    if args.tracked_files is not None:
        import check_public_text
        try:
            public_code,public_result=check_public_text.run(args.repo,args.tracked_files,args.tracked_format,args.public_text_allowlist)
        except (OSError,UnicodeError,ValueError,TypeError):
            print("<configuration>:1: PT900 invalid/unreadable public-text input (values omitted)")
            return 2
    result=inspect_repo(args.repo,load_scope(args.scope),include_sources=args.scan_reviewed_sources)
    result["scope_sha256"]=hashlib.sha256(args.scope.read_bytes()).hexdigest()
    result["checker_sha256"]=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    if public_result is not None: result["public_text"]=public_result
    text=json.dumps(result,indent=2,ensure_ascii=False)+"\n"
    if args.report:args.report.write_text(text,encoding="utf-8",newline="\n")
    print(f"{len(result['selected_projects'])} selected library projects, {result['source_files']} C# source files, {len(result['findings'])} findings, {len(result['advisories'])} advisories; SOURCE ONLY")
    for finding in result['findings']:print(f"{finding['code']} {finding['file']}:{finding['line']}: {finding['message']}")
    if public_code: return public_code
    return 1 if result['findings'] else (0 if result["coverage_complete"] else 2)

if __name__=="__main__":raise SystemExit(main())
