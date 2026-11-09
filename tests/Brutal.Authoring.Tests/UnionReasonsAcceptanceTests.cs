using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Brutal.Authoring.Data;
using Xunit;
using Xunit.Abstractions;

namespace Brutal.Authoring.Tests;

// INVENTED counterexamples; BORROWED: our accepted A135's 27 self-authored fixtures
// (18 shape groups plus nine controls, not 27 plus another nine) and A132's bounded
// child-process test pattern. No game schemas/records, windows or new dependencies.
public sealed class UnionReasonsAcceptanceTests(ITestOutputHelper output)
{
    private static List<Issue> CheckRoot(string schema, string value) =>
        new JsonSchemas().Add("doc", schema).Validate(new DataFormat().Read(value).Root, "doc").ToList();
    private static string Root(string field, string defs = "{}") =>
        "{\"type\":\"object\",\"$defs\":" + defs + ",\"properties\":{\"v\":" + field + "}}";
    private static List<Issue> Check(string field, string value, string defs = "{}") => CheckRoot(Root(field, defs), "{\"v\":" + value + "}");
    private static Issue Only(string field, string value, string defs = "{}") => Assert.Single(Check(field, value, defs));
    private static void Good(string field, string value, string defs = "{}") => Assert.Empty(Check(field, value, defs));
    private static void Bad(string field, string value, string defs = "{}") => Assert.NotEmpty(Check(field, value, defs));
    private static void Bounded(Issue issue)
    {
        Assert.InRange(issue.Message.Length, 1, 96);
        Assert.DoesNotContain('\r', issue.Message); Assert.DoesNotContain('\n', issue.Message);
        for (int i = 0; i < issue.Message.Length; i++)
        {
            if (char.IsHighSurrogate(issue.Message[i])) Assert.True(i + 1 < issue.Message.Length && char.IsLowSurrogate(issue.Message[i + 1]), "The reason split a Unicode character.");
            if (char.IsLowSurrogate(issue.Message[i])) Assert.True(i > 0 && char.IsHighSurrogate(issue.Message[i - 1]), "The reason has a detached low surrogate.");
        }
    }

    public static IEnumerable<object[]> A135Examples()
    {
        using var doc = JsonDocument.Parse(ExampleJson);
        foreach (var c in doc.RootElement.EnumerateArray())
            yield return new object[] { c.GetProperty("id").GetString()!, c.GetProperty("schema").GetRawText(), c.GetProperty("bad").GetRawText(), c.GetProperty("repair").GetRawText(), c.TryGetProperty("repairSchema", out var fixedRule) ? fixedRule.GetRawText() : c.GetProperty("schema").GetRawText() };
    }

    [Theory]
    [MemberData(nameof(A135Examples))]
    public void Each_of_the_27_starting_examples_still_refuses_with_a_short_reason_and_accepts_its_repair(string id, string schema, string bad, string repair, string repairSchema)
    {
        var issues = CheckRoot(schema, bad); Assert.NotEmpty(issues); Assert.All(issues, Bounded);
        Assert.Empty(CheckRoot(repairSchema, repair));
        output.WriteLine(id + ": " + string.Join(" | ", issues.Select(x => x.Path + ": " + x.Message)));
    }

    [Fact]
    public void A_finite_choice_reason_must_not_offer_a_value_below_the_outer_minimum()
    {
        const string s = """{"minimum":10,"anyOf":[{"enum":[0,20]},{"const":30}]}""";
        Good(s, "20"); Good(s, "30"); Bad(s, "0");
        var reason = Check(s, "5").Last(); Bounded(reason);
        Assert.DoesNotMatch(@"(?<!\d)0(?!\d)", reason.Message);
        Assert.Contains("20", reason.Message); Assert.Contains("30", reason.Message);
    }

    [Fact]
    public void A_oneOf_choice_reason_must_not_offer_a_value_accepted_by_two_alternatives()
    {
        const string s = """{"oneOf":[{"enum":[0,90]},{"enum":[90,180]}]}""";
        Good(s, "0"); Good(s, "180"); Bad(s, "90");
        var reason = Only(s, "45"); Bounded(reason);
        Assert.DoesNotMatch(@"(?<!\d)90(?!\d)", reason.Message);
    }

