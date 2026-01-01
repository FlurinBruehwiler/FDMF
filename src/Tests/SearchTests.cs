using Shared;
using Shared.Database;
using TestModel.Generated;
using Environment = Shared.Environment;

namespace Tests;

[CollectionDefinition(DatabaseCollection.DatabaseCollectionName)]
public class SearchTests
{

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Exact_String_Search(bool indexed)
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        testModel.FieldsById[TestingFolder.Fields.Name].IsIndexed = indexed;

        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

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

        var unnamedFolder = new TestingFolder(tsx);

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new StringCriterion
        {
            FieldId = TestingFolder.Fields.Name,
            Value = "Barbapapa",
            Type = StringCriterion.MatchType.Exact
        });

        AssertEqual([barbapapaFolder], result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Substring_Search(bool indexed)
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        testModel.FieldsById[TestingFolder.Fields.Name].IsIndexed = indexed;
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var folderA = new TestingFolder(tsx)
        {
            Name = "oooHooo"
        };

        var folderB = new TestingFolder(tsx)
        {
            Name = "oooooo"
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new StringCriterion
        {
            FieldId = TestingFolder.Fields.Name,
            Value = "oooo",
            Type = StringCriterion.MatchType.Substring,
        });

        AssertEqual([folderB], result);
    }

    [Fact]
    public void Fuzzy_Search()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        new TestingFolder(tsx)
        {
            Name = "Foxtrott"
        };

        var folderB = new TestingFolder(tsx)
        {
            Name = "Firefox :)"
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new StringCriterion
        {
            FieldId = TestingFolder.Fields.Name,
            Value = "firfo",
            Type = StringCriterion.MatchType.Fuzzy,
            FuzzyCutoff = 0.3f
        });

        AssertEqual([folderB], result);
    }

    [Fact]
    public void Assoc_Search()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var folderA = new TestingFolder(tsx);

        var folderB = new TestingFolder(tsx)
        {
            Parent = folderA
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new AssocCriterion
        {
            FieldId = TestingFolder.Fields.Parent,
            Type = AssocCriterion.AssocCriterionType.Subquery,
            SearchCriterion = new IdCriterion
            {
                Guid = folderA.ObjId
            }
        });

        AssertEqual([folderB], result);

        var result2 = Searcher.Search<TestingFolder>(tsx, new AssocCriterion
        {
            FieldId = TestingFolder.Fields.Subfolders,
            Type = AssocCriterion.AssocCriterionType.Subquery,
            SearchCriterion = new IdCriterion
            {
                Guid = folderB.ObjId
            }
        });

        AssertEqual([folderA], result2);
    }

    [Fact]
    public void Assoc_Parent_Null_Search()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var folderA = new TestingFolder(tsx);

        _ = new TestingFolder(tsx)
        {
            Parent = folderA
        };

        var folderC = new TestingFolder(tsx);

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new AssocCriterion
        {
            FieldId = TestingFolder.Fields.Parent,
            Type = AssocCriterion.AssocCriterionType.Null,
        });

        AssertEqual([folderA, folderC], result);
    }

    [Fact]
    public void Assoc_Parent_NotNull_Search()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var folderA = new TestingFolder(tsx);

        var folderB = new TestingFolder(tsx)
        {
            Parent = folderA
        };

        _ = new TestingFolder(tsx);

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new AssocCriterion
        {
            FieldId = TestingFolder.Fields.Parent,
            Type = AssocCriterion.AssocCriterionType.NotNull,
        });

        AssertEqual([folderB], result);
    }

    [Fact]
    public void Assoc_Subfolders_Null_Search()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var folderA = new TestingFolder(tsx);

        var folderB = new TestingFolder(tsx)
        {
            Parent = folderA
        };

        var folderC = new TestingFolder(tsx);

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new AssocCriterion
        {
            FieldId = TestingFolder.Fields.Subfolders,
            Type = AssocCriterion.AssocCriterionType.Null,
        });

        AssertEqual([folderB, folderC], result);
    }

    [Fact]
    public void Assoc_Subfolders_NotNull_Search()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var folderA = new TestingFolder(tsx);

        _ = new TestingFolder(tsx)
        {
            Parent = folderA
        };

        _ = new TestingFolder(tsx);

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new AssocCriterion
        {
            FieldId = TestingFolder.Fields.Subfolders,
            Type = AssocCriterion.AssocCriterionType.NotNull,
        });

        AssertEqual([folderA], result);
    }

    [Fact]
    public void Default_String_Search()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        testModel.FieldsById[TestingFolder.Fields.Name].IsIndexed = true;

        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var defaultFolder = new TestingFolder(tsx);

        _ = new TestingFolder(tsx)
        {
            Name = "abc"
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new StringCriterion
        {
            FieldId = TestingFolder.Fields.Name,
            Value = string.Empty,
            Type = StringCriterion.MatchType.Exact,
        });

        // Expect unset fields to behave like default(empty string).
        AssertEqual([defaultFolder], result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Default_Integer_Search(bool indexed)
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        testModel.FieldsById[TestingFolder.Fields.TestIntegerField].IsIndexed = indexed;

        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var defaultFolder = new TestingFolder(tsx);

        _ = new TestingFolder(tsx)
        {
            TestIntegerField = 1
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new LongCriterion
        {
            FieldId = TestingFolder.Fields.TestIntegerField,
            From = 0,
            To = 0
        });

        // Expect unset fields to behave like default(0).
        AssertEqual([defaultFolder], result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Default_Decimal_Search(bool indexed)
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        testModel.FieldsById[TestingFolder.Fields.TestDecimalField].IsIndexed = indexed;

        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var defaultFolder = new TestingFolder(tsx);

        _ = new TestingFolder(tsx)
        {
            TestDecimalField = 1
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new DecimalCriterion
        {
            FieldId = TestingFolder.Fields.TestDecimalField,
            From = 0,
            To = 0
        });

        // Expect unset fields to behave like default(0m).
        AssertEqual([defaultFolder], result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Default_DateTime_Search(bool indexed)
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        testModel.FieldsById[TestingFolder.Fields.TestDateField].IsIndexed = indexed;

        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var defaultFolder = new TestingFolder(tsx);

        _ = new TestingFolder(tsx)
        {
            TestDateField = new DateTime(2000, 01, 01)
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new DateTimeCriterion
        {
            FieldId = TestingFolder.Fields.TestDateField,
            From = default,
            To = default
        });

        // Expect unset fields to behave like default(DateTime).
        AssertEqual([defaultFolder], result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Boolean_Search(bool indexed)
    {
        var boolFieldId = TestingFolder.Fields.TestBoolField;

        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        testModel.FieldsById[boolFieldId].IsIndexed = indexed;

        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());
        using var tsx = new DbSession(env);

        _ = new TestingFolder(tsx);

        var trueFolder = new TestingFolder(tsx);
        var trueValue = true;
        tsx.SetFldValue(trueFolder.ObjId, boolFieldId, trueValue.AsSpan());

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new BooleanCriterion
        {
            FieldId = boolFieldId,
            Value = true
        });

        AssertEqual([trueFolder], result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Default_Boolean_Search(bool indexed)
    {
        var boolFieldId = TestingFolder.Fields.TestBoolField;

        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        testModel.FieldsById[boolFieldId].IsIndexed = indexed;

        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());
        using var tsx = new DbSession(env);

        var defaultA = new TestingFolder(tsx);
        var defaultB = new TestingFolder(tsx);

        var trueFolder = new TestingFolder(tsx);
        var trueValue = true;
        tsx.SetFldValue(trueFolder.ObjId, boolFieldId, trueValue.AsSpan());

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new BooleanCriterion
        {
            FieldId = boolFieldId,
            Value = false
        });

        AssertEqual([defaultA, defaultB], result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DateTime_Search(bool indexed)
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        testModel.FieldsById[TestingFolder.Fields.TestDateField].IsIndexed = indexed;

        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var startOfDay = new TestingFolder(tsx)
        {
            TestDateField = new DateTime(2000, 09, 13)
        };

        var middleOfDay = new TestingFolder(tsx)
        {
            TestDateField = new DateTime(2000, 09, 13, hour: 8, 0, 0)
        };

        var previousDay = new TestingFolder(tsx)
        {
            TestDateField = new DateTime(2000, 09, 12)
        };

        var nextDay = new TestingFolder(tsx)
        {
            TestDateField = new DateTime(2000, 09, 14)
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new DateTimeCriterion
        {
            FieldId  = TestingFolder.Fields.TestDateField,
            From = new DateTime(2000, 09, 13),
            To = new DateTime(2000, 09, 13).AddDays(1).AddTicks(-1)
        });

        AssertEqual([startOfDay, middleOfDay], result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decimal_Search(bool indexed)
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        testModel.FieldsById[TestingFolder.Fields.TestDecimalField].IsIndexed = indexed;

        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var folder1 = new TestingFolder(tsx)
        {
            TestDecimalField = 14
        };

        var folder2 = new TestingFolder(tsx)
        {
            TestDecimalField = 15
        };

        var folder5 = new TestingFolder(tsx)
        {
            TestDecimalField = 17
        };

        var folder3 = new TestingFolder(tsx)
        {
            TestDecimalField = 20
        };

        var folder4 = new TestingFolder(tsx)
        {
            TestDecimalField = 21
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new DecimalCriterion
        {
            FieldId  = TestingFolder.Fields.TestDecimalField,
            From = 15,
            To = 20
        });

        AssertEqual([folder2, folder5, folder3], result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Integer_Search(bool indexed)
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        testModel.FieldsById[TestingFolder.Fields.TestIntegerField].IsIndexed = indexed;

        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var folder1 = new TestingFolder(tsx)
        {
            TestIntegerField = 14,
        };

        var folder2 = new TestingFolder(tsx)
        {
            TestIntegerField = 15
        };

        var folder5 = new TestingFolder(tsx)
        {
            TestIntegerField = 17
        };

        var folder3 = new TestingFolder(tsx)
        {
            TestIntegerField = 20
        };

        var folder4 = new TestingFolder(tsx)
        {
            TestIntegerField = 21
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new LongCriterion
        {
            FieldId  = TestingFolder.Fields.TestIntegerField,
            From = 15,
            To = 20
        });

        AssertEqual([folder2, folder5, folder3], result);
    }

    [Fact]
    public void Type_Search()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var folderA = new TestingFolder(tsx);

        var folderB = new TestingFolder(tsx)
        {
            Parent = folderA
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx);

        AssertEqual([folderA, folderB], result);
    }

    [Fact]
    public void SubQuery_Search()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var folderA = new TestingFolder(tsx)
        {
            Name = "Foo"
        };

        var folderB = new TestingFolder(tsx)
        {
            Parent = folderA
        };

        var folderC = new TestingFolder(tsx)
        {
            Name = "Bar"
        };

        var folderD = new TestingFolder(tsx)
        {
            Parent = folderC
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new AssocCriterion
        {
            FieldId = TestingFolder.Fields.Parent,
            Type = AssocCriterion.AssocCriterionType.Subquery,
            SearchCriterion = new StringCriterion
            {
                FieldId = TestingFolder.Fields.Name,
                Type = StringCriterion.MatchType.Exact,
                Value = "Foo"
            }
        });

        AssertEqual([folderB], result);
    }

    [Fact]
    public void LogicalAnd_Search()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var folderA = new TestingFolder(tsx)
        {
            Name = "Foo",
            TestDateField = new DateTime(2004, 09, 13)
        };

        var folderB = new TestingFolder(tsx)
        {
            Name = "Foo",
            TestIntegerField = 42,
            TestDecimalField = 300.45M
        };

        var folderC = new TestingFolder(tsx)
        {
            Name = "Bar",
            TestDateField = new DateTime(2004, 09, 13),
            TestDecimalField = 20.3M
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new MultiCriterion()
        {
            Type = MultiCriterion.MultiType.AND,
            Criterions = [
                new StringCriterion
                {
                    FieldId = TestingFolder.Fields.Name,
                    Type = StringCriterion.MatchType.Exact,
                    Value = "Foo"
                },
                new DateTimeCriterion
                {
                    FieldId = TestingFolder.Fields.TestDateField,
                    From = new DateTime(2004, 09, 13),
                    To = new DateTime(2004, 09, 13).AddDays(1).AddTicks(-1)
                }
            ]
        });

        AssertEqual([folderA], result);
    }

    [Fact]
    public void LogicalOr_Search()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        using var tsx = new DbSession(env);

        var folderA = new TestingFolder(tsx)
        {
            Name = "Foo",
            TestDateField = new DateTime(2004, 09, 13)
        };

        var folderB = new TestingFolder(tsx)
        {
            TestIntegerField = 42,
            TestDecimalField = 300.45M
        };

        var folderC = new TestingFolder(tsx)
        {
            Name = "Bar",
            TestDateField = new DateTime(2004, 09, 13),
            TestDecimalField = 20.3M
        };

        tsx.Commit();

        var result = Searcher.Search<TestingFolder>(tsx, new MultiCriterion()
        {
            Type = MultiCriterion.MultiType.OR,
            Criterions = [
                new StringCriterion
                {
                    FieldId = TestingFolder.Fields.Name,
                    Type = StringCriterion.MatchType.Exact,
                    Value = "Foo"
                },
                new DateTimeCriterion
                {
                    FieldId = TestingFolder.Fields.TestDateField,
                    From = new DateTime(2004, 09, 13),
                    To = new DateTime(2004, 09, 13).AddDays(1).AddTicks(-1)
                }
            ]
        });

        AssertEqual([folderA, folderC], result);
    }

    private void AssertEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual) where T : ITransactionObject
    {
        Assert.Equal(expected.OrderBy(x => x.ObjId, new GuidComparer()) , actual.OrderBy(x => x.ObjId, new GuidComparer()));
    }
}