using System.Text;
using System.Text.Json;
using Bed.Data;
using Xunit;
namespace Bed.Tests;

public sealed class DataSavePerf22Tests
{
    [Fact]
    public void Dirty_then_save_reuses_exact_large_text_without_serializing_it_again()
    {
        var root=Directory.CreateTempSubdirectory("own-save-perf-").FullName;
        try
        {
            var path=Path.Combine(root,"data.json");
            var text=JsonSerializer.Serialize(new{rows=Enumerable.Range(0,5000).Select(i=>new{id="row"+i,score=i,note="Unicode 水 and quote \" kept"})})+"\r\n";
            File.WriteAllText(path,text,new UTF8Encoding(false));
            var schemas=new JsonSchemas().Add("rows","""{"type":"object"}""");
            var session=DataKit.Open(path,schemas,"rows");Assert.True(session.Execute(new SetValue("/rows/2499/score",JsonSerializer.SerializeToElement(-1))));
            Assert.True(session.Dirty);var expected=session.TextToSave;
            var before=GC.GetAllocatedBytesForCurrentThread();
            for(var i=0;i<20;i++)Assert.Equal(expected,session.TextToSave);
            Assert.True(GC.GetAllocatedBytesForCurrentThread()-before<64_000,"Repeated save text should not rebuild the large document.");
            session.Save();Assert.Equal(expected,File.ReadAllText(path));Assert.False(session.Dirty);Assert.Equal(1,session.History.UndoCount);
            Assert.True(session.Undo());Assert.Equal(text,session.TextToSave);
            Assert.True(session.Redo());Assert.Equal(expected,session.TextToSave);
        }
        finally{Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);Directory.Delete(root,true);}
    }
    [Fact]
    public void Different_document_instances_and_source_spellings_do_not_share_cached_text()
    {
        var format=new DataFormat();
        const string first=" { \"n\": 1.0, \"caption\": \"水\" }\r\n";
        const string second="{\n  \"caption\": \"\\u6c34\",\n  \"n\": 1\n}\n";
        var a=format.Read(first);var b=format.Read(second);Assert.True(JsonSchemas.Same(a.Root,b.Root));
        Assert.Equal(first,format.Write(a));Assert.Equal(second,format.Write(b));Assert.Equal(first,format.Write(a));
        var changed=a with{Root=JsonSerializer.SerializeToElement(new{n=2,caption="水"})};
        var written=format.Write(changed);Assert.Contains("2",written);Assert.Equal(first,format.Write(a));Assert.Equal(written,format.Write(changed));
    }
    [Fact]
    public void A_shared_formatter_publishes_coherent_document_text_pairs()
    {
        var format=new DataFormat();var inputs=Enumerable.Range(0,16).Select(i=>(Text:"{\"id\":"+i+",\"name\":\"text \"}\n",Doc:(DataDocument)null!)).ToArray();
        for(var i=0;i<inputs.Length;i++)inputs[i]=(inputs[i].Text,format.Read(inputs[i].Text));
        Parallel.For(0,160,i=>{var input=inputs[i%inputs.Length];Assert.Equal(input.Text,format.Write(input.Doc));});
    }
}