    [Fact]
    public void A_referenced_choice_reason_keeps_the_reference_siblings_limit()
    {
        const string defs = """{"options":{"enum":[0,20]}}""";
        const string s = """{"anyOf":[{"$ref":"#/$defs/options","minimum":10},{"const":30}]}""";
        Good(s, "20", defs); Good(s, "30", defs); Bad(s, "0", defs);
        var reason = Only(s, "5", defs); Bounded(reason);
        Assert.DoesNotMatch(@"(?<!\d)0(?!\d)", reason.Message);
    }

    [Fact]
    public void An_outer_enum_is_not_contradicted_by_the_union_reason()
    {
        const string s = """{"enum":["amber"],"anyOf":[{"enum":["amber","blue"]},{"const":"green"}]}""";
        Good(s, "\"amber\""); Bad(s, "\"blue\""); Bad(s, "\"green\"");
        var reason = Check(s, "\"other\"").Last();
        Assert.DoesNotContain("'blue'", reason.Message); Assert.DoesNotContain("'green'", reason.Message);
    }

    [Theory]
    [InlineData("{\"const\":null}")]
    [InlineData("{\"enum\":[null]}")]
    public void A_legal_null_alternative_is_not_described_as_forbidden(string noValue)
    {
        var s = "{\"anyOf\":[{\"const\":\"alpha\"}," + noValue + "]}";
        Good(s, "null"); Good(s, "\"alpha\"");
        var reason = Only(s, "false"); Bounded(reason);
        Assert.True(reason.Message.Contains("nothing", StringComparison.OrdinalIgnoreCase) || reason.Message.Contains("null", StringComparison.OrdinalIgnoreCase), reason.Message);
    }

