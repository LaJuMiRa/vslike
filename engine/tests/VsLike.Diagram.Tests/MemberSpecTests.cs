using VsLike.Diagram.Model;

namespace VsLike.Diagram.Tests;

public sealed class MemberSpecTests
{
    [Fact]
    public void Method_WithGenericParametersAndReturnType()
    {
        Assert.True(MemberSpec.TryParse(MemberKind.Method, "# Apply(map: Dictionary<string, int>, n: int): Task<bool>", "Order", out var m));
        Assert.Equal((Access.Protected, "Apply", "Task<bool>"), (m.Access, m.Name, m.Type));
        Assert.Equal(["Dictionary<string, int>", "int"], m.ParameterList.Select(p => p.Type));
    }

    [Fact]
    public void FieldDefaultsToPublic_PropertyKeepsInitializer()
    {
        Assert.True(MemberSpec.TryParse(MemberKind.Field, "_count: string", "A", out var field));
        Assert.Equal((Access.Public, "string"), (field.Access, field.Type));
        Assert.True(MemberSpec.TryParse(MemberKind.Property, "+ Name: string = \"\"", "A", out var property));
        Assert.Equal(("Name", "string", "\"\""), (property.Name, property.Type, property.Value));
    }

    [Fact]
    public void ConstructorMethodWithoutReturnAndEnumValue()
    {
        Assert.True(MemberSpec.TryParse(MemberKind.Constructor, "id: int", "Order", out var ctor));
        Assert.Equal("Order(int)", ctor.Signature);
        Assert.True(MemberSpec.TryParse(MemberKind.Method, "- Run(): void", "A", out var run));
        Assert.Equal((Access.Private, "void"), (run.Access, run.Type));
        Assert.True(MemberSpec.TryParse(MemberKind.EnumValue, "Paid = 5", "Status", out var value));
        Assert.Equal("5", value.Value);
    }

    [Theory]
    [InlineData(MemberKind.Field, "Count")]
    [InlineData(MemberKind.Property, "Count")]
    [InlineData(MemberKind.Method, "+ Count: int")]
    public void OnlyName_DefaultsToPublicInt(MemberKind kind, string text)
    {
        Assert.True(MemberSpec.TryParse(kind, text, "A", out var member));
        Assert.Equal((Access.Public, "Count", "int"), (member.Access, member.Name, member.Type));
    }

    [Theory]
    [InlineData("Run")]
    [InlineData("Run()")]
    public void MethodWithoutType_ReturnsVoid(string text)
    {
        Assert.True(MemberSpec.TryParse(MemberKind.Method, text, "A", out var member));
        Assert.Equal((Access.Public, "Run", "void"), (member.Access, member.Name, member.Type));
    }

    [Fact]
    public void ParameterWithoutType_IsInt_EventWithoutType_IsEventHandler()
    {
        Assert.True(MemberSpec.TryParse(MemberKind.Method, "Add(a, b: long)", "A", out var add));
        Assert.Equal(["int", "long"], add.ParameterList.Select(p => p.Type));
        Assert.True(MemberSpec.TryParse(MemberKind.Event, "Changed", "A", out var changed));
        Assert.Equal("EventHandler", changed.Type);
    }

    [Theory]
    [InlineData(MemberKind.Property, "")]
    [InlineData(MemberKind.Method, "Run(: int): int")]
    [InlineData(MemberKind.Field, "1abc: int")]
    public void Invalid_IsRejected(MemberKind kind, string text) => Assert.False(MemberSpec.TryParse(kind, text, "A", out _));
}
