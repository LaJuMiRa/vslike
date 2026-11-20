using VsLike.Core.Build;
using VsLike.Projects.Testing;

namespace VsLike.Projects.Tests;

public sealed class TestOutputParserTests
{
    [Fact]
    public void ParseTestList_ReadsIndentedNamesAfterLocalizedHeader()
    {
        var output = new[]
        {
            "  Tst -> /x/bin/Debug/net10.0/Tst.dll",
            "Testlauf für \"/x/Tst.dll\" (.NETCoreApp,Version=v10.0)",
            "Die folgenden Tests sind verfügbar:",
            "    Tst.Inner.UnitTest1.Passes",
            "    Tst.Inner.UnitTest1.Data(x: 1)",
            "    Tst.Inner.UnitTest1.Data(x: 2)",
        }.Select(t => new OutputLine(t, false));

        Assert.Equal(["Tst.Inner.UnitTest1.Passes", "Tst.Inner.UnitTest1.Data(x: 1)", "Tst.Inner.UnitTest1.Data(x: 2)"], TestOutputParser.ParseTestList(output));
    }

    [Fact]
    public void ParseTrx_ReadsOutcomeDurationAndError()
    {
        const string trx = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="Ns.C.Fails" duration="00:00:00.0026858" outcome="Failed">
                  <Output><ErrorInfo><Message>Assert.Equal() Failure</Message>
                  <StackTrace>   at Ns.C.Fails() in /src/Tst/C.cs:line 9
               at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj)</StackTrace></ErrorInfo></Output>
                </UnitTestResult>
                <UnitTestResult testName="Ns.C.Passes" duration="00:00:01.5" outcome="Passed" />
                <UnitTestResult testName="Ns.C.Skip" duration="00:00:00" outcome="NotExecuted" />
              </Results>
            </TestRun>
            """;

        var results = TestOutputParser.ParseTrx(trx);

        Assert.Equal([TestOutcome.Failed, TestOutcome.Passed, TestOutcome.Skipped], results.Select(r => r.Outcome));
        Assert.Equal("Assert.Equal() Failure", results[0].Message);
        Assert.Equal(TimeSpan.FromSeconds(1.5), results[1].Duration);
        Assert.Equal(("/src/Tst/C.cs", 9), TestOutputParser.FirstSourceFrame(results[0].StackTrace));
    }

    [Theory]
    [InlineData("Ns.Sub.Class.Method", "Ns.Sub", "Class", "Method", "")]
    [InlineData("Ns.Class.Data(x: 1.5, s: \"a.b\")", "Ns", "Class", "Data", "(x: 1.5, s: \"a.b\")")]
    [InlineData("Class.Method", "", "Class", "Method", "")]
    public void TestName_SplitsNamespaceClassMethodAndArguments(string full, string ns, string cls, string method, string args)
    {
        var name = TestName.Parse(full);
        Assert.Equal((ns, cls, method, args), (name.Namespace, name.ClassName, name.Method, name.Arguments));
    }

    [Fact]
    public void BuildFilter_EscapesAndCombines()
    {
        Assert.Equal(
            @"FullyQualifiedName=Ns.C.Data|FullyQualifiedName~Ns.Other.",
            TestService.BuildFilter(["Ns.C.Data(x: 1)"], ["Ns.Other."]));
    }
}
