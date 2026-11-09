using System.Text.Json;
using System.Text;

// Self-authored headless protocol fixture, never a real game or generic external-command runner.
using var stdin=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false,true));
var mode=args.FirstOrDefault() ?? "ok";
var start=await stdin.ReadLineAsync();
if(start is null) return 20;
using var input=JsonDocument.Parse(start);
var root=input.RootElement;
var version=root.GetProperty("version"u8).GetInt32();
var session=root.GetProperty("session"u8).GetString();
var revision=root.GetProperty("revision"u8).GetInt64();
var hash=root.GetProperty("hash"u8).GetString();
var payload=Convert.FromBase64String(root.GetProperty("payload"u8).GetString()!);
if(mode=="crash") return 23;
if(mode=="hang") { await Task.Delay(Timeout.InfiniteTimeSpan);return 24; }
if(mode=="start-fail")
{
    Console.WriteLine(JsonSerializer.Serialize(new {kind="failure",version,session,revision,hash,code="LoadFailed",message="Invented helper start failure"}));
    return 7;
}
if(payload.Length==0) return 21;
if(mode=="stale")
    Console.WriteLine(JsonSerializer.Serialize(new {kind="ready",version=99,session=Guid.NewGuid().ToString("N"),revision,hash,supported=7}));
Console.WriteLine(JsonSerializer.Serialize(new {kind="ready",version,session,revision,hash,supported=7}));
Console.WriteLine(JsonSerializer.Serialize(new {kind="log",version,session,revision,hash,severity="Info",message=JsonSerializer.Serialize(args)}));
if(mode=="crash-after-ready") return 23;
var stop=await stdin.ReadLineAsync();
if(stop is null) return 22;
using var stopping=JsonDocument.Parse(stop);
if(stopping.RootElement.GetProperty("kind"u8).GetString()!="stop") return 25;
if(mode=="detect-duplicate-stop")
{
    var another=stdin.ReadLineAsync();
    if(await Task.WhenAny(another,Task.Delay(150))==another && await another is not null)
    {
        Console.WriteLine(JsonSerializer.Serialize(new {kind="failure",version,session,revision,hash,code="RuntimeFailed",message="Invented duplicate stop detected"}));
        return 26;
    }
}
Console.WriteLine(JsonSerializer.Serialize(new {kind="stopped",version,session,revision,hash}));
return 0;
