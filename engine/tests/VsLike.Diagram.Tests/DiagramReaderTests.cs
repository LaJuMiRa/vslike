using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VsLike.Diagram.Code;
using VsLike.Diagram.Model;

namespace VsLike.Diagram.Tests;

public sealed class DiagramReaderTests
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList());

    private static readonly DiagramModel Model = DiagramReader.Read(Compile(
        ("/src/Order.cs", """
            namespace Shop;

            public abstract partial class Order : Entity, IDisposable
            {
                private readonly Customer _customer = new();
                private int _count;

                protected Order(int id) { }

                public event EventHandler? Changed;

                public List<Item> Items { get; set; } = [];

                public required string Code { get; init; }

                public abstract decimal Total(bool withTax);

                public void Dispose() { }
            }
            """),
        ("/src/Order.Part.cs", "namespace Shop;\n\npublic partial class Order\n{\n    public static int Count() => 0;\n}\n"),
        ("/src/Model.cs", """
            namespace Shop;

            public class Entity { }

            public sealed class Customer { public Address? Home { get; set; } }

            public record Item(string Name, decimal Price);

            public record struct Address(string City);

            public enum Status { New, Paid = 5 }

            public interface IRepository<T> where T : Entity, new()
            {
                T? Find(int id);
            }

            internal static class Helpers { }
            """)));

    [Fact]
    public void Types_AreReadWithKindModifiersAndFiles()
    {
        var order = Type("Shop.Order");
        Assert.Equal(DiagramTypeKind.Class, order.Kind);
        Assert.Equal(TypeModifiers.Abstract | TypeModifiers.Partial, order.Modifiers);
        Assert.Equal(["/src/Order.Part.cs", "/src/Order.cs"], order.FileList);
        Assert.Equal("Entity", order.BaseType);
        Assert.Equal(["IDisposable"], order.InterfaceList);

        Assert.Equal(DiagramTypeKind.Record, Type("Shop.Item").Kind);
        Assert.Equal(DiagramTypeKind.RecordStruct, Type("Shop.Address").Kind);
        Assert.Equal(TypeModifiers.Sealed, Type("Shop.Customer").Modifiers);
        Assert.Equal((Access.Internal, TypeModifiers.Static), (Type("Shop.Helpers").Access, Type("Shop.Helpers").Modifiers));
        var repository = Type("Shop.IRepository`1");
        Assert.Equal("IRepository<T>", repository.DisplayName);
        Assert.Equal("Entity, new()", Assert.Single(repository.TypeParameterList).Constraints);
    }

    [Fact]
    public void Members_IncludeAllKindsWithAccessorsAndInitializers()
    {
        var members = Type("Shop.Order").MemberList;
        Assert.Contains(members, m => m is { Kind: MemberKind.Field, Name: "_customer", Type: "Customer", Access: Access.Private, Value: "new()" } && m.Modifiers.HasFlag(MemberModifiers.ReadOnly));
        Assert.Contains(members, m => m is { Kind: MemberKind.Constructor, Access: Access.Protected } && m.Signature == "Order(int)");
        Assert.Contains(members, m => m is { Kind: MemberKind.Event, Name: "Changed", Type: "EventHandler?" });
        Assert.Contains(members, m => m is { Kind: MemberKind.Property, Name: "Code", Accessors: PropertyAccessors.GetInit } && m.Modifiers.HasFlag(MemberModifiers.Required));
        Assert.Contains(members, m => m is { Kind: MemberKind.Method, Name: "Total", Type: "decimal" } && m.Modifiers.HasFlag(MemberModifiers.Abstract) && m.Signature == "Total(bool)");
        Assert.Contains(members, m => m is { Kind: MemberKind.Method, Name: "Count" } && m.Modifiers.HasFlag(MemberModifiers.Static));
        Assert.Equal(["New", "Paid"], Type("Shop.Status").MemberList.Select(m => m.Name));
        Assert.Equal("5", Type("Shop.Status").MemberList[1].Value);
        Assert.DoesNotContain(Type("Shop.Item").MemberList, m => m.Name is "EqualityContract" or "PrintMembers" or "<Clone>$");
    }

    [Fact]
    public void Relations_CoverInheritanceImplementationAssociationAndExternalTypes()
    {
        Assert.Contains(Model.Relations, r => r is { From: "Shop.Order", To: "Shop.Entity", Kind: RelationKind.Inheritance });
        Assert.Contains(Model.Relations, r => r is { From: "Shop.Order", To: "System.IDisposable", Kind: RelationKind.Implementation });
        Assert.Contains(Model.Relations, r => r is { From: "Shop.Order", To: "Shop.Customer", Kind: RelationKind.Composition, Label: "_customer", IsMany: false });
        Assert.Contains(Model.Relations, r => r is { From: "Shop.Order", To: "Shop.Item", Kind: RelationKind.Association, Label: "Items", IsMany: true });
        Assert.Contains(Model.Relations, r => r is { From: "Shop.Customer", To: "Shop.Address", Kind: RelationKind.Association });
        Assert.True(Type("System.IDisposable").IsExternal);
        Assert.False(Type("Shop.Entity").IsExternal);
    }

    private static DiagramType Type(string fullName) => Model.Find(fullName) ?? throw new InvalidOperationException(fullName + " missing");

    private static CSharpCompilation Compile(params (string Path, string Code)[] files)
        => CSharpCompilation.Create(
            "Test",
            files.Select(f => CSharpSyntaxTree.ParseText(f.Code, new CSharpParseOptions(LanguageVersion.Preview), f.Path)).Append(CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic;", new CSharpParseOptions(LanguageVersion.Preview), "/src/Usings.cs")),
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
}
