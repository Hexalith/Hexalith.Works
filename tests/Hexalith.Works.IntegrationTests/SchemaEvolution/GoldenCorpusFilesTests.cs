using System.IO;

using Shouldly;

namespace Hexalith.Works.IntegrationTests.SchemaEvolution;

/// <summary>Exercises stale copied fixtures and ambiguous checkout discovery in isolated directories.</summary>
public sealed class GoldenCorpusFilesTests
{
    /// <summary>A source deletion is refused even when PreserveNewest leaves a complete copied corpus.</summary>
    [Theory]
    [InlineData("Golden")]
    [InlineData("EventPersisterGolden")]
    public void DeletedSourceFixtureCannotBeSatisfiedByCopiedOutput(string corpusName)
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("works-golden-membership-");
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(temporary.FullName, "source", corpusName)).FullName;
            string output = Directory.CreateDirectory(Path.Combine(temporary.FullName, "output", corpusName)).FullName;
            const string fileName = "WorkItemCreated.v1.json";
            File.WriteAllText(Path.Combine(source, fileName), "{}");
            File.WriteAllText(Path.Combine(output, fileName), "{}");
            GoldenCorpusFiles.AssertMembership([fileName], source, output);

            File.Delete(Path.Combine(source, fileName));

            File.Exists(Path.Combine(output, fileName)).ShouldBeTrue();
            Should.Throw<ShouldAssertException>(() => GoldenCorpusFiles.AssertMembership([fileName], source, output))
                .Message.ShouldContain("source filenames must match");
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    /// <summary>Source additions and stale extra output are independently refused.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnexpectedFixtureCannotEscapeBidirectionalMembership(bool extraInSource)
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("works-golden-extra-");
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(temporary.FullName, "source")).FullName;
            string output = Directory.CreateDirectory(Path.Combine(temporary.FullName, "output")).FullName;
            const string fileName = "WorkItemCreated.v1.json";
            File.WriteAllText(Path.Combine(source, fileName), "{}");
            File.WriteAllText(Path.Combine(output, fileName), "{}");
            File.WriteAllText(Path.Combine(extraInSource ? source : output, "Unexpected.v1.json"), "{}");

            Should.Throw<ShouldAssertException>(() => GoldenCorpusFiles.AssertMembership([fileName], source, output));
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    /// <summary>A custom output directory under another checkout must not silently select that checkout.</summary>
    [Fact]
    public void SourceDiscoveryRejectsConflictingCheckouts()
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("works-golden-conflict-");
        try
        {
            string first = CreateCheckout(Path.Combine(temporary.FullName, "first"));
            string second = CreateCheckout(Path.Combine(temporary.FullName, "second"));

            Should.Throw<InvalidOperationException>(() => GoldenCorpusFiles.SourceDirectory("Golden", first, second))
                .Message.ShouldContain("Conflicting Works source checkouts");
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    /// <summary>Output outside a checkout can use the working directory, while missing source fails closed.</summary>
    [Fact]
    public void SourceDiscoverySupportsExternalOutputAndRequiresACheckout()
    {
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("works-golden-external-");
        try
        {
            string output = Directory.CreateDirectory(Path.Combine(temporary.FullName, "output")).FullName;
            Should.Throw<DirectoryNotFoundException>(() => GoldenCorpusFiles.SourceDirectory("Golden", output));
            string checkout = CreateCheckout(Path.Combine(temporary.FullName, "checkout"));

            GoldenCorpusFiles.SourceDirectory("Golden", output, checkout).ShouldBe(
                Path.Combine(checkout, "tests", "Hexalith.Works.IntegrationTests", "SchemaEvolution", "Golden"));
            GoldenCorpusFiles.SourceDirectory("Golden", checkout, checkout).ShouldBe(
                Path.Combine(checkout, "tests", "Hexalith.Works.IntegrationTests", "SchemaEvolution", "Golden"));
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    private static string CreateCheckout(string root)
    {
        string project = Directory.CreateDirectory(Path.Combine(root, "tests", "Hexalith.Works.IntegrationTests")).FullName;
        File.WriteAllText(Path.Combine(root, "Hexalith.Works.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(project, "Hexalith.Works.IntegrationTests.csproj"), "<Project />");
        return root;
    }
}
