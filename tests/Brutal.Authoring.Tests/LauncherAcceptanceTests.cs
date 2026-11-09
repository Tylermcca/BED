using System.Text;
using System.Text.Json;
using Brutal.Authoring;
using Brutal.Authoring.Data;
using Brutal.Authoring.Editor;
using Brutal.Authoring.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using App = Brutal.Authoring.Launcher.Program;

namespace Brutal.Authoring.Tests;

// INVENTED projects, settings and scripted input. BORROWED targets: A064's public XML contracts.
// MIXED: black-box launcher/CLI calls and actual headless ImGui events/pixels. Never a native window.
[Collection(ImGuiCollection.Name)]
public sealed class LauncherAcceptanceTests
{
    private const string Data = """
        {"nodes":[{"id":"garnet","name":"Courier light","target":"pearl"},{"id":"pearl","name":"Watch lantern"}],
         "settings":{"name":"Lens pilot","count":2}}
        """;
    private const string Schema = """
        {"type":"object","properties":{"nodes":{"type":"array","items":{"type":"object",
          "properties":{"id":{"type":"string"},"name":{"type":"string"},"target":{"type":"string"}},"required":["id","name"]}},
         "settings":{"type":"object","properties":{"name":{"type":"string"},"count":{"type":"integer","minimum":0}}}},
         "required":["nodes","settings"]}
        """;
    private const string Map = """
        {"game":"Starter","labels":[{"file":"nodes.json","list":"/nodes","field":"name"}],"references":[
         {"file":"nodes.json","at":"/nodes/*/target","target":{"file":"nodes.json","list":"/nodes","key":"id"},
          "evidence":"Invented A064 courier/watch nodes."}]}
        """;
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; }=Path.Combine(Path.GetTempPath(),"a064-launcher-"+Guid.NewGuid().ToString("N"));
        public string Settings => Path.Combine(Root,"settings");
        public string RecentFile => Path.Combine(Settings,"recent.json");
        public Sandbox(){Directory.CreateDirectory(Root);}
        public ProjectPaths Project(string name="Beacon workshop",bool map=false,bool recipes=false)
        {
            var at=Path.Combine(Root,name);var data=Path.Combine(at,"data");var schemas=Path.Combine(at,"schemas");
            Directory.CreateDirectory(data);Directory.CreateDirectory(schemas);
            File.WriteAllText(Path.Combine(data,"nodes.json"),Data,new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(schemas,"nodes.schema.json"),Schema,new UTF8Encoding(false));
            if(recipes)
            {
                File.WriteAllText(Path.Combine(data,"recipes.json"),"""{"recipes":[{"makes":"amber_thread","from":"reed_fiber"}]}""");
                File.WriteAllText(Path.Combine(schemas,"recipes.schema.json"),"""{"type":"object","properties":{"recipes":{"type":"array","items":{"type":"object","properties":{"makes":{"type":"string"},"from":{"type":"string"}},"required":["makes","from"]}}}}""");
            }
            var m=Path.Combine(at,"reference_map.json");if(map)File.WriteAllText(m,Map,new UTF8Encoding(false));
            return new(data,schemas,map?m:null);
        }
        public RecentProjects Recent(int limit=10)=>new(RecentFile,limit);
        public ProjectLauncher Launcher(ProjectPaths? sample=null)=>new(Recent(),sample,StartWindowTestProjects.Creator(Root),Path.Combine(Root,"created"));
        public byte[] Bytes(ProjectPaths p)=>File.ReadAllBytes(Path.Combine(p.Data,"nodes.json"));
        public void Dispose()
        {
            var full=Path.GetFullPath(Root);var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("a064-launcher-",StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected cleanup boundary.");
            Directory.Delete(full,true);
        }
    }
    private sealed class Adapter(string name,bool recognises=true,bool readOnly=false,bool failRecognition=false,bool failOpen=false) : IProjectAdapter
    {
        public int Opened,Configured;
        public string Name=>name;
        public bool Recognises(ProjectPaths p)=>failRecognition?throw new InvalidOperationException("recognition unavailable"):recognises;
        public DataProject Open(ProjectPaths p)
        {
            Opened++;if(failOpen)throw new IOException("Adapter's catalog could not be read; choose plain data.");
            return DataProject.Open(p.Data,p.Schemas,readOnlyFile:readOnly?_=>"adapter owns generated nodes":null);
        }
        public void Configure(EditorModel<DataDocument> editor,DataTableView table)
        {
            Configured++;editor.Add(new Panel("test.courier","Courier adapter",()=>{}));
        }
    }
    private static void Dirty(ProjectLauncher l,string value="Draft courier")
    {
        var f=l.Workspace!.Project.Find("nodes.json")!;Assert.True(f.Open());
        Assert.True(f.Session!.Execute(new SetValue("/nodes/0/name",JsonSerializer.SerializeToElement(value))));
        Assert.True(f.Dirty);
    }
    private static string Folder(ProjectPaths p)=>Path.GetDirectoryName(p.Data)!;

    [Theory]
    [InlineData("data")][InlineData("schemas")]
    public void ProjectPaths_MissingEitherRequiredFolder_IsNotAProject(string missing)
    {
        using var r=new Sandbox();var at=Path.Combine(r.Root,"incomplete");Directory.CreateDirectory(Path.Combine(at,missing=="data"?"schemas":"data"));
        Assert.Null(ProjectPaths.In(at));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void ProjectPaths_UsesUsualFoldersAndOnlyTheOptionalUnderscoreMap(bool withMap)
    {
        using var r=new Sandbox();var p=r.Project(map:withMap);File.WriteAllText(Path.Combine(Folder(p),"reference-map.json"),"not the conventional map");
        var found=ProjectPaths.In(Folder(p));Assert.NotNull(found);
        Assert.Equal(Path.GetFullPath(p.Data),found.Data);Assert.Equal(Path.GetFullPath(p.Schemas),found.Schemas);
        Assert.Equal(p.Map,found.Map);
    }
    [Theory]
    [InlineData("data","Beacon workshop")][InlineData("DATA","Beacon workshop")][InlineData("Records","Records")]
    public void ProjectName_IsHumanReadableAndIgnoresTrailingSeparators(string name,string expected)
    {
        using var r=new Sandbox();var data=Path.Combine(r.Root,"Beacon workshop",name)+Path.DirectorySeparatorChar;
        Assert.Equal(expected,ProjectLauncher.Name(new(data,r.Root)));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Open_PlainProjectWorksWithoutAdapter_WithOrWithoutLinkMap(bool map)
    {
        using var r=new Sandbox();var p=r.Project(map:map);var bytes=r.Bytes(p);var l=r.Launcher();
        Assert.True(l.Open(p),l.Said);Assert.NotNull(l.Workspace);Assert.Equal(p,l.Current);Assert.Null(l.Using);
        Assert.Equal("nodes.json",l.Workspace.Active!.Name);Assert.NotNull(l.Workspace.TableOf("nodes.json"));
        Assert.Equal(map,l.Workspace.Map is not null);Assert.Single(l.Recent.Items);Assert.Equal(bytes,r.Bytes(p));Assert.Empty(l.Workspace.Project.Dirty);
        if(map)Assert.Equal(new ReferenceTarget("nodes.json","/nodes/1"),l.Workspace.TableOf("nodes.json")!.LinkOf("/nodes/0","target"));
    }
    [Theory]
    [InlineData("data-empty","Choose the data folder")]
    [InlineData("schema-empty","Choose the schema folder")]
    [InlineData("data-missing","Browse")]
    [InlineData("schema-missing","Browse")]
    [InlineData("no-schemas","right folder")]
    public void Open_FolderRefusals_AreActionableAndDoNotRememberProject(string problem,string reason)
    {
        using var r=new Sandbox();var p=r.Project();var l=r.Launcher();
        p=problem switch {"data-empty"=>p with{Data="  "},"schema-empty"=>p with{Schemas=""},"data-missing"=>p with{Data=Path.Combine(r.Root,"missing-data")},
            "schema-missing"=>p with{Schemas=Path.Combine(r.Root,"missing-schemas")},_=>p};
        if(problem=="no-schemas")File.Delete(Path.Combine(p.Schemas,"nodes.schema.json"));
        Assert.False(l.Open(p));Assert.Contains(reason,l.Said);Assert.Null(l.Workspace);Assert.Null(l.Current);Assert.Empty(l.Recent.Items);
    }
    [Theory]
    [InlineData("syntax")][InlineData("shape")][InlineData("missing")]
    public void Open_LinkMapFailures_AreRefusedWithoutLosingAnExistingCleanProject(string problem)
    {
        using var r=new Sandbox();var first=r.Project("First");var second=r.Project("Second",map:true);var l=r.Launcher();Assert.True(l.Open(first));
        var workspace=l.Workspace;
        if(problem=="missing")File.Delete(second.Map!);else File.WriteAllText(second.Map!,problem=="syntax"?"{broken":"[]");
        Assert.False(l.Open(second));Assert.Contains("map",l.Said,StringComparison.OrdinalIgnoreCase);
        Assert.Same(workspace,l.Workspace);Assert.Equal(first,l.Current);Assert.Equal(new[]{first},l.Recent.Items);
        if(problem=="missing")Assert.Contains("Leave it empty",l.Said);
    }
    [Fact]
    public void Open_InvalidSchema_ReportsItAndKeepsStartScreen()
    {
        using var r=new Sandbox();var p=r.Project();File.WriteAllText(Path.Combine(p.Schemas,"nodes.schema.json"),"{ broken");
        var l=r.Launcher();Assert.False(l.Open(p));Assert.Contains("can't be opened",l.Said);Assert.Null(l.Workspace);Assert.Empty(l.Recent.Items);
    }
    [Theory]
    [InlineData("schema")][InlineData("map")]
    public void Open_AFileLockedByAnotherWriter_IsARefusalNotAnException(string which)
    {
        using var r=new Sandbox();var p=r.Project(map:true);var path=which=="schema"?Path.Combine(p.Schemas,"nodes.schema.json"):p.Map!;
        using var held=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);var l=r.Launcher();
        Assert.False(l.Open(p));Assert.Contains("can't",l.Said);Assert.Null(l.Workspace);Assert.Empty(l.Recent.Items);
    }
    [Fact]
    public void Open_BadDataFile_DoesNotHideOtherUsableFiles()
    {
        using var r=new Sandbox();var p=r.Project();File.WriteAllText(Path.Combine(p.Data,"zbroken.json"),"{broken");
        File.WriteAllText(Path.Combine(p.Schemas,"zbroken.schema.json"),"""{"type":"object"}""");
        var l=r.Launcher();Assert.True(l.Open(p),l.Said);Assert.Contains("1 that can't open",l.Said);
        Assert.Equal("nodes.json",l.Workspace!.Active!.Name);Assert.NotEmpty(l.Workspace.Project.Find("zbroken.json")!.Problems);
    }
    [Fact]
    public void Open_AnEmptyDataFolder_StillShowsAWorkspaceAndCanBeClosed()
    {
        using var r=new Sandbox();var p=r.Project();File.Delete(Path.Combine(p.Data,"nodes.json"));var l=r.Launcher();
        Assert.True(l.Open(p),l.Said);Assert.NotNull(l.Workspace);Assert.Null(l.Workspace.Active);Assert.Empty(l.Workspace.Project.Files);Assert.True(l.Close());
    }
    [Fact]
    public void OpenFolder_ExplainsLayout_ThenOpensAValidProject()
    {
        using var r=new Sandbox();var l=r.Launcher();Assert.False(l.OpenFolder(r.Root));Assert.Contains("Choose the two folders separately",l.Said);
        var p=r.Project();Assert.True(l.OpenFolder(Folder(p)),l.Said);Assert.Equal(p,l.Current);
    }
    [Fact]
    public void Open_DirtyProjectCannotBeReplaced_AndItsDraftStaysIntact()
    {
        using var r=new Sandbox();var p=r.Project();var next=r.Project("Replacement");var l=r.Launcher();Assert.True(l.Open(p));Dirty(l);
        var workspace=l.Workspace;var bytes=r.Bytes(p);
        Assert.False(l.Open(next));Assert.Contains("unsaved",l.Said);Assert.Same(workspace,l.Workspace);Assert.Equal(p,l.Current);
        Assert.Equal("Draft courier",l.Workspace!.Active!.Session!.Current.At("/nodes/0/name")!.Value.GetString());Assert.Equal(bytes,r.Bytes(p));Assert.Single(l.Recent.Items);
    }
    [Fact]
    public void Open_CleanReplacement_IsCurrentAndMovesToRecentFront()
    {
        using var r=new Sandbox();var a=r.Project("Alpha");var b=r.Project("Beta");var l=r.Launcher();Assert.True(l.Open(a));Assert.True(l.Open(b));
        Assert.Equal(b,l.Current);Assert.Equal(new[]{b,a},l.Recent.Items);Assert.False(l.Workspace!.Active!.Dirty);
    }
    [Fact]
    public void Close_DirtyRefusalThenDiscard_ReturnsToStartWithoutSaving()
    {
        using var r=new Sandbox();var p=r.Project();var bytes=r.Bytes(p);var l=r.Launcher();Assert.True(l.Open(p));Dirty(l);
        var w=l.Workspace;Assert.False(l.Close());Assert.Contains("Choose Save",l.Said);Assert.Contains("Don't Save",l.Said);Assert.Same(w,l.Workspace);
        Assert.True(l.Close(discard:true));Assert.Null(l.Workspace);Assert.Null(l.Current);Assert.Equal(bytes,r.Bytes(p));
        Assert.True(l.Open(p));Assert.Equal("Courier light",l.Workspace!.Active!.Session!.Current.At("/nodes/0/name")!.Value.GetString());
    }
    [Fact]
    public void Close_CleanProject_LeavesRecentEntryAndCanReopen()
    {
        using var r=new Sandbox();var p=r.Project();var l=r.Launcher();Assert.True(l.Open(p));Assert.True(l.Close());
        Assert.Null(l.Workspace);Assert.Null(l.Current);Assert.Single(l.Recent.Items);Assert.True(l.Open(l.Recent.Items[0]));Assert.Single(l.Recent.Items);
    }
    [Fact]
    public void Close_ResetsTheAdapterOfTheOpenProject()
    {
        using var r=new Sandbox();var p=r.Project();var adapter=new Adapter("Courier rules");var l=r.Launcher();l.Adapter=adapter;
        Assert.True(l.Open(p));Assert.Same(adapter,l.Using);Assert.True(l.Close());Assert.Null(l.Current);Assert.Null(l.Workspace);Assert.Null(l.Using);
    }
    [Fact]
    public void Recent_AddMovesToFrontAndEnforcesLimitAcrossReload()
    {
        using var r=new Sandbox();var a=r.Project("Alpha");var b=r.Project("Beta");var c=r.Project("Gamma");var recent=r.Recent(2);
        recent.Add(a);recent.Add(b);recent.Add(a);Assert.Equal(new[]{a,b},recent.Items);recent.Add(c);
        Assert.Equal(new[]{c,a},recent.Items);Assert.Equal(new[]{c,a},r.Recent(2).Items);
    }
    [Fact]
    public void Recent_SameFoldersIgnoringCase_RefreshOptionalMapInsteadOfDuplicating()
    {
        using var r=new Sandbox();var a=r.Project(map:true);var recent=r.Recent();recent.Add(a);
        var changed=new ProjectPaths(a.Data.ToUpperInvariant(),a.Schemas.ToUpperInvariant(),null);recent.Add(changed);
        Assert.Equal(new[]{changed},recent.Items);Assert.Single(r.Recent().Items);
    }
    [Fact]
    public void Recent_RemoveDoesNotTouchAnyProjectFiles()
    {
        using var r=new Sandbox();var p=r.Project();var bytes=r.Bytes(p);var recent=r.Recent();recent.Add(p);recent.Remove(p);
        Assert.Empty(recent.Items);Assert.Empty(r.Recent().Items);Assert.Equal(bytes,r.Bytes(p));Assert.True(Directory.Exists(p.Schemas));
    }
    [Theory]
    [InlineData("{broken")][InlineData("{}")][InlineData("null")]
    [InlineData("[null,{\"Data\":\"\",\"Schemas\":\"x\"}]")]
    public void Recent_CorruptOrUnusableSettings_StartEmpty(string text)
    {
        using var r=new Sandbox();Directory.CreateDirectory(r.Settings);File.WriteAllText(r.RecentFile,text);
        Assert.Empty(r.Recent().Items);
    }
    [Fact]
    public void Recent_MissingProjectFolders_AreRetainedUntilExplicitRemoval()
    {
        using var r=new Sandbox();var gone=new ProjectPaths(Path.Combine(r.Root,"gone/data"),Path.Combine(r.Root,"gone/schemas"));
        var recent=r.Recent();recent.Add(gone);Assert.Equal(new[]{gone},r.Recent().Items);recent.Remove(gone);Assert.Empty(r.Recent().Items);
    }
    [Fact]
    public void Recent_LockedSettings_CannotBreakOpeningOrRememberingInMemory()
    {
        using var r=new Sandbox();var p=r.Project();Directory.CreateDirectory(r.Settings);File.WriteAllText(r.RecentFile,"[]");
        using var held=new FileStream(r.RecentFile,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        var recent=r.Recent();Assert.Empty(recent.Items);var l=new ProjectLauncher(recent);
        Assert.True(l.Open(p),l.Said);Assert.Single(recent.Items);Assert.NotNull(l.Workspace);
    }
    [Fact]
    public void Recent_UnwritableSettingsParent_IsNonFatal()
    {
        using var r=new Sandbox();var p=r.Project();var parent=Path.Combine(r.Root,"not-a-folder");File.WriteAllText(parent,"owned file");
        var recent=new RecentProjects(Path.Combine(parent,"recent.json"));recent.Add(p);Assert.Single(recent.Items);
        Assert.Equal("owned file",File.ReadAllText(parent));
    }
    [Theory]
    [InlineData("directory")][InlineData("file")][InlineData("missing-child")]
    public void Picker_StartsAtKnownFolderOrAnExistingFilesParent(string from)
    {
        using var r=new Sandbox();var p=r.Project();var picker=new FolderPicker();
        var path=from=="directory"?p.Data:Path.Combine(p.Data,from=="file"?"nodes.json":"not-there.json");picker.Start(path,chooseFile:from=="file");
        Assert.Equal(Path.GetFullPath(p.Data),picker.At);
    }
    [Fact]
    public void Picker_GoesDownAndUp_AndRejectsNonexistentFolderWithoutMoving()
    {
        using var r=new Sandbox();var bridge=Path.Combine(r.Root,"Bridge");Directory.CreateDirectory(bridge);var picker=new FolderPicker();picker.Start(r.Root,false);
        Assert.True(picker.Go("Bridge"));Assert.Equal(bridge,picker.At);Assert.False(picker.Go("not-there"));Assert.Equal(bridge,picker.At);
        Assert.True(picker.Go(".."));Assert.Equal(r.Root,picker.At);
    }
    [Fact]
    public void Adapters_AreAutomaticInLoadOrder_AndRecognitionFailureIsNotFatal()
    {
        using var r=new Sandbox();var p=r.Project();var bad=new Adapter("Broken recognizer",failRecognition:true);
        var first=new Adapter("First");var later=new Adapter("Later");var l=r.Launcher();l.Loaded=[bad,first,later];
        Assert.True(l.Open(p),l.Said);Assert.Same(first,l.Using);Assert.Equal(1,first.Opened);Assert.Equal(0,later.Opened);
        Assert.Contains("First",l.Said);Assert.Equal(1,first.Configured);Assert.Contains(l.Workspace!.Window!.Editor.Panels,x=>x.Id=="test.courier");
    }
    [Theory]
    [InlineData("plain")][InlineData("explicit")][InlineData("no-recogniser")]
    public void Adapters_AreOptionalAndExplicitChoiceWins(string choice)
    {
        using var r=new Sandbox();var p=r.Project();var selected=new Adapter("Chosen",recognises:false);var auto=new Adapter("Auto",recognises:choice!="no-recogniser");
        var l=r.Launcher();l.Loaded=[auto];l.Adapter=choice=="explicit"?selected:null;l.NoAdapter=choice=="plain";
        Assert.True(l.Open(p),l.Said);
        if(choice=="explicit"){Assert.Same(selected,l.Using);Assert.Equal(1,selected.Opened);Assert.Equal(0,auto.Opened);}
        else{Assert.Null(l.Using);Assert.Equal(0,auto.Opened);}
    }
    [Fact]
    public void Adapter_OpensWithItsPolicyAndConfiguresEachShownFilesEditor()
    {
        using var r=new Sandbox();var p=r.Project(recipes:true);var adapter=new Adapter("Protected",readOnly:true);var l=r.Launcher();l.Adapter=adapter;
        Assert.True(l.Open(p),l.Said);Dirty(l);Assert.False(l.Workspace!.Active!.Save());Assert.Contains("adapter owns",l.Workspace.Active.Problems[0].Message);
        Assert.True(l.Workspace.Show("recipes.json"));Assert.Equal(2,adapter.Configured);
    }
    [Fact]
    public void Adapter_ReadFailure_IsReportedWithoutEnteringAWorkspace()
    {
        using var r=new Sandbox();var l=r.Launcher();l.Adapter=new Adapter("Unavailable",failOpen:true);
        Assert.False(l.Open(r.Project()));Assert.Contains("choose plain data",l.Said,StringComparison.OrdinalIgnoreCase);Assert.Null(l.Workspace);Assert.Empty(l.Recent.Items);
    }
    private static string StarterDll()
    {
        var artifacts=Environment.GetEnvironmentVariable("ArtifactsPath");
        if(artifacts is not null)
        {
            var root=Path.Combine(artifacts,"bin","StarterAdapter");
            if(Directory.Exists(root)){var dlls=Directory.GetFiles(root,"StarterAdapter.dll",SearchOption.AllDirectories);if(dlls.Length>0)return dlls.Order(StringComparer.Ordinal).First();}
        }
        for(var d=new DirectoryInfo(Environment.CurrentDirectory);d is not null;d=d.Parent)
        {
            if(!File.Exists(Path.Combine(d.FullName,"Brutal.Authoring.slnx")))continue;
            var bin=Path.Combine(d.FullName,"samples","StarterAdapter","bin");
            if(Directory.Exists(bin)){var dlls=Directory.GetFiles(bin,"StarterAdapter.dll",SearchOption.AllDirectories);if(dlls.Length>0)return dlls.Order(StringComparer.Ordinal).First();}
        }
        throw new InvalidOperationException("Build the already-referenced StarterAdapter project before running launcher acceptance tests.");
    }
    [Fact]
    public void Adapter_LoadsRealStarterDllAndEnforcesItsCrossFieldRule()
    {
        using var r=new Sandbox();var p=r.Project(map:true,recipes:true);var result=Adapters.Load([StarterDll()]);
        Assert.Empty(result.Problems);var adapter=Assert.Single(result.Loaded);Assert.True(adapter.Recognises(p));
        var l=r.Launcher();l.Loaded=result.Loaded;Assert.True(l.Open(p),l.Said);Assert.Same(adapter,l.Using);
        Assert.True(l.Workspace!.Show("recipes.json"));var f=l.Workspace.Active!;var bytes=File.ReadAllBytes(f.Path);
        Assert.False(f.Session!.Execute(new SetValue("/recipes/0/from",JsonSerializer.SerializeToElement("amber_thread"))));
        Assert.Contains(f.Session.LastIssues,x=>x.Message.Contains("can't use up"));Assert.False(f.Dirty);Assert.Equal(bytes,File.ReadAllBytes(f.Path));
    }
    [Theory]
    [InlineData("missing")][InlineData("corrupt")][InlineData("ordinary-library")]
    public void Adapter_LoadRefusals_ReturnReasonsInsteadOfThrowing(string kind)
    {
        using var r=new Sandbox();var path=kind=="ordinary-library"?typeof(DataProject).Assembly.Location:Path.Combine(r.Root,"MissingAdapter.dll");
        if(kind=="corrupt")File.WriteAllText(path,"This is invented text, not a DLL.");
        var result=Adapters.Load([path]);Assert.Empty(result.Loaded);Assert.NotEmpty(result.Problems);
        Assert.Contains(Path.GetFileName(path),result.Problems[0]);Assert.False(string.IsNullOrWhiteSpace(result.Problems[0]));
    }
    [Fact]
    public void Adapter_InvalidPath_IsAlsoReturnedAsAProblem_NotThrown()
    {
        using var r=new Sandbox();var path=Path.Combine(r.Root,"bad"+'\0'+"Adapter.dll");
        var exception=Record.Exception(()=>{var result=Adapters.Load([path]);Assert.Empty(result.Loaded);Assert.NotEmpty(result.Problems);});
        Assert.Null(exception);
    }
    [Fact]
    public void Adapter_Discovery_RecursesOnlyThroughNamedAdapterDlls()
    {
        using var r=new Sandbox();var nested=Path.Combine(r.Root,"adapters","nested");Directory.CreateDirectory(nested);
        var a=Path.Combine(nested,"CourierAdapter.dll");var other=Path.Combine(nested,"Library.dll");File.WriteAllText(a,"probe");File.WriteAllText(other,"probe");
        Assert.Equal(new[]{a},Adapters.In(Path.Combine(r.Root,"adapters")));Assert.Empty(Adapters.In(Path.Combine(r.Root,"absent")));
    }

    private static (int Exit,string Output,string Error,string Shot) Cli(Sandbox r,params string[] args)
    {
        var output=new StringWriter();var errors=new StringWriter();var shot=Path.Combine(r.Root,"shot-"+Guid.NewGuid().ToString("N")+".png");
        // Every invocation has the real --shot option; never exercise a window-launch path.
        var code=App.Run(args.Concat(new[]{"--shot",shot}).ToArray(),output,errors,r.Settings);
        return(code,output.ToString(),errors.ToString(),shot);
    }
    [Theory]
    [InlineData("--help")][InlineData("-h")][InlineData("/?")]
    public void Cli_HelpIsSuccessfulWithoutOpeningOrRememberingAnything(string option)
    {
        using var r=new Sandbox();var result=Cli(r,option);Assert.Equal(0,result.Exit);Assert.Contains("usage: BED",result.Output);
        Assert.Empty(result.Error);Assert.False(File.Exists(result.Shot));Assert.False(File.Exists(r.RecentFile));
    }
    [Theory]
    [InlineData("folder")][InlineData("separate-folders")]
    public void Cli_OpensProjectHeadlessAndRemembersItInOnlySuppliedSettings(string layout)
    {
        using var r=new Sandbox();var p=r.Project(map:true);var bytes=r.Bytes(p);
        var result=layout=="folder"?Cli(r,Folder(p)):Cli(r,"--data",p.Data,"--schemas",p.Schemas,"--map",p.Map!);
        Assert.Equal(0,result.Exit);Assert.Contains("Opened",result.Output);Assert.Contains("Drew the screen",result.Output);
        var png=File.ReadAllBytes(result.Shot);Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},png.Take(8));
        Assert.True(png.Length>1000);Assert.Equal(new[]{p},r.Recent().Items);Assert.Equal(bytes,r.Bytes(p));
    }
    [Fact]
    public void Cli_StartScreenCanBeDrawnWithNoProject()
    {
        using var r=new Sandbox();var result=Cli(r);Assert.Equal(0,result.Exit);Assert.Contains("Drew the screen",result.Output);
        Assert.True(File.Exists(result.Shot));Assert.Empty(r.Recent().Items);
    }
    [Theory]
    [InlineData("unknown-option")][InlineData("only-data")][InlineData("only-schemas")][InlineData("two-modes")]
    public void Cli_BadArgumentsReturnTwoWithoutAWindow(string kind)
    {
        using var r=new Sandbox();var p=r.Project();
        var result=kind switch
        {
            "unknown-option"=>Cli(r,"--not-an-option"),
            "only-data"=>Cli(r,"--data",p.Data),
            "only-schemas"=>Cli(r,"--schemas",p.Schemas),
            _=>Cli(r,Folder(p),"--data",p.Data,"--schemas",p.Schemas)
        };
        Assert.Equal(2,result.Exit);Assert.Contains("usage:",result.Error);Assert.False(File.Exists(result.Shot));Assert.Empty(r.Recent().Items);
    }
    [Theory]
    [InlineData("layout")][InlineData("missing-data")][InlineData("missing-map")][InlineData("bad-schema")]
    public void Cli_ProjectRefusalsReturnOneAndNeverProduceASuccessShot(string kind)
    {
        using var r=new Sandbox();var p=r.Project(map:true);(int Exit,string Output,string Error,string Shot) result;
        if(kind=="bad-schema")File.WriteAllText(Path.Combine(p.Schemas,"nodes.schema.json"),"{broken");
        result=kind switch
        {
            "layout"=>Cli(r,r.Root),
            "missing-data"=>Cli(r,"--data",Path.Combine(r.Root,"gone"),"--schemas",p.Schemas),
            "missing-map"=>Cli(r,"--data",p.Data,"--schemas",p.Schemas,"--map",Path.Combine(r.Root,"no-map.json")),
            _=>Cli(r,Folder(p))
        };
        Assert.Equal(1,result.Exit);Assert.False(string.IsNullOrWhiteSpace(result.Error));Assert.False(File.Exists(result.Shot));Assert.Empty(r.Recent().Items);
    }
    [Fact]
    public void Cli_OptionalBadAdapterSaysWhy_ButPlainProjectStillOpens()
    {
        using var r=new Sandbox();var p=r.Project();var result=Cli(r,Folder(p),"--adapter",Path.Combine(r.Root,"absentAdapter.dll"));
        Assert.Equal(0,result.Exit);Assert.Contains("didn't load",result.Error);Assert.True(File.Exists(result.Shot));Assert.Single(r.Recent().Items);
    }
    [Fact]
    public void Cli_ExplicitRealAdapter_LoadsAndIsNamedInTheResult()
    {
        using var r=new Sandbox();var p=r.Project(map:true,recipes:true);var result=Cli(r,Folder(p),"--adapter",StarterDll());
        Assert.Equal(0,result.Exit);Assert.Contains("Starter (sample game)",result.Output);Assert.True(File.Exists(result.Shot));
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

    private sealed class Screen : IDisposable
    {
        public readonly HeadlessHost Host;
        public readonly Action Draw;
        private readonly Dictionary<string,Caption> patterns;
        public Screen(Action draw)
        {
            Draw=draw;
            patterns=new[]{"Remove from list","Folder not found","Try the example","Rules folder (schema)","Open project...","Open the sample project","Data folder","Schema folder","Link map (optional)","Close project",
                "Don't Save","Cancel","Save","Remove","(folder gone)","None yet.",
                "Choose this folder","Cancel","[Bridge]","[data]","[schemas]","reference_map.json","Beacon workshop"}
                .Distinct(StringComparer.Ordinal).ToDictionary(x=>x,CaptionFor,StringComparer.Ordinal);
            Host=new HeadlessHost(1600,900){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());
            Host.MoveMouse(Draw,-100,-100);Host.Frames(Draw,5);
        }
        public (float X,float Y)? Find(string caption,string window="BED###launcher")
        {
            Host.Drawing=true;Host.Frame(Draw);var w=ImGui.Internal.FindWindowByName(window);
            if(caption=="Open the sample project")caption="Try the example";
            if(caption=="Schema folder")caption="Rules folder (schema)";
            var rect=w.InnerClipRect;var p=patterns[caption];
            var x0=Math.Max(0,(int)Math.Ceiling(rect.Min.X));var y0=Math.Max(0,(int)Math.Ceiling(rect.Min.Y));
            var x1=Math.Min(Host.Width,(int)Math.Floor(rect.Max.X))-p.Width;
            var y1=Math.Min(Host.Height,(int)Math.Floor(rect.Max.Y))-p.Height;
            if(window.Contains("###launcher.close",StringComparison.Ordinal))
            {
                // Short labels also occur in prose: keep the glyph oracle, restricted to the actual button.
                var button=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Button && n.Label==caption
                    && n.Bounds.X>=rect.Min.X && n.Bounds.Y>=rect.Min.Y
                    && n.Bounds.X+n.Bounds.Width<=rect.Max.X && n.Bounds.Y+n.Bounds.Height<=rect.Max.Y);
                x0=Math.Max(x0,(int)Math.Ceiling(button.Bounds.X));y0=Math.Max(y0,(int)Math.Ceiling(button.Bounds.Y));
                x1=Math.Min(x1,(int)Math.Floor(button.Bounds.X+button.Bounds.Width)-p.Width);
                y1=Math.Min(y1,(int)Math.Floor(button.Bounds.Y+button.Bounds.Height)-p.Height);
            }
            var first=p.Ink[0];var blank=p.Paper[0];
            for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
            {
                var ink=Host.Pixels[(y+first.Y)*Host.Width+x+first.X]&0xffffff;
                var paper=Host.Pixels[(y+blank.Y)*Host.Width+x+blank.X]&0xffffff;
                if(ink==paper)continue;
                int bad=0;foreach(var q in p.Ink)if((Host.Pixels[(y+q.Y)*Host.Width+x+q.X]&0xffffff)!=ink)bad++;
                if(bad>p.Ink.Length*.02)continue;
                // A translucent picker can show varied dark pixels from the form behind it.
                // Require empty glyph positions to differ from actual ink, not to be one flat fill.
                bad=0;
                foreach(var q in p.Paper)
                {
                    var c=Host.Pixels[(y+q.Y)*Host.Width+x+q.X]&0xffffff;
                    var distance=Math.Abs((int)(c&255)-(int)(ink&255))+Math.Abs((int)(c>>8&255)-(int)(ink>>8&255))
                        +Math.Abs((int)(c>>16&255)-(int)(ink>>16&255));
                    if(distance<120)bad++;
                }
                if(bad>p.Paper.Length*.02)continue;
                Host.Drawing=false;return(x+p.Width/2f,y+p.Height/2f);
            }
            Host.Drawing=false;
            Host.Save(Path.Combine(Path.GetTempPath(),"a064-missing-"+caption.Replace(" ","_")+"-"+((uint)window.GetHashCode()).ToString("X8")+".png"));
            return null;
        }
        public void Click(string caption,string window="BED###launcher")
        {
            if(caption=="Remove" && window=="BED###launcher")
            {
                var row=Find("Folder not found")??Find("Beacon workshop");Assert.NotNull(row);Host.MoveMouse(Draw,row.Value.X,row.Value.Y);
                ImGui.GetIO().AddMouseButtonEvent(1,true);Host.Frame(Draw);ImGui.GetIO().AddMouseButtonEvent(1,false);Host.Frames(Draw,2);
                window=System.Runtime.InteropServices.Marshal.PtrToStringUTF8((IntPtr)ImGui.GetCurrentContext().NavWindow.Name)!;
                caption="Remove from list";
            }
            var p=Find(caption,window);Assert.True(p is not null,$"Actual caption {caption} was not visible in {window}.");
            Host.Click(Draw,p.Value.X,p.Value.Y);Host.Frames(Draw);
        }
        public void DoubleClick(string caption){var at=Find(caption);Assert.NotNull(at);Host.Click(Draw,at.Value.X,at.Value.Y);Host.Click(Draw,at.Value.X,at.Value.Y);Host.Frames(Draw);}
        private bool manualShown;
        public void FocusPath(string label)
        {
            if(!manualShown){Host.Press(Draw,ImGuiKey.O,ctrl:true);Host.Frames(Draw);manualShown=true;}
            var p=Find(label);Assert.NotNull(p);
            // All path fields follow their labelled row, beginning after the longest label.
            Host.Click(Draw,250,p.Value.Y);
        }
        public void PathField(string label,string value)
        {
            FocusPath(label);Host.Press(Draw,ImGuiKey.A,ctrl:true);Host.Type(Draw,value);Host.Frames(Draw);
        }
        public void OpenFields(ProjectPaths p)
        {
            PathField("Data folder",p.Data);PathField("Schema folder",p.Schemas);PathField("Link map (optional)",p.Map??"");
            Host.Press(Draw,ImGuiKey.Tab);Host.Press(Draw,ImGuiKey.Tab);Host.Press(Draw,ImGuiKey.Enter);Host.Frames(Draw);
        }
        public void PaletteClose()
        {
            Host.Press(Draw,ImGuiKey.P,ctrl:true,shift:true);
            Host.Press(Draw,ImGuiKey.A,ctrl:true);Host.Type(Draw,"editor.panel_do.project.files/close_project");
            Host.Press(Draw,ImGuiKey.Enter);Host.Frames(Draw);
        }
        public void Dispose()=>Host.Dispose();
    }
    [Fact]
    public void StartScreen_ExplainsEmptyRecentList_AndSampleButtonOpensActualWorkspace()
    {
        using var r=new Sandbox();var p=r.Project();var l=r.Launcher(p);using var ui=new Screen(l.Draw);
        Assert.Null(l.Workspace);Assert.Null(l.Current);Assert.NotNull(ui.Find("None yet."));
        var before=r.Bytes(p);ui.Click("Try the example");Assert.NotNull(l.Workspace);Assert.Equal(Path.Combine(r.Root,"created","Example","data"),l.Current!.Data);Assert.Equal(before,r.Bytes(p));Assert.Single(l.Recent.Items);
        Assert.NotNull(ui.Find("Close project","Files###panel.project.files"));
    }
    [Fact]
    public void StartScreen_TypedSeparatePathsIncludingOptionalMap_OpenByKeyboard()
    {
        using var r=new Sandbox();var p=r.Project(map:true);var l=r.Launcher();using var ui=new Screen(l.Draw);
        ui.OpenFields(p);Assert.NotNull(l.Workspace);Assert.Equal(p,l.Current);Assert.NotNull(l.Workspace.Map);
        Assert.Equal("nodes.json",l.Workspace.Active!.Name);
    }
    [Fact]
    public void StartScreen_QuotedProjectFolderAlone_IsEnough()
    {
        using var r=new Sandbox();var p=r.Project();var l=r.Launcher();using var ui=new Screen(l.Draw);
        ui.PathField("Data folder"," \""+Folder(p)+"\" ");ui.PathField("Schema folder","");ui.PathField("Link map (optional)","");
        ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Enter);
        Assert.Equal(p,l.Current);Assert.NotNull(l.Workspace);
    }
    [Fact]
    public void StartScreen_EmptyPaths_ExplainWhatToChooseWithoutOpeningAnything()
    {
        using var r=new Sandbox();var l=r.Launcher();using var ui=new Screen(l.Draw);
        ui.PathField("Link map (optional)","");ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Enter);
        Assert.Null(l.Workspace);Assert.Empty(l.Recent.Items);Assert.Contains("Choose the data folder",l.Said);
    }
    [Theory]
    [InlineData("data")][InlineData("schemas")]
    public void StartScreen_BrowseFolder_UsesTheControlledFolderWithoutTypingItAgain(string which)
    {
        using var r=new Sandbox();var p=r.Project();var l=r.Launcher();using var ui=new Screen(l.Draw);
        var label=which=="data"?"Data folder":"Schema folder";
        ui.PathField(label,Folder(p));ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Enter);ui.Host.Frames(ui.Draw);
        // BED completes a typed project root to its data/ folder, so the data picker already opens there.
        if(which!="data")ui.Click("["+which+"]","Choose a folder###picker");
        Assert.NotNull(ui.Find("Choose this folder","Choose a folder###picker"));
        ui.Click("Choose this folder","Choose a folder###picker");
        // Complete the other fields through real input; the chosen field is retained.
        if(which=="data")ui.PathField("Schema folder",p.Schemas);else ui.PathField("Data folder",p.Data);
        ui.PathField("Link map (optional)","");ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Enter);
        Assert.Equal(p,l.Current);Assert.NotNull(l.Workspace);
    }
    [Fact]
    public void StartScreen_BrowseLinkMap_ActuallyListsAndChoosesAJsonFile()
    {
        using var r=new Sandbox();var p=r.Project(map:true);var l=r.Launcher();using var ui=new Screen(l.Draw);
        ui.PathField("Data folder",p.Data);ui.PathField("Schema folder",p.Schemas);ui.PathField("Link map (optional)",Folder(p));
        ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Enter);ui.Host.Frames(ui.Draw);
        Assert.NotNull(ui.Find("reference_map.json","Choose a file###picker"));
        ui.Click("reference_map.json","Choose a file###picker");
        ui.FocusPath("Link map (optional)"); // do not retype or mask the picker result
        ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Enter);
        Assert.Equal(p,l.Current);Assert.NotNull(l.Workspace!.Map);
    }
    [Fact]
    public void StartScreen_BrowseCancel_LeavesPathsAndRecentListAlone()
    {
        using var r=new Sandbox();var p=r.Project();var l=r.Launcher();using var ui=new Screen(l.Draw);
        ui.PathField("Data folder",p.Data);ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Enter);
        ui.Click("Cancel","Choose a folder###picker");Assert.Null(l.Workspace);Assert.Empty(l.Recent.Items);
        ui.PathField("Schema folder",p.Schemas);ui.PathField("Link map (optional)","");
        ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Tab);ui.Host.Press(ui.Draw,ImGuiKey.Enter);Assert.Equal(p,l.Current);
    }
    [Fact]
    public void Picker_RenderedFolderChoice_CanGoDownAndChooseIt()
    {
        using var r=new Sandbox();var child=Path.Combine(r.Root,"Bridge");Directory.CreateDirectory(child);
        var picker=new FolderPicker();picker.Start(r.Root,false);string? chosen=null;
        void Draw(){if(picker.Draw(false) is { } result)chosen=result;}
        using var ui=new Screen(Draw);ui.Click("[Bridge]","Choose a folder###picker");Assert.Equal(child,picker.At);
        ui.Click("Choose this folder","Choose a folder###picker");Assert.Equal(child,chosen);
    }
    [Fact]
    public void Picker_RenderedFileChoice_ListsJsonAndReturnsItsFullPath()
    {
        using var r=new Sandbox();var p=r.Project(map:true);var picker=new FolderPicker();picker.Start(p.Map!,true);string? chosen=null;
        void Draw(){if(picker.Draw(true) is { } result)chosen=result;}
        using var ui=new Screen(Draw);ui.Click("reference_map.json","Choose a file###picker");Assert.Equal(p.Map,chosen);
    }
    [Fact]
    public void Picker_RenderedCancel_IsAnEmptyStringNotAChosenFolder()
    {
        using var r=new Sandbox();var picker=new FolderPicker();picker.Start(r.Root,false);string? chosen=null;
        void Draw(){if(picker.Draw(false) is { } result)chosen=result;}
        using var ui=new Screen(Draw);ui.Click("Cancel","Choose a folder###picker");Assert.Equal("",chosen);Assert.Equal(r.Root,picker.At);
    }
    [Fact]
    public void Recent_GoneFolderIsVisiblyMarked_AndRemoveDoesNotDeleteOtherData()
    {
        using var r=new Sandbox();var p=r.Project();var bytes=r.Bytes(p);var l=r.Launcher();
        var gone=new ProjectPaths(Path.Combine(r.Root,"lost/data"),Path.Combine(r.Root,"lost/schemas"));l.Recent.Add(gone);
        using var ui=new Screen(l.Draw);Assert.NotNull(ui.Find("Folder not found"));ui.Click("Remove");
        Assert.Empty(l.Recent.Items);Assert.Empty(r.Recent().Items);Assert.Equal(bytes,r.Bytes(p));
    }
    [Fact]
    public void Recent_RealChoiceReopensAfterFilesCloseProjectButton()
    {
        using var r=new Sandbox();var p=r.Project();var l=r.Launcher(p);using var ui=new Screen(l.Draw);
        ui.OpenFields(p);ui.Click("Close project","Files###panel.project.files");
        Assert.Null(l.Workspace);Assert.Null(l.Current);ui.DoubleClick("Beacon workshop");Assert.Equal(p,l.Current);Assert.Single(l.Recent.Items);
    }
    [Theory]
    [InlineData("button")][InlineData("palette")]
    public void CloseProject_UnsavedConfirmationCanKeepOpenOrDiscard(string route)
    {
        using var r=new Sandbox();var p=r.Project();var bytes=r.Bytes(p);var l=r.Launcher();Assert.True(l.Open(p));Dirty(l);
        using var ui=new Screen(l.Draw);
        if(route=="button")ui.Click("Close project","Files###panel.project.files");else ui.PaletteClose();
        Assert.NotNull(l.Workspace);Assert.Contains("unsaved",l.Said);
        ui.Click("Cancel","Save changes?###launcher.close");Assert.NotNull(l.Workspace);Assert.True(l.Workspace.Active!.Dirty);
        ui.Click("Close project","Files###panel.project.files");ui.Click("Don't Save","Save changes?###launcher.close");
        Assert.Null(l.Workspace);Assert.Null(l.Current);Assert.Equal(bytes,r.Bytes(p));
    }
    [Fact]
    public void CloseProject_SaveAllThenClose_PersistsDraftAndReturnsToStart()
    {
        using var r=new Sandbox();var p=r.Project();var l=r.Launcher();Assert.True(l.Open(p));Dirty(l,"Saved courier");using var ui=new Screen(l.Draw);
        ui.Click("Close project","Files###panel.project.files");ui.Click("Save","Save changes?###launcher.close");
        Assert.Null(l.Workspace);Assert.Null(l.Current);Assert.True(l.Open(p));
        Assert.Equal("Saved courier",l.Workspace!.Active!.Session!.Current.At("/nodes/0/name")!.Value.GetString());
    }
}
