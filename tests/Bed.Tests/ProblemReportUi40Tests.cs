using Bed.Data;
using Bed.Editor.App;
using Bed.Editor;
using Bed.Editor.ImGui;
using Brutal.ImGuiApi;
using Xunit;
namespace Bed.Tests;
[Collection(ImGuiCollection.Name)]
public sealed class ProblemReportUi40Tests
{
    private static string Drive(string suffix)=>"D"+':'+Path.DirectorySeparatorChar+suffix;
    [Theory]
    [InlineData("user")][InlineData("quoted")][InlineData("unc")][InlineData("unix")][InlineData("forward")]
    public void Redaction_removes_absolute_paths_including_spaces_and_UNC(string shape)
    {
        // Construct deliberately private-looking fixture paths rather than embedding workstation paths in source.
        var user=Drive(@"Users\Private Person");
        var path=shape switch{"user"=>user+@"\secret project\level.json","quoted"=>"'"+Drive(@"Shared Projects\private file.txt")+"'","unc"=>@"\\server\private share\level.json","unix"=>string.Join('/',new[]{"","home","person","secret","level.json"}),_=>"C"+':'+"/private/folder/level.json"};
        var text=ProblemReport.Redact("Failed: "+path+"\nSafe next line",user:user);
        Assert.DoesNotContain("level.json",text);Assert.DoesNotContain("private file",text);Assert.DoesNotContain("secret",text);Assert.DoesNotContain("Private Person",text);Assert.EndsWith("Safe next line",text);
    }
    [Fact]
    public void Redaction_removes_project_name_and_folder_from_all_report_sections()
    {
        var root=Path.Combine(Path.GetTempPath(),"SecretProjectName");var report=ProblemReport.Format(new("Data",root),"SecretProjectName failed at "+root,"Open SecretProjectName",["Loaded "+root,"SecretProjectName failed"]);
        Assert.DoesNotContain("SecretProjectName",report);Assert.DoesNotContain(root,report);Assert.Contains("Project kind: Data",report);
    }
    [Fact]
    public void Log_tail_and_browser_URL_are_bounded_with_no_image_or_hidden_data()
    {
        var report=ProblemReport.Format(new(),log:Enumerable.Range(0,300).Select(i=>$"line {i:000} "+new string('x',2500)));
        Assert.DoesNotContain("line 099",report);Assert.Contains("line 100",report);Assert.Contains("line 299",report);
        var url=ProblemReport.IssueUrl(ProblemReport.DefaultRepository,report+new string('界',10000));Assert.InRange(url.Length,1,ProblemReport.MaxUrlLength);Assert.Contains("/issues/new?title=",url);Assert.Contains("Report shortened",Uri.UnescapeDataString(url));Assert.DoesNotContain(".png",url);
        Assert.Throws<ArgumentException>(()=>ProblemReport.IssueUrl("file:///private",report));Assert.Throws<ArgumentException>(()=>ProblemReport.IssueUrl("https://user:password@example.com/repo",report));
    }
    [Fact]
    public void BED_log_rotates_at_one_MiB_keeps_two_files_and_records_no_payload()
    {
        var root=Directory.CreateTempSubdirectory("report-log-").FullName;
        try
        {
            using var scope=ProblemReportLog.Configure(root);var logFolder=Directory.CreateDirectory(Path.Combine(root,"logs")).FullName;var file=Path.Combine(logFolder,"bed.log");
            File.WriteAllBytes(file,new byte[ProblemReportLog.MaxBytes-10]);ProblemReportLog.Record(ReportEvent.Started);
            Assert.True(new FileInfo(file).Length<=ProblemReportLog.MaxBytes);Assert.Equal(ProblemReportLog.MaxBytes-10,new FileInfo(file+".1").Length);
            File.WriteAllBytes(file,new byte[ProblemReportLog.MaxBytes-10]);ProblemReportLog.Record(ReportEvent.PanelFailed);Assert.Equal(2,Directory.GetFiles(logFolder).Length);
            File.Delete(file+".1");for(var i=0;i<230;i++)ProblemReportLog.Record(ReportEvent.OperationAccepted);
            var lines=ProblemReportLog.Read(root);Assert.Equal(200,lines.Length);Assert.All(lines,line=>{Assert.Contains(" INFO [editor] OperationAccepted",line);Assert.True(DateTime.TryParse(line.Split(' ')[0],out _));});
        }
        finally{Directory.Delete(root,true);}
    }
    [Theory][InlineData("{\"reportRepositoryUrl\":123}")][InlineData("{\"reportRepositoryUrl\":\"file:///private\"}")][InlineData("invalid")]
    public void Malformed_repository_settings_fall_back_with_a_plain_note(string json)
    {
        var root=Directory.CreateTempSubdirectory("report-setting-").FullName;
        try{File.WriteAllText(Path.Combine(root,"settings.json"),json);var setting=ProblemReportSettings.Read(root);Assert.Equal(ProblemReport.DefaultRepository,setting.Repository);Assert.Contains("public BED repository",setting.Note);}
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void Preview_and_CLI_use_the_same_formatter_without_writing_or_opening_anything()
    {
        var root=Directory.CreateTempSubdirectory("report-cli-").FullName;using var scope=ProblemReportLog.Configure(root);
        try{var text=new StringWriter();var errors=new StringWriter();Assert.Equal(0,Bed.Agent.Cli.Program.Run(["report"],new StringReader(""),text,errors));Assert.Equal(ProblemReport.Format(new(),log:ProblemReportLog.Read()),text.ToString());Assert.Equal("",errors.ToString());Assert.Equal(2,Bed.Agent.Cli.Program.Run(["report","--save"],new StringReader(""),text,errors));}finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void Named_controls_require_explicit_clicks_for_copy_save_picture_and_browser()
    {
        var root=Directory.CreateTempSubdirectory("report-ui-").FullName;
        try
        {
            using var host=new HeadlessHost(1200,900){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());ImGui.GetStyle().FontSizeBase=16;var copies=new List<string>();var urls=new List<string>();var captures=0;var report=new ProblemReportDialog{Context=()=>new("Data",Path.Combine(root,"PrivateProject"),root),Copy=copies.Add,Browse=urls.Add,Capture=()=>{captures++;return [137,80,78,71];}};report.Open();host.Frames(report.Draw,4);
            UiNode Node(string label)=>Assert.Single(Ui.Snapshot().Nodes,n=>n.Label==label&&n.Role is UiRole.Button or UiRole.TextField or UiRole.Checkbox);
            void Act(string label,UiActions action,string? value=null){var ticket=Ui.RequestAction(Node(label).Id,action,value);host.Frames(report.Draw,3);Assert.Equal(UiActionOutcome.Applied,ticket.Outcome);}
            Assert.Empty(copies);Assert.Empty(urls);Assert.Equal(0,captures);Assert.Empty(Directory.GetFiles(root));
            foreach(var name in new[]{"What happened","What you expected","Copy report","Save report","Open the issue page","Include current-window picture"})Assert.True(Node(name).Enabled);
            Act("What happened",UiActions.SetValue,"PrivateProject failed at "+Drive(@"PrivateFolder\level.json"));Act("What you expected",UiActions.SetValue,"The level should open.");Assert.DoesNotContain("PrivateProject",report.Preview);Assert.DoesNotContain("PrivateFolder",report.Preview);
            Act("Copy report",UiActions.Invoke);Assert.Equal(report.Preview,Assert.Single(copies));Assert.Empty(urls);Assert.Equal(0,captures);Assert.Empty(Directory.GetFiles(root));
            Act("Open the issue page",UiActions.Invoke);var url=Assert.Single(urls);Assert.DoesNotContain("PrivateFolder",url);Assert.DoesNotContain("PrivateProject",url);Assert.Equal(0,captures);Assert.Empty(Directory.GetFiles(root));
            Act("Save folder",UiActions.SetValue,root);Act("Save report",UiActions.Invoke);var md=Assert.Single(Directory.GetFiles(root));Assert.Equal(report.Preview,File.ReadAllText(md));Assert.EndsWith(".md",md);Assert.Equal(0,captures);
            Act("Include current-window picture",UiActions.Toggle);Assert.Equal(1,captures);Assert.Single(Directory.GetFiles(root));Act("Save report",UiActions.Invoke);Assert.Equal(3,Directory.GetFiles(root).Length);Assert.Equal(new byte[]{137,80,78,71},File.ReadAllBytes(Assert.Single(Directory.GetFiles(root,"*.png"))));
            host.Drawing=true;host.Frame(report.Draw);var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Bed.slnx")))repo=repo.Parent;var captureFolder=Path.Combine(repo!.FullName,".validation/UI40");Directory.CreateDirectory(captureFolder);host.Save(Path.Combine(captureFolder,"report-dialog-headless.png"));
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    [Theory][InlineData(0u)][InlineData(71u)]
    public void Picture_masks_private_controls_in_main_and_secondary_viewports_with_actual_origin_and_scale(uint viewport)
    {
        var root=Path.Combine(Path.GetTempPath(),"PrivateProject");var tree=new UiTree();tree.Begin();
        tree.Record(new(1,UiRole.TextField,"Save folder",new(110,220,20,10),true,null,false,false,false,null,null,Drive(@"private\folder"),-1,UiActions.None,ViewportId:viewport));
        tree.Record(new(2,UiRole.Text,"PrivateProject",new(140,220,10,10),true,null,false,false,false,null,null,null,-1,UiActions.None,ViewportId:viewport));
        tree.Record(new(3,UiRole.Text,"Safe",new(100,200,10,10),true,null,false,false,false,null,null,null,-1,UiActions.None,ViewportId:viewport));tree.Complete();
        var pixels=Enumerable.Repeat((byte)200,200*100*4).ToArray();Assert.True(WindowsReportPicture.RedactPixels(pixels,200,100,tree.Snapshot(),viewport,new(100,200,100,50),new("Data",root)));
        Assert.Equal(32,pixels[(45*200+25)*4]);Assert.Equal(32,pixels[(45*200+85)*4]);Assert.Equal(200,pixels[(5*200+5)*4]);Assert.Equal(255,pixels[(45*200+25)*4+3]);
        Assert.False(WindowsReportPicture.RedactPixels(pixels,200,100,tree.Snapshot(),viewport,new(100,200,0,50),new("Data",root)));
    }
    [Theory][InlineData(1f)][InlineData(2f)]
    public void Narrow_inspector_wraps_its_heading_and_empty_message_at_the_larger_default(float zoom)
    {
        using var host=new HeadlessHost(1280,900){Drawing=false};EditorWindow<DataDocument>.Configure(ImGui.GetIO());ImGui.GetStyle().FontSizeBase=16;ImGui.GetStyle().FontScaleMain=zoom;
        var schemas=new JsonSchemas().Add("data","{}");var session=DataKit.Session(new DataFormat().Read("{}"),schemas,"data");var editor=new EditorModel<DataDocument>(session,DataEditing.Outline);var window=new EditorWindow<DataDocument>(editor){Searcher=(_,_) => ([],false)};
        host.Frames(window.DrawDocked,4);var nodes=Ui.Snapshot().Nodes;var inspector=Assert.Single(nodes,n=>n.Role==UiRole.Window&&n.Label=="Inspector");var input=Assert.Single(nodes,n=>n.Role==UiRole.TextField&&n.Label=="Find in this file");var message=Assert.Single(nodes,n=>n.Label=="Select an Outliner item to see its properties.");
        Assert.InRange(input.Bounds.X,inspector.Bounds.X,inspector.Bounds.X+inspector.Bounds.Width-input.Bounds.Width);Assert.True(message.Bounds.Width<=inspector.Bounds.Width);Assert.True(message.Bounds.Height>ImGui.GetTextLineHeight());
    }
    [Fact]
    public void Keyboard_Help_menu_opens_named_report_without_changing_the_document()
    {
        using var host=new HeadlessHost(1280,900){Drawing=false};EditorWindow<Scene>.Configure(ImGui.GetIO());var editor=new EditorModel<Scene>(new DocumentSession<Scene>(new SceneFormat(),new SceneValidator(),SceneKit.Catalog(),Scene.Empty),SceneEditing.Outline);var window=new EditorWindow<Scene>(editor);host.Frames(window.DrawDocked,4);host.Press(window.DrawDocked,ImGuiKey.H,alt:true);host.Frames(window.DrawDocked,2);var item=Assert.Single(Ui.Snapshot().Nodes,n=>n.Role==UiRole.MenuItem&&n.Label=="Report a problem");Assert.True(item.Enabled);var action=Ui.RequestAction(item.Id,UiActions.Invoke);host.Frames(window.DrawDocked,3);Assert.Equal(UiActionOutcome.Applied,action.Outcome);Assert.Contains(Ui.Snapshot().Nodes,n=>n.Role==UiRole.Window&&n.Label=="Report a problem");Assert.False(editor.Session.Dirty);Assert.Empty(editor.Session.Journal);
    }
}
