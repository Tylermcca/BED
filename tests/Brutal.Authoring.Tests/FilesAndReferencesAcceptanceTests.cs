using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Authoring;
using Brutal.Authoring.Agent;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Cli = Brutal.Authoring.Agent.Cli.Program;

namespace Brutal.Authoring.Tests;

// INVENTED fixtures/adversarial schedules; BORROWED targets are the public XML contracts named by A062.
// MIXED: public project/workspace/agent calls plus real ImGui backend input, with no game data.
[Collection(ImGuiCollection.Name)]
public sealed class FilesAndReferencesAcceptanceTests
{
    private const string Loose = """{"type":"object","properties":{"$schema":{"type":"string"}},"additionalProperties":true}""";
    private const string Tokens = """
        { "$schema": "blank.schema.json", "tokens": [
          {"id":"amber","caption":"Amber spindle"},
          {"id":"jade","caption":"Jade lantern"},
          {"id":"echo","caption":"First echo"},{"id":"echo","caption":"Second echo"}],
          "teams":{"crew":{"caption":"Ridge team"}},"local":[{"target":"amber"}] }
        """;
    private const string Routes = """
        {"$schema":"blank.schema.json","routes":[
          {"caption":"Ridge courier","target":"amber","many":["amber","jade"],"byKey":{"amber":2},
           "team":"crew","ambiguous":"echo","missing":"gone","unloaded":"amber","words":"amber","sentinel":"none","plain":"amber"},
          {"caption":"Canal runner","target":"jade"}]}
        """;

    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),"a062-files-"+Guid.NewGuid().ToString("N"));
        public string Data => Path.Combine(Root,"data");
        public string Schemas => Path.Combine(Root,"schemas");
        public DataProject Project { get; }
        public ReferenceMap Map { get; }
        public Sandbox(string? shape=null)
        {
            Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);
            File.WriteAllText(Path.Combine(Schemas,"blank.schema.json"),shape??Loose,new UTF8Encoding(false));
            Write("tokens.json",Tokens);Write("routes.json",Routes);
            Project=Open();Map=MakeMap();
        }
        public void Write(string name,string content)=>File.WriteAllText(Path.Combine(Data,name),content,new UTF8Encoding(false));
        public byte[] Bytes(string name)=>File.ReadAllBytes(Path.Combine(Data,name));
        public DataProject Open(bool readOnly=false,Func<string,string?>? fileRule=null,Func<string,IValidator<DataDocument>?>? rules=null)
            =>DataProject.Open(Data,Schemas,readOnly,fileRule,rules);
        public DataFile Loaded(string name="tokens.json",DataProject? p=null)
        {
            var f=(p??Project).Find(name)!;Assert.NotNull(f);Assert.True(f.Open(),string.Join("; ",f.Problems.Select(i=>i.Message)));return f;
        }
        public Dictionary<string,string> Disk()=>Directory.EnumerateFiles(Data).Order(StringComparer.Ordinal)
            .ToDictionary(p=>Path.GetFileName(p)!,p=>Convert.ToHexString(File.ReadAllBytes(p)))!;
        public void Dispose()
        {
            var full=Path.GetFullPath(Root);var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("a062-files-",StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected cleanup boundary.");
            Directory.Delete(full,true);
        }
    }
    private static ReferenceMap MakeMap()
    {
        var refs=new JsonArray();
        void Add(string file,string at,string targetFile="tokens.json",string list="/tokens",string key="id",bool keys=false,string? scope=null,bool sentinel=false)
        {
            var o=new JsonObject {["file"]=file,["at"]=at,["target"]=new JsonObject {["file"]=targetFile,["list"]=list,["key"]=key},
                ["evidence"]="Invented A062 route/token fixture."};
            if(keys)o["keys"]=true;if(scope is not null)o["scope"]=scope;if(sentinel)o["sentinels"]=new JsonArray("none");
            refs.Add(o);
        }
        Add("routes.json","/routes/*/target");Add("routes.json","/routes/*/many");Add("routes.json","/routes/*/byKey",keys:true);
        Add("routes.json","/routes/*/team",list:"/teams",key:"$key");
        Add("routes.json","/routes/*/ambiguous");Add("routes.json","/routes/*/missing");
        Add("routes.json","/routes/*/unloaded",targetFile:"absent.json");
        Add("routes.json","/routes/*/words",scope:"Each route's context, not implemented.");
        Add("routes.json","/routes/*/sentinel",sentinel:true);Add("tokens.json","/local/*/target");
        var map=new JsonObject {["game"]="A062 invented relay garden",["labels"]=new JsonArray(
            new JsonObject {["file"]="routes.json",["list"]="/routes",["field"]="caption"},
            new JsonObject {["file"]="tokens.json",["list"]="/tokens",["field"]="caption"}),["references"]=refs};
        return ReferenceMap.Read(map.ToJsonString());
    }
    private sealed class Rule(Func<DataDocument,IReadOnlyList<Issue>> run) : IValidator<DataDocument>
    {
        public IReadOnlyList<Issue> Validate(DataDocument d)=>run(d);
    }
    private static JsonElement J(string text)=>JsonDocument.Parse(text).RootElement.Clone();
    private static void SameDisk(Dictionary<string,string> expected,Sandbox r)
        =>Assert.Equal(expected.OrderBy(p=>p.Key),r.Disk().OrderBy(p=>p.Key));
    private static void Edit(DataFile f,string at,string json)
        =>Assert.True(f.Session!.Execute(new SetValue(at,J(json))),string.Join("; ",f.Session!.LastIssues.Select(i=>i.Message)));
    private static void Success(JsonObject result){Assert.False(result.ContainsKey("error"),result.ToJsonString());Assert.NotNull(result["next"]);}

    [Theory]
    [InlineData("string","\"\"")][InlineData("integer","0")][InlineData("number","0")]
    [InlineData("boolean","false")][InlineData("array","[]")][InlineData("object","{}")][InlineData("null","null")]
    public void Create_RequiredField_GetsItsOwnTypesEmptyValue(string kind,string expected)
    {
        var shape=new JsonObject {["type"]="object",["required"]=new JsonArray("value"),
            ["properties"]=new JsonObject {["value"]=new JsonObject {["type"]=kind}}};
        using var r=new Sandbox(shape.ToJsonString());
        Assert.True(r.Project.Create("blank.json","blank.schema.json",out var why),why);
        var f=r.Project.Find("blank.json")!;Assert.NotNull(f.Session);
        Assert.Equal(expected,f.Session!.Current.At("/value")!.Value.GetRawText());
        Assert.False(f.Session.Current.Root.TryGetProperty("$schema",out _));
        Assert.False(f.Dirty);Assert.Empty(f.Session.Journal);
    }
    [Fact]
    public void Create_RequiredDefaults_ArePreservedIncludingNestedValues()
    {
        using var r=new Sandbox("""{"type":"object","required":["settings","on"],"properties":{"settings":{"type":"object","default":{"gain":7}},"on":{"type":"boolean","default":true},"optional":{"type":"string"}}}""");
        Assert.True(r.Project.Create("blank.json","blank.schema.json",out var why),why);
        var d=r.Project.Find("blank.json")!.Session!.Current;
        Assert.Equal(7,d.At("/settings/gain")!.Value.GetInt32());Assert.True(d.At("/on")!.Value.GetBoolean());Assert.Null(d.At("/optional"));
    }
    [Theory]
    [InlineData("blank.json",false)][InlineData("new-relays.json",true)]
    public void Create_SchemaMarker_IsAddedOnlyWhenFilenameNeedsIt(string name,bool marker)
    {
        using var r=new Sandbox();Assert.True(r.Project.Create(name,"blank.schema.json",out var why),why);
        var root=J(File.ReadAllText(Path.Combine(r.Data,name)));
        Assert.Equal(marker,root.TryGetProperty("$schema",out var selected));if(marker)Assert.Equal("blank.schema.json",selected.GetString());
    }
    [Fact]
    public void Create_Preview_IsACompleteNoWriteNoOpenOperation()
    {
        using var r=new Sandbox();var disk=r.Disk();var listed=r.Project.Files.ToArray();
        Assert.True(r.Project.Create("proposal.json","blank.schema.json",out var why,preview:true),why);
        Assert.Contains("Would",why);SameDisk(disk,r);Assert.Equal(listed,r.Project.Files);Assert.Null(r.Project.Find("proposal.json"));Assert.Empty(r.Project.Deleted);
    }
    [Theory]
    [InlineData("")][InlineData("../escape.json")][InlineData("child/file.json")][InlineData(".hidden.json")]
    [InlineData("notes.txt")][InlineData("LOUD.JSON")][InlineData("bad:name.json")]
    public void Create_RejectsNonPlainJsonNames_WithoutSideEffects(string name)
    {
        using var r=new Sandbox();var disk=r.Disk();Assert.False(r.Project.Create(name,"blank.schema.json",out var why));
        Assert.Contains("plain",why);SameDisk(disk,r);
    }
    [Theory]
    [InlineData("taken")][InlineData("schema")][InlineData("project")][InlineData("file")]
    public void Create_EachRefusal_SaysWhyAndKeepsDisk(string reason)
    {
        using var r=new Sandbox();var disk=r.Disk();var p=r.Open(reason=="project",n=>reason=="file"?"generated elsewhere":null);
        var name=reason=="taken"?"tokens.json":"unmade.json";var schema=reason=="schema"?"absent.schema.json":"blank.schema.json";
        Assert.False(p.Create(name,schema,out var why));Assert.False(string.IsNullOrWhiteSpace(why));
        Assert.Contains(reason switch {"taken"=>"already","schema"=>"schema",_=>"read-only"},why);SameDisk(disk,r);
    }
    [Fact]
    public void Create_InvalidInitialSkeleton_IsRefusedWithRemedy()
    {
        using var r=new Sandbox("""{"type":"object","required":["count"],"properties":{"count":{"type":"integer","minimum":1}}}""");
        var disk=r.Disk();Assert.False(r.Project.Create("blank.json","blank.schema.json",out var why));
        Assert.Contains("invalid",why);Assert.Contains("copying",why);SameDisk(disk,r);Assert.Null(r.Project.Find("blank.json"));
    }
    [Fact]
    public void Create_GameRule_CanRefuseAnOtherwiseValidSkeleton()
    {
        using var r=new Sandbox();var disk=r.Disk();var p=r.Open(rules:_=>new Rule(_=>[Issue.Error("Relay budget needs a keeper.")]));
        Assert.False(p.Create("new.json","blank.schema.json",out var why));Assert.Contains("Relay budget",why);SameDisk(disk,r);
    }
    [Fact]
    public void Create_AFileAppearingDuringValidation_IsNeverOverwritten()
    {
        using var r=new Sandbox();const string incoming="External creator owns these bytes.\r\n";
        var p=r.Open(rules:n=>new Rule(_=>{r.Write(n,incoming);return [];}));
        Assert.False(p.Create("arrival.json","blank.schema.json",out var why));Assert.Contains("written",why);
        Assert.Equal(Encoding.UTF8.GetBytes(incoming),r.Bytes("arrival.json"));
    }
    [Fact]
    public void SchemaNames_ListsRealSchemaFilesInOrdinalOrder()
    {
        using var r=new Sandbox();File.WriteAllText(Path.Combine(r.Schemas,"z.schema.json"),Loose);File.WriteAllText(Path.Combine(r.Schemas,"a.schema.json"),Loose);
        File.WriteAllText(Path.Combine(r.Schemas,"notes.txt"),"not a schema");
        Assert.Equal(new[]{"a.schema.json","blank.schema.json","z.schema.json"},r.Project.SchemaNames);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Rename_KeepsExactBytes_AndReopensOnlyIfItWasOpen(bool open)
    {
        using var r=new Sandbox();var before=r.Bytes("tokens.json");var old=r.Project.Find("tokens.json")!;
        if(open)r.Loaded();var session=old.Session;
        Assert.True(r.Project.Rename("tokens.json","renamed.json",out var why),why);
        Assert.Null(r.Project.Find("tokens.json"));Assert.False(File.Exists(Path.Combine(r.Data,"tokens.json")));
        Assert.Equal(before,r.Bytes("renamed.json"));var renamed=r.Project.Find("renamed.json")!;Assert.NotNull(renamed);
        Assert.Equal(open,renamed.Session is not null);if(open){Assert.NotSame(session,renamed.Session);Assert.False(renamed.Dirty);}
    }
    [Fact]
    public void Rename_Preview_PreservesOpenSessionAndBytes()
    {
        using var r=new Sandbox();var f=r.Loaded();var session=f.Session;var disk=r.Disk();
        Assert.True(r.Project.Rename("tokens.json","planned.json",out var why,preview:true),why);
        SameDisk(disk,r);Assert.Same(session,f.Session);Assert.Null(r.Project.Find("planned.json"));
    }
    [Fact]
    public void Rename_SchemaChangeRefusal_NamesAnEffectiveRemedy()
    {
        using var r=new Sandbox();r.Write("blank.json","{\"items\":[]}");r.Project.Refresh();var before=r.Bytes("blank.json");
        Assert.False(r.Project.Rename("blank.json","elsewhere.json",out var why));Assert.Contains("$schema",why);Assert.Equal(before,r.Bytes("blank.json"));
        r.Write("blank.json","{\"$schema\":\"blank.schema.json\",\"items\":[]}");
        Assert.True(r.Project.Rename("blank.json","elsewhere.json",out why),why);
        Assert.Equal("blank.schema.json",DataProject.SchemaNameFor("elsewhere.json",J(File.ReadAllText(Path.Combine(r.Data,"elsewhere.json")))));
    }
    [Theory]
    [InlineData("dirty")][InlineData("taken")][InlineData("from-readonly")][InlineData("to-readonly")][InlineData("project")]
    public void Rename_RefusesEachUnsafeCondition_KeepingDraftAndDisk(string reason)
    {
        using var r=new Sandbox();var p=r.Open(reason=="project",n=>(reason=="from-readonly"&&n=="tokens.json"||reason=="to-readonly"&&n=="moved.json")?"owned by generator":null);
        var f=r.Loaded(p:p);if(reason=="dirty")Edit(f,"/tokens/0/caption","\"Draft label\"");
        var disk=r.Disk();var session=f.Session;
        Assert.False(p.Rename("tokens.json",reason=="taken"?"routes.json":"moved.json",out var why));
        Assert.Contains(reason=="dirty"?"unsaved":reason=="taken"?"already":"read-only",why);
        SameDisk(disk,r);Assert.Same(session,f.Session);Assert.Equal(reason=="dirty",f.Dirty);
    }
    [Fact]
    public void Delete_Preview_KeepsBytesSessionAndRecoveryStack()
    {
        using var r=new Sandbox();var f=r.Loaded();var session=f.Session;var disk=r.Disk();
        Assert.True(r.Project.Delete("tokens.json",out var why,preview:true),why);SameDisk(disk,r);Assert.Same(session,f.Session);Assert.Empty(r.Project.Deleted);
    }
    [Fact]
    public void DeleteAndRestore_RoundTripExactBytesAndRegistry()
    {
        using var r=new Sandbox();r.Write("tokens.json","{ \"$schema\":\"blank.schema.json\", \"value\":1.00, \"text\":\"Café\" }\r\n");
        var bytes=r.Bytes("tokens.json");r.Loaded();Assert.True(r.Project.Delete("tokens.json",out var why),why);
        Assert.Null(r.Project.Find("tokens.json"));Assert.False(File.Exists(Path.Combine(r.Data,"tokens.json")));Assert.Equal(new[]{"tokens.json"},r.Project.Deleted);
        Assert.True(r.Project.Restore("tokens.json",out why),why);Assert.Equal(bytes,r.Bytes("tokens.json"));Assert.Empty(r.Project.Deleted);
        Assert.NotNull(r.Project.Find("tokens.json"));
    }
    [Fact]
    public void Restore_WhenNameWasTaken_RefusesAndKeepsRecoveryBytes()
    {
        using var r=new Sandbox();var bytes=r.Bytes("tokens.json");Assert.True(r.Project.Delete("tokens.json",out _));r.Write("tokens.json","external owner");
        Assert.False(r.Project.Restore("tokens.json",out var why));Assert.Contains("back",why);Assert.Equal("external owner",File.ReadAllText(Path.Combine(r.Data,"tokens.json")));
        Assert.Equal(new[]{"tokens.json"},r.Project.Deleted);File.Delete(Path.Combine(r.Data,"tokens.json"));
        Assert.True(r.Project.Restore("tokens.json",out why),why);Assert.Equal(bytes,r.Bytes("tokens.json"));
    }
    [Fact]
    public void Restore_RepeatedName_PutsBackLatestDeletionFirst()
    {
        using var r=new Sandbox();var first=r.Bytes("tokens.json");Assert.True(r.Project.Delete("tokens.json",out _));
        const string second="{\"$schema\":\"blank.schema.json\",\"generation\":2}\n";r.Write("tokens.json",second);r.Project.Refresh();Assert.True(r.Project.Delete("tokens.json",out _));
        Assert.Equal(2,r.Project.Deleted.Count);Assert.True(r.Project.Restore("tokens.json",out var why),why);
        Assert.Equal(Encoding.UTF8.GetBytes(second),r.Bytes("tokens.json"));Assert.Single(r.Project.Deleted);
        File.Delete(Path.Combine(r.Data,"tokens.json"));r.Project.Refresh();Assert.True(r.Project.Restore("tokens.json",out why),why);Assert.Equal(first,r.Bytes("tokens.json"));
    }
    [Theory]
    [InlineData("dirty")][InlineData("file")][InlineData("project")]
    public void Delete_RefusesReadOnlyOrUnsavedWork_WithoutLosingIt(string reason)
    {
        using var r=new Sandbox();var p=r.Open(reason=="project",_=>reason=="file"?"generated elsewhere":null);
        var f=r.Loaded(p:p);if(reason=="dirty")Edit(f,"/tokens/0/caption","\"Keep my draft\"");
        var disk=r.Disk();Assert.False(p.Delete("tokens.json",out var why));Assert.Contains(reason=="dirty"?"unsaved":"read-only",why);
        SameDisk(disk,r);Assert.Empty(p.Deleted);Assert.Equal(reason=="dirty",f.Dirty);
    }
    [Fact]
    public void Restore_AnUnknownDeletion_SaysSoWithoutWriting()
    {
        using var r=new Sandbox();var disk=r.Disk();Assert.False(r.Project.Restore("never.json",out var why));Assert.Contains("wasn't deleted",why);SameDisk(disk,r);
    }
    [Fact]
    public void Restore_WhenHostMarksNameReadOnly_DoesNotWriteIt()
    {
        using var r=new Sandbox();bool locked=false;var p=r.Open(fileRule:_=>locked?"host owns the restored file":null);
        Assert.True(p.Delete("tokens.json",out _));locked=true;var disk=r.Disk();
        Assert.False(p.Restore("tokens.json",out var why));Assert.Contains("read-only",why);SameDisk(disk,r);Assert.Single(p.Deleted);
    }

    [Theory]
    [InlineData("../renamed.json")][InlineData("notes.txt")]
    public void Rename_InvalidDestination_IsRefusedWithNoWrites(string name)
    {
        using var r=new Sandbox();var disk=r.Disk();Assert.False(r.Project.Rename("tokens.json",name,out var why));Assert.Contains("plain",why);SameDisk(disk,r);
    }
    [Theory]
    [InlineData("rename")][InlineData("delete")]
    public void FileOperation_MissingSource_IsAnActionableRefusal(string operation)
    {
        using var r=new Sandbox();var disk=r.Disk();string why;
        var ok=operation=="rename"?r.Project.Rename("gone.json","new.json",out why):r.Project.Delete("gone.json",out why);
        Assert.False(ok);Assert.Contains("no file",why);SameDisk(disk,r);
    }
    [Fact]
    public void LinksInto_CountsScalarListAndKeyLinksButExcludesSameFile()
    {
        using var r=new Sandbox();Assert.Equal(6,r.Map.LinksInto(r.Project,"tokens.json"));
        var refs=r.Map.ReferencesTo(r.Project,new("tokens.json","/tokens/0"));
        Assert.Contains(new ReferenceTarget("routes.json","/routes/0/target"),refs);
        Assert.Contains(new ReferenceTarget("routes.json","/routes/0/many/0"),refs);
        Assert.Contains(new ReferenceTarget("routes.json","/routes/0/byKey/amber"),refs);
        Assert.Contains(new ReferenceTarget("tokens.json","/local/0/target"),refs);
        Assert.Equal(4,refs.Count);
    }
    [Fact]
    public void LinksInto_UsesUnsavedSourceEditsImmediately()
    {
        using var r=new Sandbox();Assert.Equal(6,r.Map.LinksInto(r.Project,"tokens.json"));var source=r.Loaded("routes.json");var bytes=r.Bytes("routes.json");
        Edit(source,"/routes/0/target","\"gone\"");Assert.Equal(5,r.Map.LinksInto(r.Project,"tokens.json"));
        Assert.Equal(bytes,r.Bytes("routes.json"));Assert.True(source.Dirty);
        Assert.True(source.Session!.Undo());Assert.Equal(6,r.Map.LinksInto(r.Project,"tokens.json"));
    }
    [Fact]
    public void LinksInto_UsesUnsavedTargetIds_AndNeverFirstDuplicate()
    {
        using var r=new Sandbox();var f=r.Loaded();Assert.Equal(6,r.Map.LinksInto(r.Project,"tokens.json"));
        Edit(f,"/tokens/0/id","\"new-amber\"");Assert.Equal(3,r.Map.LinksInto(r.Project,"tokens.json"));
        Assert.True(f.Session!.Undo());Edit(f,"/tokens/1/id","\"amber\"");
        Assert.Equal(1,r.Map.LinksInto(r.Project,"tokens.json")); // only keyed crew remains; amber is ambiguous, jade missing
    }
    [Fact]
    public void LinksInto_UnopenableTarget_IsZero()
    {
        using var r=new Sandbox();r.Write("tokens.json","{broken");var p=r.Open();Assert.Equal(0,r.Map.LinksInto(p,"tokens.json"));
        Assert.False(p.Find("tokens.json")!.Open());Assert.NotEmpty(p.Find("tokens.json")!.Problems);
    }
    [Fact]
    public void ReferencedBy_LabelsEachSourcePlace_WithoutChangingDrafts()
    {
        using var r=new Sandbox();var w=new DataWorkspace(r.Project,map:r.Map);Assert.True(w.Show("tokens.json"));w.Window!.Editor.Select("/tokens/0");
        var refs=w.ReferencedBy();Assert.Equal(4,refs.Count);
        Assert.Equal(3,refs.Count(x=>x.Label=="Ridge courier"));Assert.Contains(refs,x=>x.At.File=="tokens.json"&&x.Label=="/local/0/target");
        Assert.Empty(r.Project.Dirty);
    }
    [Fact]
    public void ReferencedBy_UpdatesWhileSelectionStaysAndSourceDraftChanges()
    {
        using var r=new Sandbox();var w=new DataWorkspace(r.Project,map:r.Map);Assert.True(w.Show("tokens.json"));w.Window!.Editor.Select("/tokens/0");
        Assert.Equal(4,w.ReferencedBy().Count);var source=r.Loaded("routes.json");
        Edit(source,"/routes/0/target","\"jade\"");Assert.Equal(3,w.ReferencedBy().Count);
        Edit(source,"/routes/0/caption","\"Draft courier\"");Assert.Equal(2,w.ReferencedBy().Count(x=>x.Label=="Draft courier"));
        Assert.Equal("/tokens/0",w.Window!.Editor.Primary);Assert.True(source.Dirty);
    }
    [Fact]
    public void ReferencedBy_EmptyWithNoMapOrNoSelection()
    {
        using var r=new Sandbox();var noMap=new DataWorkspace(r.Project);Assert.True(noMap.Show("tokens.json"));noMap.Window!.Editor.Select("/tokens/0");
        Assert.Empty(noMap.ReferencedBy());Assert.Equal(0,noMap.LinksInto("tokens.json"));
        var mapped=new DataWorkspace(r.Project,map:r.Map);Assert.True(mapped.Show("tokens.json"));Assert.Empty(mapped.ReferencedBy());
    }
    [Fact]
    public void ReferencedBy_SkipsUnopenableSource_AndKeepsOtherReferences()
    {
        using var r=new Sandbox();r.Write("routes.json","not json");var p=r.Open();var w=new DataWorkspace(p,map:r.Map);
        Assert.True(w.Show("tokens.json"));w.Window!.Editor.Select("/tokens/0");var only=Assert.Single(w.ReferencedBy());
        Assert.Equal(new ReferenceTarget("tokens.json","/local/0/target"),only.At);Assert.False(p.Find("routes.json")!.Open());
    }
    [Fact]
    public void ReferencedBy_GoToAndBack_ReturnToExactFileAndItem()
    {
        using var r=new Sandbox();var w=new DataWorkspace(r.Project,map:r.Map);Assert.True(w.Show("tokens.json"));w.Window!.Editor.Select("/tokens/0");
        var source=w.ReferencedBy().Single(x=>x.At==new ReferenceTarget("routes.json","/routes/0/target"));
        Assert.True(w.GoTo(source.At),w.Said);Assert.Equal("routes.json",w.Active!.Name);Assert.Equal("/routes/0",w.Window!.Editor.Primary);
        Assert.True(w.Back());Assert.Equal("tokens.json",w.Active!.Name);Assert.Equal("/tokens/0",w.Window!.Editor.Primary);Assert.Empty(r.Project.Dirty);
    }
    [Theory]
    [InlineData("ambiguous","2 items","different id")]
    [InlineData("missing","gone","spelling")]
    [InlineData("unloaded","absent.json","Files panel")]
    [InlineData("words","context","only in words")]
    [InlineData("sentinel","deliberately","nothing")]
    public void LinkProblem_EachUnfollowableOutcome_ExplainsItsReasonAndRemedy(string field,string reason,string next)
    {
        using var r=new Sandbox();var w=new DataWorkspace(r.Project,map:r.Map);Assert.True(w.Show("routes.json"));
        var table=w.TableOf("routes.json")!;Assert.False(table.Follow("/routes/0",field));
        Assert.Contains(reason,table.Said);Assert.Contains(next,table.Said);Assert.Equal("routes.json",w.Active!.Name);Assert.Empty(r.Project.Dirty);
    }
    [Fact]
    public void LinkProblem_AnOffMapValue_DoesNotGetAnInventedReferenceReason()
    {
        using var r=new Sandbox();var w=new DataWorkspace(r.Project,map:r.Map);Assert.True(w.Show("routes.json"));
        var table=w.TableOf("routes.json")!;var value=r.Loaded("routes.json").Session!.Current.At("/routes/0/plain")!.Value;
        Assert.Null(table.LinkProblem!("/routes/0/plain",value));Assert.False(table.Follow("/routes/0","plain"));
        Assert.Equal("plain doesn't name an item here.",table.Said);
    }
    [Fact]
    public void Workspace_FileOperations_KeepShowingTheRightEditor()
    {
        using var r=new Sandbox();var w=new DataWorkspace(r.Project,map:r.Map);
        Assert.True(w.NewFile("fresh.json","blank.schema.json"),w.Said);Assert.Equal("fresh.json",w.Active!.Name);
        Assert.True(w.RenameFile("fresh.json","moved.json"),w.Said);Assert.Equal("moved.json",w.Active!.Name);
        Assert.Null(w.TableOf("fresh.json"));Assert.NotNull(w.TableOf("moved.json"));Assert.Contains("reference map",w.Said);
        Assert.True(w.DeleteFile("moved.json"),w.Said);Assert.Null(w.Active);Assert.Null(w.Window);Assert.Null(w.TableOf("moved.json"));
        Assert.True(w.RestoreFile("moved.json"),w.Said);Assert.True(w.Show("moved.json"));Assert.False(w.Active!.Dirty);
    }

    private static JsonObject Rpc(McpServer server,string name,JsonObject args)
    {
        var request=new JsonObject {["jsonrpc"]="2.0",["id"]=47,["method"]="tools/call",
            ["params"]=new JsonObject {["name"]=name,["arguments"]=args.DeepClone()}};
        var response=server.Handle(request.ToJsonString())!;
        Assert.Equal(47,response["id"]!.GetValue<int>());return (JsonObject)response["result"]!;
    }
    private static JsonObject RpcBody(JsonObject result)=>JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!.AsObject();
    private static (int Exit,JsonObject? Answer,string Error) RunCli(Sandbox r,params string[] call)
    {
        var output=new StringWriter();var errors=new StringWriter();
        var args=new[]{"--data",r.Data,"--schemas",r.Schemas}.Concat(call).ToArray();
        var code=Cli.Run(args,new StringReader(""),output,errors);
        return (code,string.IsNullOrWhiteSpace(output.ToString())?null:JsonNode.Parse(output.ToString())!.AsObject(),errors.ToString());
    }
    [Fact]
    public void Agent_FilePreviewAndImmediateOperations_UseTheSameGuards()
    {
        using var r=new Sandbox();var agent=new AgentSession(r.Project,r.Map);var disk=r.Disk();
        var preview=agent.Create("agent.json","blank.schema.json",preview:true);Success(preview);Assert.NotNull(preview["wouldDo"]);SameDisk(disk,r);
        var made=agent.Create("agent.json","blank.schema.json");Success(made);Assert.True(File.Exists(Path.Combine(r.Data,"agent.json")));
        Success(agent.Rename("agent.json","agent-moved.json",preview:true));Assert.True(File.Exists(Path.Combine(r.Data,"agent.json")));
        Success(agent.Rename("agent.json","agent-moved.json"));var bytes=r.Bytes("agent-moved.json");
        Success(agent.Delete("agent-moved.json",preview:true));Assert.Equal(bytes,r.Bytes("agent-moved.json"));
        Success(agent.Delete("agent-moved.json"));Assert.False(File.Exists(Path.Combine(r.Data,"agent-moved.json")));
        Success(agent.Restore("agent-moved.json"));Assert.Equal(bytes,r.Bytes("agent-moved.json"));
    }
    [Theory]
    [InlineData("rename")][InlineData("delete")]
    public void Agent_PreviewCountsLinks_IncludingUnsavedChanges(string operation)
    {
        using var r=new Sandbox();var agent=new AgentSession(r.Project,r.Map);var f=r.Loaded("routes.json");Edit(f,"/routes/0/target","\"gone\"");
        var disk=r.Disk();var result=operation=="rename"?agent.Rename("tokens.json","moved.json",true):agent.Delete("tokens.json",true);
        Success(result);Assert.Equal(5,result["linksInto"]!.GetValue<int>());Assert.NotNull(result["wouldDo"]);SameDisk(disk,r);
    }
    [Fact]
    public void Mcp_NewToolSchemas_DescribeTheirActualArguments()
    {
        var tools=McpServer.Tools().Select(n=>n!.AsObject()).ToDictionary(n=>n["name"]!.GetValue<string>());
        foreach(var (name,required) in new[]{("create",new[]{"file","schema"}),("rename",new[]{"file","to"}),("delete",new[]{"file"}),("restore",new[]{"file"})})
        {
            var schema=tools[name]["inputSchema"]!;Assert.Equal(required,schema["required"]!.AsArray().Select(n=>n!.GetValue<string>()));
            foreach(var key in required)Assert.Equal("string",schema["properties"]![key]!["type"]!.GetValue<string>());
            if(name!="restore")Assert.Equal("boolean",schema["properties"]!["preview"]!["type"]!.GetValue<string>());
        }
    }
    [Theory]
    [InlineData("create","missing")][InlineData("create","null")][InlineData("create","wrong")]
    [InlineData("rename","missing")][InlineData("rename","null")][InlineData("rename","wrong")]
    [InlineData("delete","missing")][InlineData("delete","null")][InlineData("delete","wrong")]
    [InlineData("restore","missing")][InlineData("restore","null")][InlineData("restore","wrong")]
    public void Mcp_EachNewTool_RejectsBadArgumentsBeforeWriting(string tool,string kind)
    {
        using var r=new Sandbox();var disk=r.Disk();var a=new JsonObject {["file"]="tokens.json",["schema"]="blank.schema.json",["to"]="moved.json"};
        if(kind=="missing")a.Remove("file");else a["file"]=kind=="null"?null:JsonValue.Create(7);
        var result=Rpc(new McpServer(new AgentSession(r.Project,r.Map)),tool,a);
        Assert.True(result["isError"]!.GetValue<bool>());Assert.Contains("arguments",RpcBody(result)["error"]!.GetValue<string>());SameDisk(disk,r);
    }
    [Fact]
    public void Mcp_FileLifecycle_UsesOneSessionAndRestoresExactBytes()
    {
        using var r=new Sandbox();var server=new McpServer(new AgentSession(r.Project,r.Map));var disk=r.Disk();
        var a=new JsonObject {["file"]="rpc.json",["schema"]="blank.schema.json",["preview"]=true};
        var preview=Rpc(server,"create",a);Assert.False(preview["isError"]!.GetValue<bool>());Assert.NotNull(RpcBody(preview)["wouldDo"]);SameDisk(disk,r);
        a["preview"]=false;Assert.False(Rpc(server,"create",a)["isError"]!.GetValue<bool>());var bytes=r.Bytes("rpc.json");
        Assert.False(Rpc(server,"rename",new(){["file"]="rpc.json",["to"]="rpc-moved.json"})["isError"]!.GetValue<bool>());
        Assert.False(Rpc(server,"delete",new(){["file"]="rpc-moved.json",["preview"]=true})["isError"]!.GetValue<bool>());Assert.Equal(bytes,r.Bytes("rpc-moved.json"));
        Assert.False(Rpc(server,"delete",new(){["file"]="rpc-moved.json"})["isError"]!.GetValue<bool>());Assert.False(File.Exists(Path.Combine(r.Data,"rpc-moved.json")));
        Assert.False(Rpc(server,"restore",new(){["file"]="rpc-moved.json"})["isError"]!.GetValue<bool>());Assert.Equal(bytes,r.Bytes("rpc-moved.json"));
    }
    [Theory]
    [InlineData("create")][InlineData("rename")][InlineData("delete")]
    public void Cli_FileOperations_OnlyPreviewWithoutSave(string operation)
    {
        using var r=new Sandbox();var disk=r.Disk();
        var call=operation switch {"create"=>new[]{"create","cli.json","blank.schema.json"},"rename"=>new[]{"rename","tokens.json","moved.json"},_=>new[]{"delete","tokens.json"}};
        var result=RunCli(r,call);Assert.Equal(0,result.Exit);Assert.NotNull(result.Answer!["wouldDo"]);Assert.Null(result.Answer["done"]);SameDisk(disk,r);
    }
    [Theory]
    [InlineData("create")][InlineData("rename")][InlineData("delete")]
    public void Cli_ExplicitSave_PerformsTheNamedFileOperation(string operation)
    {
        using var r=new Sandbox();var before=r.Bytes("tokens.json");
        var call=operation switch {"create"=>new[]{"create","cli.json","blank.schema.json","--save"},"rename"=>new[]{"rename","tokens.json","moved.json","--save"},_=>new[]{"delete","tokens.json","--save"}};
        var result=RunCli(r,call);Assert.Equal(0,result.Exit);Assert.NotNull(result.Answer!["done"]);
        if(operation=="create")Assert.True(File.Exists(Path.Combine(r.Data,"cli.json")));
        else if(operation=="rename"){Assert.False(File.Exists(Path.Combine(r.Data,"tokens.json")));Assert.Equal(before,r.Bytes("moved.json"));}
        else Assert.False(File.Exists(Path.Combine(r.Data,"tokens.json")));
    }
    [Theory]
    [InlineData("refused",1)][InlineData("arguments",2)][InlineData("readonly",1)]
    public void Cli_FailureExitCodes_KeepDiskAndGiveNextStep(string reason,int expected)
    {
        using var r=new Sandbox();var disk=r.Disk();
        var call=reason switch {"refused"=>new[]{"create","tokens.json","blank.schema.json","--save"},"arguments"=>new[]{"rename","tokens.json"},_=>new[]{"--read-only","delete","tokens.json","--save"}};
        var result=RunCli(r,call);Assert.Equal(expected,result.Exit);SameDisk(disk,r);
        if(expected==2)Assert.Contains("needs more arguments",result.Error);else{Assert.NotNull(result.Answer!["error"]);Assert.NotNull(result.Answer["next"]);}
    }

    private sealed record Caption(int Width,int Height,(int X,int Y)[] Ink,(int X,int Y)[] Paper);
    private static readonly Dictionary<string,Caption> captions=new(StringComparer.Ordinal);
    private static Caption CaptionFor(string text)
    {
        if(captions.TryGetValue(text,out var cached))return cached;
        using var h=new HeadlessHost(700,100);
        float2 start=default,end=default;
        void Draw()
        {
            ImGui.SetNextWindowPos(new float2(0,0),ImGuiCond.Always);ImGui.SetNextWindowSize(new float2(700,100),ImGuiCond.Always);
            ImGui.Begin("A062 caption reference"u8,ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.NoSavedSettings);
            ImGui.Text(text);start=ImGui.GetItemRectMin();end=ImGui.GetItemRectMax();ImGui.End();
        }
        h.Frames(Draw,4);
        var counts=new Dictionary<uint,int>();
        int x0=(int)start.X,y0=(int)start.Y,x1=(int)Math.Ceiling(end.X),y1=(int)Math.Ceiling(end.Y);
        for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)
        {var c=h.Pixels[y*h.Width+x]&0xffffff;counts[c]=counts.GetValueOrDefault(c)+1;}
        var paper=counts.MaxBy(c=>c.Value).Key;
        static int Distance(uint a,uint b)=>Math.Abs((int)(a&255)-(int)(b&255))+Math.Abs((int)(a>>8&255)-(int)(b>>8&255))+Math.Abs((int)(a>>16&255)-(int)(b>>16&255));
        var ink=counts.Where(c=>c.Value>=2).MaxBy(c=>Distance(c.Key,paper)).Key;
        var painted=new List<(int X,int Y)>();
        for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)if((h.Pixels[y*h.Width+x]&0xffffff)!=paper)painted.Add((x,y));
        Assert.NotEmpty(painted);int left=painted.Min(p=>p.X),top=painted.Min(p=>p.Y),right=painted.Max(p=>p.X),bottom=painted.Max(p=>p.Y);
        var yes=new List<(int,int)>();var no=new List<(int,int)>();
        for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++)
        {
            var c=h.Pixels[y*h.Width+x]&0xffffff;
            if(c==ink)yes.Add((x-left,y-top));
            else if(c==paper&&x>left&&x<right&&y>top&&y<bottom)no.Add((x-left,y-top));
        }
        Assert.True(yes.Count>=5&&no.Count>=5);
        var answer=new Caption(right-left+1,bottom-top+1,yes.ToArray(),no.Where((_,i)=>i%Math.Max(1,no.Count/24)==0).ToArray());
        captions[text]=answer;return answer;
    }
    private sealed class Ui : IDisposable
    {
        public readonly HeadlessHost Host;
        public readonly DataWorkspace Workspace;
        private readonly Dictionary<string,Caption> patterns;
        public Action Draw=>Workspace.Draw;
        public Ui(Sandbox r,bool readOnly=false,bool map=true)
        {
            // Reference contexts are completely disposed before the real workspace context exists.
            patterns=new[]{"New","Rename","Delete","Restore","Delete it","Cancel","6 links","Ridge courier"}
                .ToDictionary(t=>t,CaptionFor,StringComparer.Ordinal);
            Host=new HeadlessHost(1600,900){Drawing=false};
            EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            Workspace=new DataWorkspace(readOnly?r.Open(readOnly:true):r.Project,map:map?r.Map:null);
            Assert.True(Workspace.Show("tokens.json"),Workspace.Said);Workspace.Window!.Editor.Select("/tokens/0");
            Host.MoveMouse(Draw,-100,-100);Host.Frames(Draw,5);
        }
        public (float X,float Y)? Find(string caption,string window="Files###panel.project.files")
        {
            Host.Drawing=true;Host.Frame(Draw);var w=ImGui.Internal.FindWindowByName(window);
            var box=w.InnerClipRect;var p=patterns[caption];
            int x0=Math.Max(0,(int)Math.Ceiling(box.Min.X)),y0=Math.Max(0,(int)Math.Ceiling(box.Min.Y));
            int x1=Math.Min(Host.Width,(int)Math.Floor(box.Max.X))-p.Width;
            int y1=Math.Min(Host.Height,(int)Math.Floor(box.Max.Y))-p.Height;
            var first=p.Ink[0];var blank=p.Paper[0];
            for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
            {
                var ink=Host.Pixels[(y+first.Y)*Host.Width+x+first.X]&0xffffff;
                var paper=Host.Pixels[(y+blank.Y)*Host.Width+x+blank.X]&0xffffff;
                if(ink==paper)continue;
                var bad=0;
                foreach(var q in p.Ink)if((Host.Pixels[(y+q.Y)*Host.Width+x+q.X]&0xffffff)!=ink)bad++;
                if(bad>p.Ink.Length*.02)continue;
                bad=0;foreach(var q in p.Paper)if((Host.Pixels[(y+q.Y)*Host.Width+x+q.X]&0xffffff)!=paper)bad++;
                if(bad>p.Paper.Length*.02)continue;
                Host.Drawing=false;return (x+p.Width/2f,y+p.Height/2f);
            }
            Host.Drawing=false;return null;
        }
        public void Click(string caption,string window="Files###panel.project.files")
        {
            var p=Find(caption,window);Assert.True(p is not null,$"Actual caption {caption} not visible in {window}.");
            Host.Click(Draw,p.Value.X,p.Value.Y);Host.Frames(Draw);
        }
        public void Palette(string action)
        {
            Host.Press(Draw,ImGuiKey.P,ctrl:true,shift:true);Assert.True(Workspace.Window!.PaletteOpen);
            Host.Press(Draw,ImGuiKey.A,ctrl:true);Host.Type(Draw,"editor.panel_do.project.files/"+action);
            Host.Press(Draw,ImGuiKey.Enter);Host.Frames(Draw);
        }
        public void NameAndEnter(string name)
        {
            Host.Press(Draw,ImGuiKey.A,ctrl:true);Host.Type(Draw,name);Host.Press(Draw,ImGuiKey.Enter);Host.Frames(Draw);
        }
        public void Escape()=>Host.Press(Draw,ImGuiKey.Escape);
        public void Dispose()=>Host.Dispose();
    }
    [Theory]
    [InlineData("button")][InlineData("palette")]
    public void Window_NewForm_TypedNameAndEnter_CreateAndShowFile(string route)
    {
        using var r=new Sandbox();using var ui=new Ui(r);
        if(route=="button")ui.Click("New");else ui.Palette("new_file");
        Assert.False(File.Exists(Path.Combine(r.Data,"ui-created.json")));
        ui.NameAndEnter("ui-created.json");
        Assert.True(File.Exists(Path.Combine(r.Data,"ui-created.json")));Assert.Equal("ui-created.json",ui.Workspace.Active!.Name);
        Assert.NotNull(ui.Workspace.TableOf("ui-created.json"));Assert.False(ui.Workspace.Active.Dirty);
    }
    [Theory]
    [InlineData("button")][InlineData("palette")]
    public void Window_RenameForm_ShowsLinkCountBeforeTypedRename(string route)
    {
        using var r=new Sandbox();var bytes=r.Bytes("tokens.json");using var ui=new Ui(r);
        if(route=="button")ui.Click("Rename");else ui.Palette("rename_file");
        Assert.NotNull(ui.Find("6 links"));Assert.Equal(bytes,r.Bytes("tokens.json"));
        ui.NameAndEnter("ui-renamed.json");
        Assert.False(File.Exists(Path.Combine(r.Data,"tokens.json")));Assert.Equal(bytes,r.Bytes("ui-renamed.json"));
        Assert.Equal("ui-renamed.json",ui.Workspace.Active!.Name);
    }
    [Theory]
    [InlineData("new_file")][InlineData("rename_file")][InlineData("delete_file")]
    public void Window_EscapeCancelsEachFileForm_WithoutWriting(string action)
    {
        using var r=new Sandbox();var disk=r.Disk();using var ui=new Ui(r);ui.Palette(action);
        if(action!="delete_file"){ui.Host.Press(ui.Draw,ImGuiKey.A,ctrl:true);ui.Host.Type(ui.Draw,"cancelled.json");}
        ui.Escape();Assert.Null(ui.Find("Cancel"));Assert.Equal("tokens.json",ui.Workspace.Active!.Name);
        SameDisk(disk,r);ui.Host.Press(ui.Draw,ImGuiKey.Enter);SameDisk(disk,r);Assert.Empty(r.Project.Deleted);
        Assert.NotNull(r.Project.Find("tokens.json"));Assert.NotNull(r.Project.Find("routes.json"));
    }
    [Fact]
    public void Window_DeleteConfirmation_ShowsLinks_AndRestoreButtonReturnsExactBytes()
    {
        using var r=new Sandbox();var bytes=r.Bytes("tokens.json");using var ui=new Ui(r);
        ui.Click("Delete");Assert.NotNull(ui.Find("6 links"));Assert.Equal(bytes,r.Bytes("tokens.json"));
        ui.Click("Delete it");Assert.False(File.Exists(Path.Combine(r.Data,"tokens.json")));Assert.Null(ui.Workspace.Active);
        ui.Click("Restore");Assert.Equal(bytes,r.Bytes("tokens.json"));Assert.Empty(r.Project.Deleted);
    }
    [Fact]
    public void Window_ReferencedByChoice_GoesToReferrer_AndBackReturns()
    {
        using var r=new Sandbox();using var ui=new Ui(r);
        Assert.Equal(4,ui.Workspace.ReferencedBy().Count);
        ui.Click("Ridge courier","Referenced by###panel.project.references");
        Assert.Equal("routes.json",ui.Workspace.Active!.Name);Assert.Equal("/routes/0",ui.Workspace.Window!.Editor.Primary);
        Assert.True(ui.Workspace.Back());Assert.Equal("tokens.json",ui.Workspace.Active!.Name);Assert.Equal("/tokens/0",ui.Workspace.Window!.Editor.Primary);
        Assert.Empty(r.Project.Dirty);
    }
    [Theory]
    [InlineData("new_file")][InlineData("rename_file")][InlineData("delete_file")]
    public void Window_ReadOnlyPaletteActions_ExplainWhyUnavailable(string action)
    {
        using var r=new Sandbox();var disk=r.Disk();using var ui=new Ui(r,readOnly:true);
        var id="editor.panel_do.project.files/"+action;
        var entry=ui.Workspace.Window!.PaletteEntries("").Single(e=>e.Id==id);
        Assert.Equal("the project is open read-only",entry.Unavailable);
        ui.Host.Press(ui.Draw,ImGuiKey.P,ctrl:true,shift:true);Assert.True(ui.Workspace.Window.PaletteOpen);
        ui.Host.Type(ui.Draw,id);ui.Host.Press(ui.Draw,ImGuiKey.Enter);SameDisk(disk,r);
        Assert.Null(ui.Find("New"));Assert.Null(ui.Find("Rename"));Assert.Null(ui.Find("Delete"));
    }
    [Fact]
    public void FilesCommands_AllUnavailableReasons_ReflectCurrentState()
    {
        using var r=new Sandbox();var w=new DataWorkspace(r.Project);
        var commands=w.FilesPanel.Commands!.ToDictionary(c=>c.Id);
        Assert.Null(commands["new_file"].Unavailable!());Assert.Equal("no file is shown",commands["rename_file"].Unavailable!());
        Assert.Equal("no file is shown",commands["delete_file"].Unavailable!());Assert.Equal("no file was deleted here",commands["restore_file"].Unavailable!());
        Assert.Equal("nothing to save",commands["save_all"].Unavailable!());Assert.Null(commands["refresh"].Unavailable);
        Assert.True(w.Show("tokens.json"));Assert.Null(commands["rename_file"].Unavailable!());Assert.Null(commands["delete_file"].Unavailable!());
        Edit(w.Active!,"/tokens/0/caption","\"Unsaved\"");Assert.Null(commands["save_all"].Unavailable!());
        Assert.True(w.Active!.Session!.Undo());Assert.True(w.DeleteFile("tokens.json"));Assert.Null(commands["restore_file"].Unavailable!());
        Assert.True(commands["restore_file"].Run());Assert.True(File.Exists(Path.Combine(r.Data,"tokens.json")));
    }
}