    [Fact]
    public void A_null_only_union_does_not_tell_the_user_to_enter_something_else()
    {
        const string s = """{"anyOf":[{"const":null},{"enum":[null]}]}""";
        Good(s, "null"); var reason = Only(s, "false");
        Assert.DoesNotContain("something else", reason.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(reason.Message.Contains("nothing", StringComparison.OrdinalIgnoreCase) || reason.Message.Contains("null", StringComparison.OrdinalIgnoreCase), reason.Message);
    }

    [Fact]
    public void Numerically_equal_selector_values_do_not_pick_a_form_by_raw_spelling()
    {
        const string a = """{"type":"object","required":["kind","n"],"properties":{"kind":{"const":1},"n":{"type":"number","minimum":10}}}""";
        const string b = """{"type":"object","required":["kind","m"],"properties":{"kind":{"const":1.0},"m":{"type":"number","minimum":0}}}""";
        var s = "{\"oneOf\":[" + a + "," + b + "]}";
        var swapped = "{\"oneOf\":[" + b + "," + a + "]}";
        const string value = """{"kind":1,"n":0,"m":-1}""";
        Good(s, """{"kind":1,"n":0,"m":0}""");
        var first = Only(s, value); var second = Only(swapped, value);
        Assert.Equal(first.Path, second.Path); Assert.Equal(first.Message, second.Message);
    }

    [Fact]
    public void Object_selectors_equal_despite_key_order_do_not_pick_a_form()
    {
        const string a = """{"type":"object","required":["kind","n"],"properties":{"kind":{"const":{"x":1,"y":2}},"n":{"minimum":10}}}""";
        const string b = """{"type":"object","required":["kind","m"],"properties":{"kind":{"const":{"y":2,"x":1}},"m":{"minimum":0}}}""";
        var s = "{\"oneOf\":[" + a + "," + b + "]}";var swapped = "{\"oneOf\":[" + b + "," + a + "]}";
        const string value = """{"kind":{"x":1,"y":2},"n":0,"m":-1}""";
        Good(s, """{"kind":{"x":1,"y":2},"n":0,"m":0}""");
        Assert.Equal(Only(s, value).Path, Only(swapped, value).Path);
        Assert.Equal(Only(s, value).Message, Only(swapped, value).Message);
    }

    [Fact]
    public void An_unknown_selector_with_a_dot_in_its_key_keeps_a_navigable_path()
    {
        const string s = """{"oneOf":[{"type":"object","required":["a.b"],"properties":{"a.b":{"const":"A"}}},{"type":"object","required":["a.b"],"properties":{"a.b":{"const":"B"}}}]}""";
        Good(s, """{"a.b":"A"}""");
        var reason = Only(s, """{"a.b":"C"}""");
        Assert.Equal("/v/a.b", Pointer.FromPath(reason.Path!));
    }

    [Fact]
    public void The_65th_legitimate_form_is_not_reported_as_an_unknown_selector()
    {
        var forms = Enumerable.Range(0,65).Select(i => new {type="object",required=new[]{"kind","n"},properties=new {kind=new { @const="p"+i },n=new {type="integer",minimum=1}}});
        var s = JsonSerializer.Serialize(new {oneOf=forms});
        Good(s, """{"kind":"p64","n":1}""");
        var reason = Only(s, """{"kind":"p64","n":0}"""); Bounded(reason);
        Assert.DoesNotContain("kind must", reason.Message,StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("$.v.kind",reason.Path);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("newline")]
    public void Rule_fault_reasons_also_obey_the_short_one_line_contract(string fault)
    {
        var broken=fault=="missing" ? JsonSerializer.Serialize(new Dictionary<string,object>{{"$ref","#/$defs/"+new string('x',200)}})
            : JsonSerializer.Serialize(new Dictionary<string,object>{{fault=="newline" ? "future\nflag" : "future_"+new string('x',200),true}});
        var s="{\"anyOf\":["+broken+",{\"type\":\"string\"}]}";
        var reason=Only(s,"1");Assert.Contains("rules",reason.Message,StringComparison.OrdinalIgnoreCase);Bounded(reason);
    }

    [Theory]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    [InlineData("\u0000")]
    public void A_listed_value_cannot_put_hidden_line_or_control_characters_in_the_status(string separator)
    {
        var s=JsonSerializer.Serialize(new {anyOf=new object[]{new { @const="first"+separator+"second"},new { @const="none"}}});
        var reason=Only(s,"\"other\"");Bounded(reason);
        Assert.DoesNotContain(separator,reason.Message,StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_string_choice_is_not_truncated_into_an_unclosed_quoted_value()
    {
        var value=new string('x',180);var s=JsonSerializer.Serialize(new {anyOf=new[]{new { @const=value},new { @const="none"}}});
        Good(s,JsonSerializer.Serialize(value));var reason=Only(s,"\"other\"");Bounded(reason);
        Assert.Equal(0,reason.Message.Count(c=>c=='\'')%2);
    }

    [Fact]
    public void A_long_number_is_not_truncated_into_a_different_suggested_number()
    {
        var value="1"+new string('2',110);var s="{\"anyOf\":[{\"const\":"+value+"},{\"const\":\"none\"}]}";
        Good(s,value);var reason=Only(s,"0");Bounded(reason);
        Assert.DoesNotContain(value[..30],reason.Message);
    }

    [Fact]
    public void Truncation_keeps_supplementary_unicode_characters_well_formed()
    {
        var s=JsonSerializer.Serialize(new {anyOf=new[]{new {@const=string.Concat(Enumerable.Repeat("🛰",80))},new {@const="none"}}});
        Bounded(Only(s,"\"other\""));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unreadable_pattern_is_a_rule_fault_even_with_another_plausible_form(bool propertyPattern)
    {
        var a=propertyPattern ? """{"type":"object","patternProperties":{"[":{"type":"number"}}}""" : """{"type":"string","pattern":"["}""";
        var b=propertyPattern ? """{"type":"object","maxProperties":0}""" : """{"type":"string","maxLength":0}""";
        var s="{\"anyOf\":["+a+","+b+"]}";var value=propertyPattern ? "{\"x\":1}" : "\"x\"";
        var reason=Only(s,value);
        Assert.True(reason.Message.Contains("rules",StringComparison.OrdinalIgnoreCase) || reason.Message.Contains("can't be read",StringComparison.OrdinalIgnoreCase),reason.Message);
    }

    [Fact]
    public void A_known_selector_follows_its_own_form_even_if_another_has_fewer_errors()
    {
        const string a="""{"type":"object","required":["kind","count","range","time"],"properties":{"kind":{"const":"A"}}}""";
        const string b="""{"type":"object","required":["kind"],"properties":{"kind":{"const":"B"}}}""";
        const string value="""{"kind":"A"}""";
        var first=Only("{\"oneOf\":["+a+","+b+"]}",value);var swapped=Only("{\"oneOf\":["+b+","+a+"]}",value);
        Assert.Contains("count",first.Message);Assert.Equal(first.Message,swapped.Message);Assert.Equal(first.Path,swapped.Path);
    }

    [Fact]
    public void A_union_edit_refusal_keeps_the_document_history_and_saved_text()
    {
        var schemas=new JsonSchemas().Add("doc",Root("""{"anyOf":[{"enum":["alpha","beta"]},{"type":"null"}]}"""));
        var session=DataKit.Session(new DataFormat().Read("""{"v":"alpha","untouched":{"safe":[1,null,true]}}"""),schemas,"doc");
        var before=session.Current;var text=session.TextToSave;
        Assert.False(session.Execute(new SetValue("/v",JsonSerializer.SerializeToElement("gamma"))));
        Assert.Same(before,session.Current);Assert.Empty(session.Journal);Assert.Equal(text,session.TextToSave);Assert.False(session.Dirty);
        Assert.True(session.Execute(new SetValue("/v",JsonSerializer.SerializeToElement("beta"))));
        Assert.Single(session.Journal);Assert.True(session.Undo());Assert.Same(before,session.Current);Assert.True(session.Redo());
        Assert.Equal("beta",session.Current.At("/v")!.Value.GetString());Assert.Equal(before.At("/untouched")!.Value.GetRawText(),session.Current.At("/untouched")!.Value.GetRawText());
    }

    private static string Wrapped(int depth)
    {
        var s="{\"const\":0}";
        for(int i=0;i<depth;i++)s="{\"anyOf\":["+s+"]}";
        return s;
    }
    [Fact]
    public void A_valid_deep_value_does_not_need_to_build_failure_explanations()=>Good(Wrapped(28),"0");

    [Fact]
    public void A_good_first_alternative_is_not_spoiled_by_an_unused_broken_rule()=>
        Good("""{"anyOf":[{"const":"good"},{"$ref":"#/$defs/missing"}]}""","\"good\"");

    [Fact]
    public async Task A_deep_but_finite_invalid_union_has_a_bounded_explanation()=>
        await Isolated(nameof(A_deep_but_finite_invalid_union_has_a_bounded_explanation),()=>
        {
            var clock=Stopwatch.StartNew();var reason=Only(Wrapped(28),"1");clock.Stop();Bounded(reason);
            output.WriteLine("Invalid wrappers28:"+clock.Elapsed.TotalMilliseconds+"ms");
            Assert.True(clock.Elapsed<TimeSpan.FromSeconds(5),"Twenty-eight finite wrappers exceeded the five-second diagnostic budget.");
        });

    [Fact]
    public async Task A_recursive_rule_is_refused_as_a_fault_without_crashing_the_validator()=>
        await Isolated(nameof(A_recursive_rule_is_refused_as_a_fault_without_crashing_the_validator),()=>
        {
            const string defs="""{"loop":{"anyOf":[{"$ref":"#/$defs/loop"},{"const":0}]}}""";
            var reason=Only("""{"$ref":"#/$defs/loop"}""","1",defs);
            Assert.Contains("rules",reason.Message,StringComparison.OrdinalIgnoreCase);Bounded(reason);
        });

    [Fact]
    public async Task A_large_finite_list_does_not_make_the_refusal_unbounded()=>
        await Isolated(nameof(A_large_finite_list_does_not_make_the_refusal_unbounded),()=>
        {
            var s=JsonSerializer.Serialize(new {anyOf=Enumerable.Range(0,15000).Select(i=>new {@const=i})});
            var clock=Stopwatch.StartNew();var reason=Only(s,"-1");clock.Stop();Bounded(reason);
            output.WriteLine("Choices15000:"+clock.Elapsed.TotalMilliseconds+"ms");
            Assert.True(clock.Elapsed<TimeSpan.FromSeconds(10),"Fifteen thousand finite alternatives exceeded ten seconds.");
        });

    private async Task Isolated(string method,Action probe)
    {
        const string marker="BRUTAL_UNION_REASON_CHILD";
        if(Environment.GetEnvironmentVariable(marker)==method){probe();return;}
        var project=new DirectoryInfo(AppContext.BaseDirectory);
        while(project is not null&&!File.Exists(Path.Combine(project.FullName,"Brutal.Authoring.Tests.csproj")))project=project.Parent;
        Assert.NotNull(project);
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,ErrorDialog=false,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=project.FullName};
        // The child runs the same build as this one: a Release parent never silently tests a stale Debug binary (found by A224).
        var configuration=typeof(UnionReasonsAcceptanceTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false).OfType<System.Reflection.AssemblyConfigurationAttribute>().FirstOrDefault()?.Configuration;
        Assert.False(string.IsNullOrEmpty(configuration),"The test assembly doesn't say its build configuration.");
        var results=Directory.CreateTempSubdirectory("union-reason-probe-").FullName;
        try
        {
            // Run this exact loaded binary in a separate testhost; project/MSBuild startup adds no proof.
            // Keep the same filter, blame timeout, outer deadline and child-exit assertions; never build or restore.
            foreach(var a in new[]{"vstest",typeof(UnionReasonsAcceptanceTests).Assembly.Location,
                "/Blame:CollectHangDump;TestTimeout=10m","/TestCaseFilter:FullyQualifiedName~"+method,
                "/Logger:console;verbosity=minimal","/Logger:trx;LogFileName=probe.trx","/ResultsDirectory:"+results})start.ArgumentList.Add(a);
            start.Environment[marker]=method;
            using var process=Process.Start(start)!;
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();var exited=process.WaitForExitAsync();
            var finished=await Task.WhenAny(exited,Task.Delay(30000))==exited;
            if(!finished)process.Kill(entireProcessTree:true);
            await exited;
            var detail=(await stdout)+(await stderr);output.WriteLine(detail.Length>6000?detail[..2000]+Environment.NewLine+"[middle omitted]"+Environment.NewLine+detail[^4000..]:detail);
            Assert.True(finished,"The isolated diagnostic exceeded its thirty-second deadline.");
            Assert.Equal(0,process.ExitCode);
            // Exit zero alone can mean no matching tests: require this one actual probe to have passed.
            var receipt=XDocument.Load(Path.Combine(results,"probe.trx"));var ns=receipt.Root!.Name.Namespace;
            var result=Assert.Single(receipt.Descendants(ns+"UnitTestResult"));Assert.Equal("Passed",result.Attribute("outcome")?.Value);
            var definition=Assert.Single(receipt.Descendants(ns+"UnitTest"),test=>test.Attribute("id")?.Value==result.Attribute("testId")?.Value);
            var target=definition.Element(ns+"TestMethod")!;
            Assert.Equal(typeof(UnionReasonsAcceptanceTests).FullName,target.Attribute("className")?.Value);
            Assert.Equal(method,target.Attribute("name")?.Value);
        }
        finally
        {
            var owned=Path.GetFullPath(results);var temporary=Path.GetFullPath(Path.GetTempPath());
            Assert.StartsWith(temporary,owned,StringComparison.OrdinalIgnoreCase);Directory.Delete(owned,true);
        }
    }
    private const string ExampleJson = """
[
  {
    "id": "U02",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "type": "array",
              "items": {
                "type": "number",
                "minimum": 0
              }
            },
            {
              "type": "null"
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": {
        "item": 1
      }
    },
    "repair": {
      "value": []
    }
  },
  {
    "id": "U03",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "type": "array",
              "items": {
                "type": "number",
                "minimum": 0
              }
            },
            {
              "type": "number",
              "minimum": 0
            },
            {
              "type": "object",
              "required": [
                "limit"
              ],
              "properties": {
                "limit": {
                  "type": "number",
                  "minimum": 0
                }
              },
              "additionalProperties": false
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": [
        -1
      ]
    },
    "repair": {
      "value": [
        0
      ]
    }
  },
  {
    "id": "U04",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "type": "array",
              "items": {
                "type": "number",
                "minimum": 0
              }
            },
            {
              "type": "object",
              "required": [
                "limit"
              ],
              "properties": {
                "limit": {
                  "type": "number",
                  "minimum": 0
                }
              },
              "additionalProperties": false
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": [
        "bad"
      ]
    },
    "repair": {
      "value": [
        1
      ]
    }
  },
  {
    "id": "U05",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "type": "boolean"
            },
            {
              "type": "number"
            },
            {
              "type": "object",
              "required": [
                "limit"
              ],
              "properties": {
                "limit": {
                  "type": "number",
                  "minimum": 0
                }
              },
              "additionalProperties": false
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": "maybe"
    },
    "repair": {
      "value": true
    }
  },
  {
    "id": "U06",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "type": "boolean"
            },
            {
              "type": "string",
              "minLength": 1
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": 7
    },
    "repair": {
      "value": true
    }
  },
  {
    "id": "U07",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "type": "null"
            },
            {
              "type": "number"
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": "none"
    },
    "repair": {
      "value": null
    }
  },
  {
    "id": "U08",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "type": "null"
            },
            {
              "type": "string",
              "minLength": 1
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": false
    },
    "repair": {
      "value": "note"
    }
  },
  {
    "id": "U09",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "type": "number"
            },
            {
              "type": "object",
              "required": [
                "limit"
              ],
              "properties": {
                "limit": {
                  "type": "number",
                  "minimum": 0
                }
              },
              "additionalProperties": false
            },
            {
              "type": "string",
              "minLength": 1
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": {
        "limit": -1
      }
    },
    "repair": {
      "value": {
        "limit": 1
      }
    }
  },
  {
    "id": "U10",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "type": "number",
              "minimum": 0
            },
            {
              "type": "string",
              "minLength": 1
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": -1
    },
    "repair": {
      "value": 0
    }
  },
  {
    "id": "U11",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "type": "object",
              "required": [
                "limit"
              ],
              "properties": {
                "limit": {
                  "type": "number",
                  "minimum": 0
                }
              },
              "additionalProperties": false
            },
            {
              "type": "string",
              "minLength": 1
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": {
        "limit": -1
      }
    },
    "repair": {
      "value": {
        "limit": 1
      }
    }
  },
  {
    "id": "U17",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "oneOf": [
            {
              "type": "object",
              "required": [
                "text"
              ],
              "properties": {
                "text": {
                  "type": "string",
                  "minLength": 1
                }
              },
              "additionalProperties": false
            },
            {
              "type": "string",
              "minLength": 1
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": {
        "text": ""
      }
    },
    "repair": {
      "value": {
        "text": "Proceed"
      }
    }
  },
  {
    "id": "U15",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "oneOf": [
            {
              "const": "all"
            },
            {
              "type": "array",
              "minItems": 1,
              "uniqueItems": true,
              "items": {
                "enum": [
                  "small",
                  "large"
                ]
              }
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": []
    },
    "repair": {
      "value": [
        "small"
      ]
    }
  },
  {
    "id": "U18",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "type": "object",
          "properties": {
            "item": {
              "type": "string",
              "minLength": 1
            },
            "group": {
              "type": "string",
              "minLength": 1
            }
          },
          "additionalProperties": false,
          "oneOf": [
            {
              "required": [
                "item"
              ]
            },
            {
              "required": [
                "group"
              ]
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": {}
    },
    "repair": {
      "value": {
        "item": "one"
      }
    }
  },
  {
    "id": "U12",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "type": "object",
          "not": {
            "anyOf": [
              {
                "required": [
                  "jet"
                ]
              },
              {
                "required": [
                  "radius"
                ]
              },
              {
                "required": [
                  "glow"
                ]
              }
            ]
          }
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": {
        "glow": 1
      }
    },
    "repair": {
      "value": {}
    }
  },
  {
    "id": "U01",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "$ref": "#/$defs/owners"
            },
            {
              "enum": [
                "free"
              ]
            }
          ]
        }
      },
      "required": [
        "value"
      ],
      "$defs": {
        "owners": {
          "enum": [
            "crew",
            "trade"
          ]
        }
      }
    },
    "bad": {
      "value": "unknown"
    },
    "repair": {
      "value": "crew"
    }
  },
  {
    "id": "U13",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "oneOf": [
            {
              "$ref": "#/$defs/base"
            },
            {
              "type": "object",
              "required": [
                "kind",
                "count"
              ],
              "properties": {
                "kind": {
                  "const": "mode1"
                },
                "count": {
                  "type": "integer",
                  "minimum": 1
                }
              },
              "additionalProperties": false
            },
            {
              "type": "object",
              "required": [
                "kind",
                "count"
              ],
              "properties": {
                "kind": {
                  "const": "mode2"
                },
                "count": {
                  "type": "integer",
                  "minimum": 1
                }
              },
              "additionalProperties": false
            },
            {
              "type": "object",
              "required": [
                "kind",
                "count"
              ],
              "properties": {
                "kind": {
                  "const": "mode3"
                },
                "count": {
                  "type": "integer",
                  "minimum": 1
                }
              },
              "additionalProperties": false
            },
            {
              "type": "object",
              "required": [
                "kind",
                "count"
              ],
              "properties": {
                "kind": {
                  "const": "mode4"
                },
                "count": {
                  "type": "integer",
                  "minimum": 1
                }
              },
              "additionalProperties": false
            },
            {
              "type": "object",
              "required": [
                "kind",
                "count"
              ],
              "properties": {
                "kind": {
                  "const": "mode5"
                },
                "count": {
                  "type": "integer",
                  "minimum": 1
                }
              },
              "additionalProperties": false
            },
            {
              "type": "object",
              "required": [
                "kind",
                "count"
              ],
              "properties": {
                "kind": {
                  "const": "mode6"
                },
                "count": {
                  "type": "integer",
                  "minimum": 1
                }
              },
              "additionalProperties": false
            },
            {
              "type": "object",
              "required": [
                "kind",
                "count"
              ],
              "properties": {
                "kind": {
                  "const": "mode7"
                },
                "count": {
                  "type": "integer",
                  "minimum": 1
                }
              },
              "additionalProperties": false
            },
            {
              "type": "object",
              "required": [
                "kind",
                "count"
              ],
              "properties": {
                "kind": {
                  "const": "mode8"
                },
                "count": {
                  "type": "integer",
                  "minimum": 1
                }
              },
              "additionalProperties": false
            },
            {
              "type": "object",
              "required": [
                "kind",
                "count"
              ],
              "properties": {
                "kind": {
                  "const": "mode9"
                },
                "count": {
                  "type": "integer",
                  "minimum": 1
                }
              },
              "additionalProperties": false
            },
            {
              "type": "object",
              "required": [
                "kind",
                "count"
              ],
              "properties": {
                "kind": {
                  "const": "mode10"
                },
                "count": {
                  "type": "integer",
                  "minimum": 1
                }
              },
              "additionalProperties": false
            }
          ]
        }
      },
      "required": [
        "value"
      ],
      "$defs": {
        "base": {
          "type": "object",
          "required": [
            "kind",
            "amount"
          ],
          "properties": {
            "kind": {
              "const": "base"
            },
            "amount": {
              "type": "number"
            }
          },
          "additionalProperties": false
        }
      }
    },
    "bad": {
      "value": {
        "kind": "mode3",
        "count": 0
      }
    },
    "repair": {
      "value": {
        "kind": "mode3",
        "count": 1
      }
    }
  },
  {
    "id": "U14",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "oneOf": [
            {
              "type": "string",
              "minLength": 1
            },
            {
              "$ref": "#/$defs/step"
            }
          ]
        }
      },
      "required": [
        "value"
      ],
      "$defs": {
        "step": {
          "type": "object",
          "required": [
            "text"
          ],
          "properties": {
            "text": {
              "type": "string",
              "minLength": 1
            }
          },
          "additionalProperties": false
        }
      }
    },
    "bad": {
      "value": {
        "text": ""
      }
    },
    "repair": {
      "value": {
        "text": "Proceed"
      }
    }
  },
  {
    "id": "U16",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "$ref": "#/$defs/requires"
        }
      },
      "required": [
        "value"
      ],
      "$defs": {
        "requires": {
          "oneOf": [
            {
              "const": true
            },
            {
              "type": "object",
              "required": [
                "key",
                "in"
              ],
              "properties": {
                "key": {
                  "type": "string",
                  "minLength": 1
                },
                "in": {
                  "type": "array",
                  "minItems": 1,
                  "items": {
                    "enum": [
                      "A",
                      "B"
                    ]
                  }
                }
              },
              "additionalProperties": false
            },
            {
              "type": "object",
              "required": [
                "all"
              ],
              "properties": {
                "all": {
                  "type": "array",
                  "minItems": 1,
                  "items": {
                    "$ref": "#/$defs/requires"
                  }
                }
              },
              "additionalProperties": false
            },
            {
              "type": "object",
              "required": [
                "any"
              ],
              "properties": {
                "any": {
                  "type": "array",
                  "minItems": 1,
                  "items": {
                    "$ref": "#/$defs/requires"
                  }
                }
              },
              "additionalProperties": false
            },
            {
              "type": "object",
              "required": [
                "not"
              ],
              "properties": {
                "not": {
                  "$ref": "#/$defs/requires"
                }
              },
              "additionalProperties": false
            }
          ]
        }
      }
    },
    "bad": {
      "value": {
        "all": []
      }
    },
    "repair": {
      "value": {
        "all": [
          true
        ]
      }
    }
  },
  {
    "id": "X-required-both",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "type": "object",
          "properties": {
            "item": {
              "type": "string",
              "minLength": 1
            },
            "group": {
              "type": "string",
              "minLength": 1
            }
          },
          "additionalProperties": false,
          "oneOf": [
            {
              "required": [
                "item"
              ]
            },
            {
              "required": [
                "group"
              ]
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": {
        "item": "one",
        "group": "two"
      }
    },
    "repair": {
      "value": {
        "item": "one"
      }
    }
  },
  {
    "id": "X-status-key",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "type": "object",
          "oneOf": [
            {
              "required": [
                "status"
              ],
              "not": {
                "required": [
                  "key"
                ]
              }
            },
            {
              "required": [
                "key"
              ],
              "not": {
                "required": [
                  "status"
                ]
              }
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": {
        "status": "ready",
        "key": "sample"
      }
    },
    "repair": {
      "value": {
        "status": "ready"
      }
    }
  },
  {
    "id": "X-wrong-nearest",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "oneOf": [
            {
              "type": "object",
              "required": [
                "kind",
                "count",
                "range",
                "time"
              ],
              "properties": {
                "kind": {
                  "const": "A"
                },
                "count": {
                  "type": "number"
                },
                "range": {
                  "type": "number"
                },
                "time": {
                  "type": "number"
                }
              }
            },
            {
              "type": "object",
              "required": [
                "kind"
              ],
              "properties": {
                "kind": {
                  "const": "B"
                }
              }
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": {
        "kind": "A"
      }
    },
    "repair": {
      "value": {
        "kind": "A",
        "count": 1,
        "range": 1,
        "time": 1
      }
    }
  },
  {
    "id": "X-unknown-kind",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "oneOf": [
            {
              "type": "object",
              "required": [
                "kind"
              ],
              "properties": {
                "kind": {
                  "const": "A"
                }
              }
            },
            {
              "type": "object",
              "required": [
                "kind"
              ],
              "properties": {
                "kind": {
                  "const": "B"
                }
              }
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": {
        "kind": "C"
      }
    },
    "repair": {
      "value": {
        "kind": "A"
      }
    }
  },
  {
    "id": "X-tie",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "oneOf": [
            {
              "type": "object",
              "required": [
                "x",
                "y"
              ]
            },
            {
              "type": "object",
              "required": [
                "u",
                "v"
              ]
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": {}
    },
    "repair": {
      "value": {
        "x": 1,
        "y": 1
      }
    }
  },
  {
    "id": "X-overlap",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "oneOf": [
            {
              "type": "number"
            },
            {
              "type": "integer"
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": 2
    },
    "repair": {
      "value": 2.5
    }
  },
  {
    "id": "X-type-array",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "type": [
            "number",
            "string"
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": true
    },
    "repair": {
      "value": "word"
    }
  },
  {
    "id": "X-outer-min",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "minimum": 10,
          "anyOf": [
            {
              "enum": [
                1,
                10,
                20
              ]
            },
            {
              "enum": [
                30
              ]
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": 1
    },
    "repair": {
      "value": 10
    }
  },
  {
    "id": "X-missing-rule",
    "schema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "$ref": "missing.json"
            },
            {
              "type": "number"
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    },
    "bad": {
      "value": "word"
    },
    "repair": {
      "value": "word"
    },
    "repairSchema": {
      "type": "object",
      "properties": {
        "value": {
          "anyOf": [
            {
              "type": "number"
            },
            {
              "type": "string"
            }
          ]
        }
      },
      "required": [
        "value"
      ]
    }
  }
]
""";
}
