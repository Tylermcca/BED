using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bed;
using Bed.Agent;
using Bed.Data;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;
using ImGui=Brutal.ImGuiApi.ImGui;

namespace Bed.Tests;

// INVENTED neutral fixtures, external-actor schedules and independent byte-level safety checks.
[Collection(ImGuiCollection.Name)]
public sealed class FileOpsAttackTests(ITestOutputHelper output)
{
    const string Schema="rows.schema.json";
    const string Original="{\"$schema\":\"rows.schema.json\",\"items\":[{\"id\":\"a\",\"name\":\"Alpha\",\"amount\":3}]}\r\n";
    const string External="{\"$schema\":\"rows.schema.json\",\"items\":[{\"id\":\"a\",\"name\":\"External\",\"amount\":41}]}\r\n";
    static JsonElement J(string text)=>JsonDocument.Parse(text).RootElement.Clone();
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    static string Hash(string text)=>Hash(Encoding.UTF8.GetBytes(text));
    static string Flat(string text)=>Regex.Replace(text,"\\s+"," ").Trim();
    sealed class Note(string name)
    {public string Name{get;}=name;public string Phase{get;set;}="starting";public Dictionary<string,object?> Values{get;}=[];public List<string> Faults{get;}=[];}
    void Write(Note n)
    {
        var text=JsonSerializer.Serialize(n);output.WriteLine(text);
        if(Environment.GetEnvironmentVariable("A208_OBSERVATIONS") is {Length:>0} path){Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,n.Name+".json"),text);}
    }
    static void Need(Note n,bool yes,string message){if(!yes)n.Faults.Add(message);}
    sealed class Fixture:IDisposable
    {
        internal readonly string Root=Path.Combine(Path.GetTempPath(),"fileops-attack-"+Guid.NewGuid().ToString("N"));
        internal string Data=>Path.Combine(Root,"data");internal string Schemas=>Path.Combine(Root,"schemas");internal string PathOf(string name)=>Path.Combine(Data,name);
        internal readonly DataProject Project;internal readonly AgentSession Agent;internal readonly DataWorkspace? Workspace;internal readonly HeadlessHost? Host;
        internal DataFile File=>Project.Find("rows.json")!;internal DocumentSession<DataDocument> Session=>File.Session!;
        internal Fixture(bool ui,bool readOnly=false,bool links=false)
        {
            Assert.True(OperatingSystem.IsWindows(),"A208 proves Windows filesystem behavior.");
            Directory.CreateDirectory(Data);Directory.CreateDirectory(Schemas);
            System.IO.File.WriteAllText(Path.Combine(Schemas,Schema),"""{"type":"object","required":["items"],"properties":{"$schema":{"type":"string"},"items":{"type":"array","items":{"type":"object","required":["id","name","amount"],"properties":{"id":{"type":"string"},"name":{"type":"string"},"amount":{"type":"integer","minimum":0}}}}}}""",new UTF8Encoding(false));
            System.IO.File.WriteAllText(PathOf("rows.json"),Original,new UTF8Encoding(false));
            System.IO.File.WriteAllBytes(Path.Combine(Root,"outside.json"),Encoding.UTF8.GetBytes("self-authored outside-data sentinel\r\n"));
            ReferenceMap? map=null;
            if(links)
            {
                System.IO.File.WriteAllText(Path.Combine(Schemas,"uses.schema.json"),"{}");System.IO.File.WriteAllText(PathOf("uses.json"),"""{"uses":[{"row":"a"},{"row":"a"}]}""");
                map=ReferenceMap.Read("""{"game":"Neutral file proof","labels":[],"references":[{"file":"uses.json","at":"/uses/*/row","target":{"file":"rows.json","list":"/items","key":"id"},"evidence":"INVENTED fixture"}]}""");
            }
            Project=DataProject.Open(Data,Schemas,readOnly);Agent=new AgentSession(Project,map);Assert.True(File.Open());
            if(ui)
            {
                Host=new HeadlessHost(1280,800){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());ImGui.GetIO().IniFilename=default;
                Workspace=new DataWorkspace(Project,map:map);Assert.True(Workspace.Show("rows.json"),Workspace.Said);
            }
        }
        internal Dictionary<string,string> Bytes()=>Directory.EnumerateFiles(Root,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToDictionary(p=>Path.GetRelativePath(Root,p).Replace('\\','/'),p=>Convert.ToBase64String(System.IO.File.ReadAllBytes(p)));
        internal string Disk()=>Hash(JsonSerializer.Serialize(Bytes()));
        internal string Draft()=>Hash(Session.Current.Root.GetRawText()+"\n"+Session.Revision+"\n"+Session.Journal.Count+"\n"+Session.History.UndoCount);
        internal void Edit(int amount=9)=>Assert.True(Session.Execute(new SetValue("/items/0/amount",J(amount.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        internal string Normal(string text)=>Flat(text.Replace(Root,"<fixture>",StringComparison.OrdinalIgnoreCase));
        internal string Text(DataWorkspace? shown=null)
        {
            if(Host is null)return "";
            void Draw(){ImGui.SetNextWindowPos(new float2(10,10));ImGui.SetNextWindowSize(new float2(1120,730));ImGui.Begin("A208 Files proof");(shown??Workspace)!.FilesPanel.Draw();ImGui.End();}
            Host.Frames(Draw,3);
            var parent=Environment.GetEnvironmentVariable("A208_OBSERVATIONS")??Path.GetTempPath();Directory.CreateDirectory(parent);var path=Path.Combine(parent,"files-text-"+Guid.NewGuid().ToString("N")+".log");
            Host.Frame(()=>{ImGui.LogToFile(0,path);Draw();ImGui.LogFinish();});return System.IO.File.ReadAllText(path);
        }
        public void Dispose()
        {
            Host?.Dispose();
            var boundary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;Assert.StartsWith(boundary,Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);
            foreach(var file in Directory.EnumerateFiles(Root,"*",SearchOption.AllDirectories))System.IO.File.SetAttributes(file,FileAttributes.Normal);
            Directory.Delete(Root,true);
        }
    }
    // A real access-denied simulation, scoped to the owned data directory; Windows's directory ReadOnly bit isn't write protection.
    sealed class DeniedFolder:IDisposable
    {
        readonly DirectoryInfo folder;readonly byte[] original;
        internal DeniedFolder(Fixture f)
        {
            if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
            var full=Path.GetFullPath(f.Data);Assert.StartsWith(Path.GetFullPath(f.Root)+Path.DirectorySeparatorChar,full,StringComparison.OrdinalIgnoreCase);folder=new DirectoryInfo(full);original=folder.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
            var denied=folder.GetAccessControl(AccessControlSections.Access);var sid=WindowsIdentity.GetCurrent().User!;
            denied.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.CreateFiles|FileSystemRights.CreateDirectories|FileSystemRights.Delete|FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.WriteData|FileSystemRights.AppendData|FileSystemRights.WriteAttributes|FileSystemRights.WriteExtendedAttributes,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Deny));
            folder.SetAccessControl(denied);
            try{Assert.Throws<UnauthorizedAccessException>(()=>System.IO.File.WriteAllText(Path.Combine(full,"denied-canary.tmp"),"must not write"));}
            catch{Restore();throw;}
        }
        void Restore()
        {
            if(!OperatingSystem.IsWindows())return;
            var restore=new DirectorySecurity();restore.SetSecurityDescriptorBinaryForm(original,AccessControlSections.Access);
            folder.SetAccessControl(restore);
            Assert.Equal(Rules(restore.GetSecurityDescriptorSddlForm(AccessControlSections.Access)),Rules(folder.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access)));
        }
        // The permission entries only: some drives (GitHub's runners) add an "auto-inherited" flag ("D:AI(...)") when permissions are set back.
        static string Rules(string sddl)=>System.Text.RegularExpressions.Regex.Replace(sddl,"^D:[A-Z]*","D:");
        public void Dispose()=>Restore();
    }
    public static IEnumerable<object[]> Cases()
    {
        foreach(var (id,name) in new[]{("odd","odd \u2603 !.json"),("long",new string('x',300)+".json"),("case","ROWS.json"),("reserved","con.json"),("parent","../outside.json"),("sub","sub/x.json"),("backslash","sub\\x.json"),("empty",""),("colon","bad:name.json"),("normal","spare.json")})
            foreach(var op in new[]{"create","rename"})yield return new object[]{op+"_name_"+id,op,name,"none"};
        foreach(var op in new[]{"create","rename","delete","save"})yield return new object[]{op+"_project_readonly",op,"spare.json","project_readonly"};
        foreach(var op in new[]{"rename","delete","save"})yield return new object[]{op+"_file_readonly",op,"spare.json","file_readonly"};
        foreach(var op in new[]{"create","rename","delete","save"})yield return new object[]{op+"_folder_denied",op,"spare.json","folder_denied"};
        foreach(var race in new[]{"changed","deleted","locked_exclusive","locked_readable"})
            foreach(var op in new[]{"rename","delete","save"})yield return new object[]{op+"_race_"+race,op,"spare.json",race};
        foreach(var op in new[]{"create","rename"})yield return new object[]{op+"_destination_appears",op,"spare.json","destination_appears"};
        foreach(var op in new[]{"rename","delete"})yield return new object[]{op+"_dirty",op,"spare.json","dirty"};
        foreach(var op in new[]{"rename","delete"})yield return new object[]{op+"_links",op,"spare.json","links"};
        yield return new object[]{"refresh_changed_dirty","refresh","","changed_dirty"};yield return new object[]{"refresh_deleted_dirty","refresh","","deleted_dirty"};
        yield return new object[]{"restore_collision","restore","rows.json","restore_collision"};
    }
    sealed record Answer(bool Ok,string Said,JsonObject? Json=null,string? Escaped=null);
    static Answer Act(Fixture f,bool ui,string op,string target)
    {
        if(ui)
        {
            var w=f.Workspace!;bool ok=op switch{"create"=>w.NewFile(target,Schema),"rename"=>w.RenameFile("rows.json",target),"delete"=>w.DeleteFile("rows.json"),"restore"=>w.RestoreFile("rows.json"),"save"=>w.SaveAll(),"refresh"=>Refresh(w),_=>throw new Exception(op)};
            return new(ok,w.Said);
        }
        JsonObject result=op switch{"create"=>f.Agent.Create(target,Schema),"rename"=>f.Agent.Rename("rows.json",target),"delete"=>f.Agent.Delete("rows.json"),"restore"=>f.Agent.Restore("rows.json"),"save"=>f.Agent.Save(),"refresh"=>Describe(f),_=>throw new Exception(op)};
        var refused=result["refused"] is JsonArray a&&a.Count>0;
        var said=result["error"]?.GetValue<string>()??(refused?string.Join(" ",result["refused"]!.AsArray().Select(x=>x?["why"]?.GetValue<string>())):result["done"]?.GetValue<string>()??result["next"]?.GetValue<string>()??result.ToJsonString());
        return new(!result.ContainsKey("error")&&!refused,said,result);
    }
    static bool Refresh(DataWorkspace w){w.Refresh();return true;}
    static JsonObject Describe(Fixture f){f.Project.Refresh();return f.Agent.Describe();}
    static Answer Preview(Fixture f,bool ui,string op,string target)
    {
        if(op is "save" or "refresh" or "restore")return new(true,"No preview API for this operation.");
        if(!ui)
        {
            var r=op switch{"create"=>f.Agent.Create(target,Schema,true),"rename"=>f.Agent.Rename("rows.json",target,true),_=>f.Agent.Delete("rows.json",true)};
            return new(!r.ContainsKey("error"),r["error"]?.GetValue<string>()??r["wouldDo"]?.GetValue<string>()??r.ToJsonString(),r);
        }
        string why;bool ok=op switch{"create"=>f.Project.Create(target,Schema,out why,true),"rename"=>f.Project.Rename("rows.json",target,out why,true),_=>f.Project.Delete("rows.json",out why,true)};return new(ok,why);
    }
    [Theory,MemberData(nameof(Cases))]
    public void Preview_act_bytes_and_person_agent_messages(string name,string op,string target,string scenario)
    {
        var n=new Note(name);Write(n);var routes=new List<object>();var answers=new List<(bool Ok,string Why)>();
        foreach(bool ui in new[]{true,false})
        {
            try
            {
                using var f=new Fixture(ui,scenario=="project_readonly",scenario=="links");
                if(op=="save"||scenario is "dirty" or "changed_dirty" or "deleted_dirty")f.Edit();
                if(scenario=="restore_collision")
                {Assert.True(f.Project.Delete("rows.json",out _));System.IO.File.WriteAllText(f.PathOf("rows.json"),External,new UTF8Encoding(false));}
                if(scenario=="file_readonly")System.IO.File.SetAttributes(f.PathOf("rows.json"),FileAttributes.ReadOnly);
                using var acl=scenario=="folder_denied"?new DeniedFolder(f):null;
                var beforePreview=f.Bytes();var beforePreviewHash=f.Disk();var draft=op=="restore"?null:f.Draft();Answer preview;
                try{preview=Preview(f,ui,op,target);}catch(Exception e){preview=new(false,e.Message,Escaped:e.GetType().Name);n.Faults.Add((ui?"person":"AI")+" preview threw "+e.GetType().Name+": "+e.Message);}
                Need(n,f.Disk()==beforePreviewHash,"Preview changed project bytes/entries.");if(draft is not null)Need(n,f.Draft()==draft,"Preview changed the draft/history.");
                string form="";
                if(scenario=="links"&&ui)
                {
                    var id=EditorWindow<DataDocument>.PanelCommandAction+"project.files/"+(op=="rename"?"rename_file":"delete_file");
                    Need(n,f.Workspace!.Window!.Run(id),"Public Files form action didn't open.");form=f.Text();Need(n,Flat(form).Contains("2 links",StringComparison.OrdinalIgnoreCase),"Person's live form omits the two links that would break.");
                }
                if(scenario is "changed" or "changed_dirty")System.IO.File.WriteAllText(f.PathOf("rows.json"),External,new UTF8Encoding(false));
                if(scenario is "deleted" or "deleted_dirty")System.IO.File.Delete(f.PathOf("rows.json"));
                if(scenario=="destination_appears")System.IO.File.WriteAllText(f.PathOf(target),"external destination bytes\r\n",new UTF8Encoding(false));
                var before=f.Bytes();var beforeHash=f.Disk();Answer act;FileStream? held=null;
                try
                {
                    if(scenario.StartsWith("locked_"))held=new FileStream(f.PathOf("rows.json"),FileMode.Open,FileAccess.Read,scenario=="locked_exclusive"?FileShare.None:FileShare.Read);
                    try{act=Act(f,ui,op,target);}catch(Exception e){act=new(false,e.Message,Escaped:e.GetType().Name);n.Faults.Add((ui?"person":"AI")+" act threw "+e.GetType().Name+": "+e.Message);}
                }
                finally{held?.Dispose();}
                var after=f.Bytes();var afterHash=f.Disk();string text=ui?f.Text():"";
                if(ui&&act.Escaped is null)Need(n,f.Normal(text).Contains(f.Normal(f.Workspace!.Said)),"Native Files panel did not display its reported status.");
                if(!act.Ok)
                {
                    Need(n,afterHash==beforeHash,"Refusal/exception changed bytes or entries.");Need(n,!string.IsNullOrWhiteSpace(act.Said),"Refusal doesn't say why.");
                    if(draft is not null&&f.Project.Find("rows.json")?.Session is not null)Need(n,f.Draft()==draft,"Refusal changed unsaved draft/history.");
                }
                else if(op=="create")
                {
                    Need(n,after.Count==before.Count+1,"Create changed the wrong number of files.");foreach(var pair in before)Need(n,after.GetValueOrDefault(pair.Key)==pair.Value,"Create overwrote an existing file: "+pair.Key);
                    Need(n,f.Project.Find(target)?.Session is not null,"Create reports success without an open valid file.");
                }
                else if(op=="rename")
                {
                    var old="data/rows.json";var next="data/"+target;
                    Need(n,!after.ContainsKey(old)&&after.GetValueOrDefault(next)==before.GetValueOrDefault(old),"Rename didn't preserve the latest source bytes exactly.");
                    foreach(var pair in before.Where(x=>x.Key!=old))Need(n,after.GetValueOrDefault(pair.Key)==pair.Value,"Rename damaged another file.");Need(n,before.Count==after.Count,"Rename left partial/extra files.");
                }
                else if(op=="delete")
                {
                    Need(n,after.Count==before.Count-1&&!after.ContainsKey("data/rows.json"),"Delete didn't remove exactly the requested file.");foreach(var pair in before.Where(x=>x.Key!="data/rows.json"))Need(n,after.GetValueOrDefault(pair.Key)==pair.Value,"Delete damaged another file.");
                    var restored=f.Project.Restore("rows.json",out var restoreWhy);Need(n,restored,"Delete can't restore its exact backup: "+restoreWhy);if(restored)Need(n,f.Disk()==beforeHash,"Restore after delete didn't recover every original byte/entry.");
                }
                else if(op=="save")
                {
                    Need(n,before.Keys.SequenceEqual(after.Keys),"Save added/removed a project entry.");foreach(var pair in before.Where(x=>x.Key!="data/rows.json"))Need(n,after.GetValueOrDefault(pair.Key)==pair.Value,"Save damaged another file.");
                    var root=JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(after["data/rows.json"])))!;var expected=JsonNode.Parse(Original)!;expected["items"]![0]!["amount"]=9;
                    Need(n,JsonNode.DeepEquals(root,expected),"Save wrote an unrelated value.");
                }
                else if(op=="refresh")
                {
                    Need(n,afterHash==beforeHash,"Refresh wrote the disk.");Need(n,f.Draft()==draft&&f.Session.Dirty,"Refresh lost the draft or undo state.");Need(n,f.Session.Current.At("/items/0/amount")!.Value.GetInt32()==9,"Refresh lost the typed amount.");
                    if(scenario=="deleted_dirty")
                    {Need(n,f.File.State==DataFileState.Missing,"Deleted draft isn't marked missing.");Need(n,!f.File.Save(),"Saving a deleted draft silently recreated the file.");Need(n,!System.IO.File.Exists(f.PathOf("rows.json")),"Deleted outside file was recreated.");}
                }
                if(scenario=="changed" && op is "rename" or "delete")Need(n,act.Ok,"The owner-authorized operation on the latest source bytes was refused.");
                bool intervened=scenario is "changed" or "deleted" or "locked_exclusive" or "locked_readable" or "destination_appears" or "changed_dirty" or "deleted_dirty";
                if(op is "create" or "rename" or "delete" && !intervened)Need(n,preview.Ok==act.Ok,"Stable-disk preview and act disagree.");
                if(scenario is "project_readonly" or "folder_denied" || op=="save"&&scenario is "file_readonly" or "changed" or "deleted" or "locked_exclusive" or "locked_readable" || scenario=="dirty")Need(n,!act.Ok,"An operation that must refuse reported success.");
                if(scenario=="links"&&!ui)Need(n,preview.Json?["linksInto"]?.GetValue<int>()==2,"AI preview omits the two broken links.");
                answers.Add((act.Ok,f.Normal(act.Said)));
                routes.Add(new { route=ui?"person":"AI",preview,act,personSaid=f.Workspace?.Said,formText=form,filesText=text,beforePreview,beforeAct=before,afterAct=after,beforeHash,afterHash,externalActor=scenario,previewMatchedAct=preview.Ok==act.Ok,remainingDraft=f.Project.Find("rows.json")?.Session?.Current.Root.GetRawText(),dirty=f.Project.Find("rows.json")?.Dirty });
            }
            catch(Exception e){n.Faults.Add((ui?"person":"AI")+" harness/route: "+e);}
        }
        n.Values["routes"]=routes;
        if(answers.Count==2){Need(n,answers[0].Ok==answers[1].Ok,"Person and AI outcomes disagree.");if(!answers[0].Ok)Need(n,answers[0].Why==answers[1].Why,"Person and AI refusal reasons disagree.");}
        n.Phase="complete";Write(n);Assert.True(n.Faults.Count==0,string.Join("\n",n.Faults));
    }
    [Fact]
    public void Two_windows_keep_both_drafts_and_refuse_overwriting_the_first_save()
    {
        var n=new Note("two_windows");Write(n);
        try
        {
            using var a=new Fixture(true);var second=DataProject.Open(a.Data,a.Schemas);var b=new DataWorkspace(second);Assert.True(b.Show("rows.json"));a.Edit(10);Assert.True(second.Find("rows.json")!.Session!.Execute(new SetValue("/items/0/amount",J("20"))));
            var before=a.Bytes();var bDraft=second.Find("rows.json")!.Session!.Current.Root.GetRawText();var first=a.Workspace!.SaveAll();var firstText=a.Workspace.Said;var between=a.Bytes();var other=b.SaveAll();var otherText=b.Said;
            Need(n,first&&!other,"Two windows did not accept first save/refuse stale second save.");Need(n,a.Disk()==Hash(JsonSerializer.Serialize(between)),"Second save overwrote the first saved bytes.");Need(n,second.Find("rows.json")!.Session!.Current.Root.GetRawText()==bDraft&&second.Find("rows.json")!.Dirty,"Stale-save refusal lost the second draft.");Need(n,otherText.Contains("changed outside",StringComparison.OrdinalIgnoreCase),"Stale-save refusal does not explain outside change.");
            n.Values["before"]=before;n.Values["afterFirst"]=between;n.Values["afterSecond"]=a.Bytes();n.Values["firstPerson"]=firstText;n.Values["secondPerson"]=otherText;n.Values["filesText"]=a.Text();
            // The same second session's AI route must refuse for exactly that reason too.
            var ai=new AgentSession(second).Save();n.Values["secondAI"]=ai;n.Values["secondFilesText"]=a.Text(b);
            Need(n,ai["refused"] is JsonArray{Count:1} refused&&refused[0]?["why"]?.GetValue<string>()==otherText,"Second-window AI refusal differs from the person.");
            var expected=JsonNode.Parse(Original)!;expected["items"]![0]!["amount"]=10;
            Need(n,JsonNode.DeepEquals(expected,JsonNode.Parse(System.IO.File.ReadAllText(a.PathOf("rows.json")))),"First save didn't write exactly its intended amount10.");
            foreach(var pair in before.Where(x=>x.Key!="data/rows.json"))Need(n,between.GetValueOrDefault(pair.Key)==pair.Value,"First-window save changed an unrelated file.");
            Need(n,a.Disk()==Hash(JsonSerializer.Serialize(between)),"The second AI refusal changed saved bytes.");
        }
        catch(Exception e){n.Faults.Add(e.ToString());}
        n.Phase="complete";Write(n);Assert.True(n.Faults.Count==0,string.Join("\n",n.Faults));
    }
}
