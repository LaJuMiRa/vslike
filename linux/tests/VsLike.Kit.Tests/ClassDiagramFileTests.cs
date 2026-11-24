using System.Text;
using VsLike.Kit;
using Xunit;

namespace VsLike.Kit.Tests;

public sealed class ClassDiagramFileTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <ClassDiagram MajorVersion="1" MinorVersion="1">
          <Comment CommentText="Domain model">
            <Position X="4.25" Y="0.5" Height="0.5" Width="1.5" />
          </Comment>
          <Class Name="Demo.Car" Collapsed="true">
            <Position X="0.5" Y="0.75" Width="1.5" />
            <TypeIdentifier>
              <HashCode>AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=</HashCode>
              <FileName>Car.cs</FileName>
            </TypeIdentifier>
          </Class>
          <Interface Name="Demo.Store&lt;TKey, TValue&gt;">
            <Position X="2.5" Y="0.5" Width="1.75" />
            <TypeIdentifier>
              <HashCode>AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=</HashCode>
              <FileName>Data\Store.cs</FileName>
            </TypeIdentifier>
          </Interface>
        </ClassDiagram>
        """;

    private static byte[] Data => Encoding.UTF8.GetBytes(Sample);

    [Fact]
    public void ReadsTypesPositionsInInchesAndCollapsedState()
    {
        var document = ClassDiagramFile.Read(Data);

        Assert.Equal(["Demo.Car", "Demo.Store`2"], document.Types.Select(t => t.FullName));
        Assert.Equal(48, document.Types[0].X);
        Assert.Equal(72, document.Types[0].Y);
        Assert.True(document.Types[0].Collapsed);
        Assert.Equal(240, document.Types[1].X);
        Assert.False(document.Types[1].Collapsed);
    }

    [Fact]
    public void ConvertsGenericNamesToDiagramNames()
    {
        Assert.Equal("Demo.Box`1", ClassDiagramFile.FullName("Demo.Box<T>"));
        Assert.Equal("Demo.Map`2", ClassDiagramFile.FullName("Demo.Map<TKey, List<TValue>>"));
        Assert.Equal("Demo.Outer.Inner", ClassDiagramFile.FullName("Demo.Outer.Inner"));
    }

    [Fact]
    public void WriteUpdatesPositionsAddsAndRemovesTypesAndKeepsOtherContent()
    {
        var document = ClassDiagramFile.Read(Data);
        document = document.With(document.Types[0].Moved(96, 144).Toggled());
        document = document.Without("Demo.Store`2");
        document = document.With(new TypePlacement("Demo.Engine", 192, 48));
        document = document.With(new TypePlacement("System.Object", 0, 0));
        var info = new Dictionary<string, ClassDiagramFile.TypeInfo> { ["Demo.Engine"] = new("Struct", "Demo.Engine", @"Parts\Engine.cs") };

        var data = ClassDiagramFile.Write(document, Data, info);
        var text = Encoding.UTF8.GetString(data);
        var reread = ClassDiagramFile.Read(data);

        Assert.Equal(["Demo.Car", "Demo.Engine"], reread.Types.Select(t => t.FullName));
        Assert.Equal(96, reread.Find("Demo.Car")?.X);
        Assert.Equal(144, reread.Find("Demo.Car")?.Y);
        Assert.False(reread.Find("Demo.Car")?.Collapsed);
        Assert.Contains("CommentText=\"Domain model\"", text);
        Assert.Contains("<Struct Name=\"Demo.Engine\">", text);
        Assert.Contains(@"<FileName>Parts\Engine.cs</FileName>", text);
        Assert.DoesNotContain("Store", text);
        Assert.DoesNotContain("System.Object", text);
    }

    [Fact]
    public void DiagramDocumentLoadsAndSavesThroughTheCdFormat()
    {
        var folder = Repository.TemporaryFolder("vslike-cd");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "Model.cd");
            File.WriteAllBytes(path, Data);

            Assert.True(DiagramDocument.IsDiagram(path));
            Assert.Equal("Model", DiagramDocument.Name(path));
            var document = DiagramDocument.Load(path);
            document.With(document.Types[0].Moved(480, 48)).Save(path);

            Assert.Equal(480, DiagramDocument.Load(path).Find("Demo.Car")?.X);
            Assert.Contains("<ClassDiagram", File.ReadAllText(path));
        }
        finally
        {
            Repository.Delete(folder);
        }
    }
}
