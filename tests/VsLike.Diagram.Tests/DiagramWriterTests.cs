using VsLike.Diagram.Code;
using VsLike.Diagram.Model;

namespace VsLike.Diagram.Tests;

/// <summary>Snapshot-Tests des Writers: Eingabecode + Diagrammaktion → erwarteter Code (Snapshots/*.verified.cs).</summary>
public sealed class DiagramWriterTests
{
    private const string Shop = """
        using System;

        namespace Shop;

        /// <summary>An order.</summary>
        public class Order
        {
            // keep this comment
            private readonly int _id;

            public Order(int id)
            {
                _id = id;
            }

            public string Customer { get; set; } = "";

            /// <summary>Total price.</summary>
            public decimal Total()
            {
                return 0m; // trailing comment
            }
        }

        public interface IOrderRepository
        {
            Order? Find(int id);
        }

        public enum Status
        {
            New,
            Paid
        }
        """;

    [Fact]
    public Task CreateTypeFile_GenericClassWithBaseAndConstraints()
        => Check(CodeText.TypeFile(
            new DiagramType("Shop.Repository`1", "Repository", "Shop", DiagramTypeKind.Class, Access.Public, TypeModifiers.Abstract | TypeModifiers.Partial, [new DiagramTypeParameter("T", "class, new()")], "RepositoryBase", ["IDisposable"]),
            "Shop.Data"));

    [Fact]
    public Task CreateTypeFile_RecordStruct() => Check(CodeText.TypeFile(new DiagramType("Shop.Money", "Money", "Shop", DiagramTypeKind.RecordStruct), "Shop"));

    [Fact]
    public Task CreateTypeFile_EnumWithoutNamespace() => Check(CodeText.TypeFile(new DiagramType("Color", "Color", string.Empty, DiagramTypeKind.Enum, Access.Internal), null));

    [Fact]
    public Task AddMember_PropertyGoesBetweenPropertiesAndMethods()
        => Check(DiagramWriter.AddMember(Shop, "Shop.Order", new DiagramMember(MemberKind.Property, "Notes", "string?", Accessors: PropertyAccessors.GetInit)));

    [Fact]
    public Task AddMember_FieldJoinsFieldGroupWithoutBlankLine()
        => Check(DiagramWriter.AddMember(Shop, "Shop.Order", new DiagramMember(MemberKind.Field, "_created", "DateTime", Access.Private, MemberModifiers.ReadOnly)));

    [Fact]
    public Task AddMember_MethodWithParametersAtEnd()
        => Check(DiagramWriter.AddMember(Shop, "Shop.Order", new DiagramMember(MemberKind.Method, "Apply", "bool", Parameters: [new DiagramParameter("code", "string"), new DiagramParameter("percent", "int")])));

    [Fact]
    public Task AddMember_InterfaceMethodHasNoBody()
        => Check(DiagramWriter.AddMember(Shop, "Shop.IOrderRepository", new DiagramMember(MemberKind.Method, "Save", "void", Parameters: [new DiagramParameter("order", "Order")])));

    [Fact]
    public Task AddMember_EnumValueAddsComma()
        => Check(DiagramWriter.AddMember(Shop, "Shop.Status", new DiagramMember(MemberKind.EnumValue, "Shipped", Value: "10")));

    [Fact]
    public Task AddMember_EnumValueKeepsTrailingCommaStyle()
        => Check(DiagramWriter.AddMember("enum E\n{\n    A,\n    B,\n}\n", "E", new DiagramMember(MemberKind.EnumValue, "C")));

    [Fact]
    public Task AddMember_EmptyClassOnOneLine()
        => Check(DiagramWriter.AddMember("namespace N;\n\npublic class Empty { }\n", "N.Empty", new DiagramMember(MemberKind.Event, "Changed", "EventHandler?")));

