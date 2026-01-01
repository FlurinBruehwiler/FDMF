using Shared;
using Shared.Database;
using TestModel.Generated;
using Environment = Shared.Environment;

namespace Tests;

[CollectionDefinition(DatabaseCollection.DatabaseCollectionName)]
public class HistoryTests
{
    [Fact]
    public void History_Commits_Can_Be_Enumerated()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        Guid objId;

        using (var session = new DbSession(env))
        {
            var folder = new TestingFolder(session);
            objId = folder.ObjId;

            folder.Name = "one";
            session.Commit();

            folder.Name = "two";
            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);

        var commits = History.GetAllCommits(env, readSession.Store.ReadTransaction).ToList();
        Assert.True(commits.Count >= 2);

        var commitsForObj = History.GetCommitsForObject(env, readSession.Store.ReadTransaction, objId).ToList();
        Assert.Equal(2, commitsForObj.Count);

        // Verify the object index points at real commits.
        Assert.NotNull(History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsForObj[0]));
        Assert.NotNull(History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsForObj[1]));
    }

    [Fact]
    public void History_Assoc_Add_And_Remove_Are_Recorded_On_Both_Sides()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        Guid aId;
        Guid bId;

        using (var session = new DbSession(env))
        {
            var a = new TestingFolder(session);
            var b = new TestingFolder(session);

            aId = a.ObjId;
            bId = b.ObjId;

            a.Parent = b;
            session.Commit();

            a.Parent = null;
            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);

        var commitsA = History.GetCommitsForObject(env, readSession.Store.ReadTransaction, aId).ToList();
        var commitsB = History.GetCommitsForObject(env, readSession.Store.ReadTransaction, bId).ToList();

        Assert.Equal(2, commitsA.Count);
        Assert.Equal(2, commitsB.Count);

        var addCommitA = History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsA[0]);
        var addCommitB = History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsB[0]);
        Assert.NotNull(addCommitA);
        Assert.NotNull(addCommitB);

        Assert.Contains(addCommitA!.EventsByObject[aId], e => e.Type == HistoryEventType.AsoAdded);
        Assert.Contains(addCommitB!.EventsByObject[bId], e => e.Type == HistoryEventType.AsoAdded);

        var removeCommitA = History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsA[1]);
        var removeCommitB = History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsB[1]);
        Assert.NotNull(removeCommitA);
        Assert.NotNull(removeCommitB);

        Assert.Contains(removeCommitA!.EventsByObject[aId], e => e.Type == HistoryEventType.AsoRemoved);
        Assert.Contains(removeCommitB!.EventsByObject[bId], e => e.Type == HistoryEventType.AsoRemoved);
    }

    [Fact]
    public void History_Field_Delete_Is_Recorded_As_Defaulting()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        Guid objId;

        using (var session = new DbSession(env))
        {
            var folder = new TestingFolder(session);
            objId = folder.ObjId;

            long value = 123;
            session.SetFldValue(objId, TestingFolder.Fields.TestIntegerField, value.AsSpan());
            session.Commit();

            // Delete VAL entry -> default value
            session.SetFldValue(objId, TestingFolder.Fields.TestIntegerField, ReadOnlySpan<byte>.Empty);
            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);

        var commits = History.GetCommitsForObject(env, readSession.Store.ReadTransaction, objId).ToList();
        Assert.Equal(2, commits.Count);

        var commit = History.TryGetCommit(env, readSession.Store.ReadTransaction, commits[1]);
        Assert.NotNull(commit);

        var events = commit!.EventsByObject[objId];
        var fldEvent = Assert.Single(events, e => e.Type == HistoryEventType.FldChanged);

        Assert.Equal(TestingFolder.Fields.TestIntegerField, fldEvent.FldId);
        Assert.True(fldEvent.OldValue.Length > 0);
        Assert.Empty(fldEvent.NewValue);
    }

    [Fact]
    public void History_Records_Field_Changes_For_All_Types()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        Guid objId;

        using (var session = new DbSession(env))
        {
            var folder = new TestingFolder(session);
            objId = folder.ObjId;

            folder.Name = "a";

            long i1 = 1;
            decimal d1 = 1;
            DateTime t1 = new DateTime(2000, 1, 1);
            bool b1 = true;

            session.SetFldValue(objId, TestingFolder.Fields.TestIntegerField, i1.AsSpan());
            session.SetFldValue(objId, TestingFolder.Fields.TestDecimalField, d1.AsSpan());
            session.SetFldValue(objId, TestingFolder.Fields.TestDateField, t1.AsSpan());
            session.SetFldValue(objId, TestingFolder.Fields.TestBoolField, b1.AsSpan());

            session.Commit();

            folder.Name = "b";

            long i2 = 2;
            decimal d2 = 2;
            DateTime t2 = new DateTime(2010, 1, 1);
            bool b2 = false;

            session.SetFldValue(objId, TestingFolder.Fields.TestIntegerField, i2.AsSpan());
            session.SetFldValue(objId, TestingFolder.Fields.TestDecimalField, d2.AsSpan());
            session.SetFldValue(objId, TestingFolder.Fields.TestDateField, t2.AsSpan());
            session.SetFldValue(objId, TestingFolder.Fields.TestBoolField, b2.AsSpan());

            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);

        var commits = History.GetCommitsForObject(env, readSession.Store.ReadTransaction, objId).ToList();
        Assert.Equal(2, commits.Count);

        var commit = History.TryGetCommit(env, readSession.Store.ReadTransaction, commits[1]);
        Assert.NotNull(commit);

        var events = commit!.EventsByObject[objId];

        Assert.Contains(events, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.Name);
        Assert.Contains(events, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.TestIntegerField);
        Assert.Contains(events, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.TestDecimalField);
        Assert.Contains(events, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.TestDateField);
        Assert.Contains(events, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.TestBoolField);
    }

    [Fact]
    public void History_Object_Delete_Records_Assoc_Removals_Without_Field_Noise()
    {
        var testModel = ProjectModel.CreateFromDirectory("TestModel");
        using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());

        Guid aId;
        Guid bId;

        using (var session = new DbSession(env))
        {
            var a = new TestingFolder(session);
            var b = new TestingFolder(session);

            aId = a.ObjId;
            bId = b.ObjId;

            a.Parent = b;
            session.Commit();

            session.DeleteObj(bId);
            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);

        var commitsA = History.GetCommitsForObject(env, readSession.Store.ReadTransaction, aId).ToList();
        var commitsB = History.GetCommitsForObject(env, readSession.Store.ReadTransaction, bId).ToList();

        Assert.Equal(2, commitsA.Count);
        Assert.Equal(2, commitsB.Count);

        var deleteCommitA = History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsA[1]);
        var deleteCommitB = History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsB[1]);
        Assert.NotNull(deleteCommitA);
        Assert.NotNull(deleteCommitB);

        Assert.Contains(deleteCommitA!.EventsByObject[aId], e => e.Type == HistoryEventType.AsoRemoved);

        var bEvents = deleteCommitB!.EventsByObject[bId];
        Assert.Contains(bEvents, e => e.Type == HistoryEventType.ObjDeleted);
        Assert.DoesNotContain(bEvents, e => e.Type == HistoryEventType.FldChanged);
    }
}
