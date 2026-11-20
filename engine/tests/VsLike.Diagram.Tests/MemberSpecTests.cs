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
    public void FieldDefaultsToPrivate_PropertyKeepsInitializer()
    {
        Assert.True(MemberSpec.TryParse(MemberKind.Field, "_count: int", "A", out var field));
        Assert.Equal(Access.Private, field.Access);
        Assert.True(MemberSpec.TryParse(MemberKind.Property, "+ Name: string = \"\"", "A", out var property));
        Assert.Equal(("Name", "string", "\"\""), (property.Name, property.Type, property.Value));
    }

    [Fact]
    public void ConstructorMethodWithoutReturnAndEnumValue()
    {
        Assert.True(MemberSpec.TryParse(MemberKind.Constructor, "id: int", "Order", out var ctor));
        Assert.Equal("Order(int)", ctor.Signature);
        Assert.True(MemberSpec.TryParse(MemberKind.Method, "Run()", "A", out var run));
        Assert.Equal("void", run.Type);
        Assert.True(MemberSpec.TryParse(MemberKind.EnumValue, "Paid = 5", "Status", out var value));
        Assert.Equal("5", value.Value);
    }

    [Theory]
    [InlineData(MemberKind.Property, "Name")]
    [InlineData(MemberKind.Method, "Run(x): int")]
    [InlineData(MemberKind.Field, "1abc: int")]
    public void Invalid_IsRejected(MemberKind kind, string text) => Assert.False(MemberSpec.TryParse(kind, text, "A", out _));
}