    [Fact]
    public Task AddMember_PositionalRecordGetsBody()
        => Check(DiagramWriter.AddMember("namespace N;\n\npublic record Point(int X, int Y);\n", "N.Point", new DiagramMember(MemberKind.Method, "Length", "double")));

    [Fact]
    public Task AddMember_NestedTypeAndCrLfAndTabs()
        => Check(DiagramWriter.AddMember("namespace N\r\n{\r\n\tpublic class Outer\r\n\t{\r\n\t\tpublic class Inner\r\n\t\t{\r\n\t\t\tpublic int A;\r\n\t\t}\r\n\t}\r\n}\r\n", "N.Outer.Inner", new DiagramMember(MemberKind.Constructor, "Inner")));

    [Fact]
    public Task RemoveMember_MethodWithDocumentation() => Check(DiagramWriter.RemoveMember(Shop, "Shop.Order", "Total()"));

    [Fact]
    public Task RemoveMember_FirstMemberLeavesNoBlankLineAfterBrace() => Check(DiagramWriter.RemoveMember(Shop, "Shop.Order", "_id"));

    [Fact]
    public Task RemoveMember_EnumValue() => Check(DiagramWriter.RemoveMember(Shop, "Shop.Status", "New"));

    [Fact]
    public Task UpdateMember_MethodKeepsBody()
        => Check(DiagramWriter.UpdateMember(Shop, "Shop.Order", "Total()", new DiagramMember(MemberKind.Method, "Total", "decimal", Access.Internal, MemberModifiers.Virtual, Parameters: [new DiagramParameter("withTax", "bool")])));

    [Fact]
    public Task UpdateMember_AutoPropertyBecomesRequiredInit()
        => Check(DiagramWriter.UpdateMember(Shop, "Shop.Order", "Customer", new DiagramMember(MemberKind.Property, "Customer", "string", Modifiers: MemberModifiers.Required, Accessors: PropertyAccessors.GetInit)));

    [Fact]
    public Task SetBaseList_AddsToClassWithoutBases() => Check(DiagramWriter.SetBaseList(Shop, "Shop.Order", "Entity", ["IComparable<Order>"]));

    [Fact]
    public Task SetBaseList_GenericWithConstraintsAndRemoval()
    {
        const string source = "public class Box<T> : Base\n    where T : struct\n{\n}\n\npublic record P(int X) : IFoo;\n";
        var withInterface = DiagramWriter.SetBaseList(source, "Box`1", "Base", ["IBox"]);
        var removed = DiagramWriter.SetBaseList(withInterface, "P", null, []);
        return Check(DiagramWriter.SetBaseList(removed, "P", "Shape", []));
    }

    [Fact]
    public Task SetTypeModifiers_ChangesOnlyModifiers()
        => Check(DiagramWriter.SetTypeModifiers(Shop, "Shop.Order", Access.Internal, TypeModifiers.Sealed | TypeModifiers.Partial));

    [Fact]
    public Task RemoveType_KeepsOtherTypes() => Check(DiagramWriter.RemoveType(Shop, "Shop.IOrderRepository")!);

    [Fact]
    public void RemoveType_LastTypeReturnsNull() => Assert.Null(DiagramWriter.RemoveType("namespace N;\n\nclass A\n{\n}\n", "N.A"));

    [Fact]
    public void Writer_LeavesUntouchedTextIdentical()
    {
        var result = DiagramWriter.AddMember(Shop, "Shop.IOrderRepository", new DiagramMember(MemberKind.Method, "Count", "int"));
        var head = Shop[..Shop.IndexOf("public interface", StringComparison.Ordinal)];
        var tail = Shop[Shop.IndexOf("public enum", StringComparison.Ordinal)..];
        Assert.StartsWith(head, result, StringComparison.Ordinal);
        Assert.EndsWith(tail, result, StringComparison.Ordinal);
    }

    private static Task Check(string code) => Verify(code, "cs").UseDirectory("Snapshots");
}
