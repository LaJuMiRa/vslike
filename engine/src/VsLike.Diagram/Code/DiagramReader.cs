using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VsLike.Diagram.Model;

namespace VsLike.Diagram.Code;

/// <summary>
/// Liest Typen, Mitglieder und Beziehungen aus dem Code. Basistypen und Interfaces außerhalb des Quelltexts werden als
/// externe (schreibgeschützte) Typen aufgenommen. Assoziationen entstehen aus Feld- und Property-Typen, die auf Typen der
/// Solution zeigen (auch über Collections/Arrays, dann mit Multiplizität *); readonly-Felder und get-only/init-Properties
/// gelten als Komposition.
/// </summary>
public static class DiagramReader
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.MinimallyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static async Task<DiagramModel> ReadAsync(Solution solution, CancellationToken cancellationToken)
    {
        var types = new Dictionary<string, (INamedTypeSymbol Symbol, HashSet<string> Files)>(StringComparer.Ordinal);
        foreach (var project in solution.Projects.Where(p => p.Language == LanguageNames.CSharp))
        {
            if (await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is { } compilation)
            {
                Collect(compilation, compilation.SyntaxTrees, types, cancellationToken);
            }
        }

        return Build(types);
    }

    public static DiagramModel Read(Compilation compilation, CancellationToken cancellationToken = default)
    {
        var types = new Dictionary<string, (INamedTypeSymbol Symbol, HashSet<string> Files)>(StringComparer.Ordinal);
        Collect(compilation, compilation.SyntaxTrees, types, cancellationToken);
        return Build(types);
    }

    public static string FullNameOf(INamedTypeSymbol symbol)
    {
        var names = new List<string>();
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            names.Insert(0, current.Name + (current.Arity > 0 ? "`" + current.Arity : string.Empty));
        }

        if (!symbol.ContainingNamespace.IsGlobalNamespace)
        {
            names.Insert(0, symbol.ContainingNamespace.ToDisplayString());
        }

        return string.Join('.', names);
    }

    private static void Collect(Compilation compilation, IEnumerable<SyntaxTree> trees, Dictionary<string, (INamedTypeSymbol Symbol, HashSet<string> Files)> types, CancellationToken cancellationToken)
    {
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot(cancellationToken).DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol symbol)
                {
                    continue;
                }

                var key = FullNameOf(symbol);
                if (!types.TryGetValue(key, out var entry))
                {
                    entry = (symbol, new HashSet<string>(StringComparer.Ordinal));
                    types[key] = entry;
                }

                entry.Files.Add(tree.FilePath);
            }
        }
    }

    private static DiagramModel Build(Dictionary<string, (INamedTypeSymbol Symbol, HashSet<string> Files)> sourceTypes)
    {
        var types = new List<DiagramType>();
        var relations = new List<DiagramRelation>();
        var external = new Dictionary<string, DiagramType>(StringComparer.Ordinal);
        foreach (var (key, (symbol, files)) in sourceTypes.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var baseType = symbol.TypeKind == TypeKind.Class && symbol.BaseType is { SpecialType: not SpecialType.System_Object } b && !IsRecordBase(symbol, b) ? b : null;
            var interfaces = symbol.Interfaces.Where(i => !(symbol.IsRecord && i.Name == "IEquatable")).ToList();
            types.Add(new DiagramType(
                key,
                symbol.Name,
                symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString(),
                KindOf(symbol),
                AccessOf(symbol.DeclaredAccessibility),
                ModifiersOf(symbol),
                [.. symbol.TypeParameters.Select(p => new DiagramTypeParameter(p.Name, Constraints(p)))],
                baseType?.ToDisplayString(TypeFormat),
                [.. interfaces.Select(i => i.ToDisplayString(TypeFormat))],
                [.. symbol.GetMembers().Where(IsShown).Select(MemberOf).OfType<DiagramMember>()],
                [.. files.Order(StringComparer.Ordinal)]));

            if (baseType is not null)
            {
                relations.Add(new DiagramRelation(key, Reference(baseType, sourceTypes, external), RelationKind.Inheritance));
            }

            foreach (var item in interfaces)
            {
                relations.Add(new DiagramRelation(key, Reference(item, sourceTypes, external), symbol.TypeKind == TypeKind.Interface ? RelationKind.Inheritance : RelationKind.Implementation));
            }

            foreach (var member in symbol.GetMembers().Where(IsShown))
            {
                if (Association(member, sourceTypes) is { } association)
                {
                    relations.Add(association with { From = key });
                }
            }
        }

        types.AddRange(external.Values.OrderBy(t => t.FullName, StringComparer.Ordinal));
        return new DiagramModel(types, relations);
    }

    private static string Reference(INamedTypeSymbol target, Dictionary<string, (INamedTypeSymbol Symbol, HashSet<string> Files)> sourceTypes, Dictionary<string, DiagramType> external)
    {
        var definition = target.OriginalDefinition;
        var key = FullNameOf(definition);
        if (!sourceTypes.ContainsKey(key) && !external.ContainsKey(key))
        {
            external[key] = new DiagramType(
                key,
                definition.Name,
                definition.ContainingNamespace.IsGlobalNamespace ? string.Empty : definition.ContainingNamespace.ToDisplayString(),
                KindOf(definition),
                AccessOf(definition.DeclaredAccessibility),
                TypeParameters: [.. definition.TypeParameters.Select(p => new DiagramTypeParameter(p.Name))],
                IsExternal: true);
        }

        return key;
    }

    private static DiagramRelation? Association(ISymbol member, Dictionary<string, (INamedTypeSymbol Symbol, HashSet<string> Files)> sourceTypes)
    {
        var (type, composite) = member switch
        {
            IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => ((ITypeSymbol?)null, false),
            IFieldSymbol field => (field.Type, field.IsReadOnly),
            IPropertySymbol property => (property.Type, property.SetMethod is null || property.SetMethod.IsInitOnly),
            _ => ((ITypeSymbol?)null, false),
        };
        if (type is null)
        {
            return null;
        }

        var (target, many) = ElementOf(type);
        if (target is not INamedTypeSymbol named || !sourceTypes.ContainsKey(FullNameOf(named.OriginalDefinition)))
        {
            return null;
        }

        return new DiagramRelation(string.Empty, FullNameOf(named.OriginalDefinition), composite ? RelationKind.Composition : RelationKind.Association, member.Name, many);
    }

    private static (ITypeSymbol? Element, bool Many) ElementOf(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        if (type is IArrayTypeSymbol array)
        {
            return (array.ElementType, true);
        }

        if (type is INamedTypeSymbol { IsGenericType: true } generic && generic.TypeArguments.Length == 1 && generic.AllInterfaces.Concat([generic]).Any(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T))
        {
            return (generic.TypeArguments[0], true);
        }

        return (type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), false);
    }

    private static bool IsRecordBase(INamedTypeSymbol symbol, INamedTypeSymbol baseType) => symbol.IsRecord && baseType.SpecialType == SpecialType.System_Object;

    private static bool IsShown(ISymbol member)
        => !member.IsImplicitlyDeclared
            && member is not INamedTypeSymbol
            && member is not IMethodSymbol { MethodKind: not (MethodKind.Ordinary or MethodKind.Constructor or MethodKind.StaticConstructor) }
            && member.Locations.Any(l => l.IsInSource);

    private static DiagramMember? MemberOf(ISymbol member)
    {
        var access = AccessOf(member.DeclaredAccessibility);
        switch (member)
        {
            case IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } value:
                return new DiagramMember(MemberKind.EnumValue, value.Name, Value: Initializer(value));
            case IFieldSymbol field:
                var fieldModifiers = Flags((field.IsStatic && !field.IsConst, MemberModifiers.Static), (field.IsReadOnly, MemberModifiers.ReadOnly), (field.IsConst, MemberModifiers.Const), (field.IsRequired, MemberModifiers.Required));
                return new DiagramMember(MemberKind.Field, field.Name, field.Type.ToDisplayString(TypeFormat), access, fieldModifiers, Value: Initializer(field));
            case IPropertySymbol property:
                var accessors = property.SetMethod switch
                {
                    null => PropertyAccessors.GetOnly,
                    { IsInitOnly: true } => PropertyAccessors.GetInit,
                    { DeclaredAccessibility: Accessibility.Private } when property.DeclaredAccessibility != Accessibility.Private => PropertyAccessors.GetPrivateSet,
                    _ => PropertyAccessors.GetSet,
                };
                return new DiagramMember(MemberKind.Property, property.Name, property.Type.ToDisplayString(TypeFormat), access, CommonModifiers(property) | (property.IsRequired ? MemberModifiers.Required : MemberModifiers.None), accessors, Value: Initializer(property));
            case IEventSymbol @event:
                return new DiagramMember(MemberKind.Event, @event.Name, @event.Type.ToDisplayString(TypeFormat), access, CommonModifiers(@event));
            case IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } constructor:
                return new DiagramMember(MemberKind.Constructor, constructor.ContainingType.Name, string.Empty, access, constructor.IsStatic ? MemberModifiers.Static : MemberModifiers.None, Parameters: Parameters(constructor));
            case IMethodSymbol method:
                return new DiagramMember(MemberKind.Method, method.Name, method.ReturnType.ToDisplayString(TypeFormat), access, CommonModifiers(method) | (method.IsAsync ? MemberModifiers.Async : MemberModifiers.None), Parameters: Parameters(method));
            default:
                return null;
        }
    }

    private static IReadOnlyList<DiagramParameter> Parameters(IMethodSymbol method)
        => [.. method.Parameters.Select(p => new DiagramParameter(p.Name, p.Type.ToDisplayString(TypeFormat)))];

    private static MemberModifiers CommonModifiers(ISymbol symbol)
        => Flags((symbol.IsStatic, MemberModifiers.Static), (symbol.IsAbstract && symbol.ContainingType.TypeKind != TypeKind.Interface, MemberModifiers.Abstract), (symbol.IsVirtual, MemberModifiers.Virtual), (symbol.IsOverride, MemberModifiers.Override));

    private static MemberModifiers Flags(params (bool On, MemberModifiers Flag)[] flags)
        => flags.Where(f => f.On).Aggregate(MemberModifiers.None, (all, f) => all | f.Flag);

    private static string? Initializer(ISymbol symbol)
    {
        var syntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        return syntax switch
        {
            VariableDeclaratorSyntax { Initializer: { } initializer } => initializer.Value.ToString(),
            PropertyDeclarationSyntax { Initializer: { } initializer } => initializer.Value.ToString(),
            EnumMemberDeclarationSyntax { EqualsValue: { } value } => value.Value.ToString(),
            _ => null,
        };
    }

    private static string Constraints(ITypeParameterSymbol parameter)
    {
        var parts = new List<string>();
        if (parameter.HasReferenceTypeConstraint)
        {
            parts.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
        }

        if (parameter.HasUnmanagedTypeConstraint)
        {
            parts.Add("unmanaged");
        }
        else if (parameter.HasValueTypeConstraint)
        {
            parts.Add("struct");
        }

        if (parameter.HasNotNullConstraint)
        {
            parts.Add("notnull");
        }

        parts.AddRange(parameter.ConstraintTypes.Select(t => t.ToDisplayString(TypeFormat)));
        if (parameter.HasConstructorConstraint)
        {
            parts.Add("new()");
        }

        return string.Join(", ", parts);
    }

    private static DiagramTypeKind KindOf(INamedTypeSymbol symbol) => symbol.TypeKind switch
    {
        TypeKind.Interface => DiagramTypeKind.Interface,
        TypeKind.Enum => DiagramTypeKind.Enum,
        TypeKind.Struct when symbol.IsRecord => DiagramTypeKind.RecordStruct,
        TypeKind.Struct => DiagramTypeKind.Struct,
        _ when symbol.IsRecord => DiagramTypeKind.Record,
        _ => DiagramTypeKind.Class,
    };

    private static TypeModifiers ModifiersOf(INamedTypeSymbol symbol)
    {
        var modifiers = TypeModifiers.None;
        if (symbol.IsStatic)
        {
            modifiers |= TypeModifiers.Static;
        }
        else if (symbol.IsAbstract && symbol.TypeKind == TypeKind.Class)
        {
            modifiers |= TypeModifiers.Abstract;
        }
        else if (symbol.IsSealed && symbol.TypeKind == TypeKind.Class)
        {
            modifiers |= TypeModifiers.Sealed;
        }

        if (symbol.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(m => m.Text == "partial")))
        {
            modifiers |= TypeModifiers.Partial;
        }

        return modifiers;
    }

    private static Access AccessOf(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => Access.Public,
        Accessibility.Internal => Access.Internal,
        Accessibility.Protected => Access.Protected,
        Accessibility.ProtectedOrInternal => Access.ProtectedInternal,
        Accessibility.ProtectedAndInternal => Access.PrivateProtected,
        _ => Access.Private,
    };
}
