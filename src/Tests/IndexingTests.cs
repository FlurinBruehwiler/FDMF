using Shared;
using Shared.Database;
using TestModel.Generated;
using Environment = Shared.Environment;

namespace Tests;

public class IndexingTests
{
    [Fact]
    public void Exact_String_Search()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        var env = Environment.Create(testModel);

        using var tsx = new DbSession(env);

        new TestingFolder(tsx)
        {
            Name = "Barbapapa Ba"
        };

        new TestingFolder(tsx)
        {
            Name = "Foooooo"
        };

        var barbapapaFolder = new TestingFolder(tsx)
        {
            Name = "Barbapapa"
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new SearchCriterion
        {
            Type = SearchCriterion.CriterionType.String,
            String = new SearchCriterion.StringCriterion
            {
                FieldId = TestingFolder.Fields.Name,
                Value = "Barbapapa",
                Type = SearchCriterion.StringCriterion.MatchType.Exact
            }
        });

        Assert.Equal([barbapapaFolder], result);
    }
}