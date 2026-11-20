using VsLike.App.Intelligence;
using VsLike.Core.Code;

namespace VsLike.App.Tests;

public class CompletionEditMapperTests
{
    [Fact]
    public void Map_ReplacesTypedAheadSegmentAndKeepsUsingInsert()
    {
        // Liste geöffnet bei "Str|" (Span 13..16), danach "ingBu" weitergetippt → Segment 13..21.
        const string current = "class C { M( StringBu ) }";
        IReadOnlyList<TextEdit> roslyn = [new TextEdit(0, 0, "using System.Text;\n"), new TextEdit(13, 3, "StringBuilder")];

        var (edits, caret) = CompletionEditMapper.Map(roslyn, 13 + 19 + "StringBuilder".Length, 13, 3, 13, 8);
        var result = WorkspaceEditApplier.ApplyToText(current, edits);

        Assert.Equal("using System.Text;\nclass C { M( StringBuilder ) }", result);
        Assert.Equal("using System.Text;\nclass C { M( StringBuilder".Length, caret);
    }

    [Fact]
    public void Map_ShiftsEditsBehindTheSegment()
    {
        IReadOnlyList<TextEdit> roslyn = [new TextEdit(2, 1, "Foo"), new TextEdit(10, 0, "!")];

        var (edits, _) = CompletionEditMapper.Map(roslyn, null, 2, 1, 2, 3);

        Assert.Equal(new TextEdit(12, 0, "!"), edits[1]);
        Assert.Equal(new TextEdit(2, 3, "Foo"), edits[0]);
    }
}
