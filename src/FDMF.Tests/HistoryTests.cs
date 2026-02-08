using System.Runtime.InteropServices;
using FDMF.Core.DatabaseLayer;
using FDMF.Tests.TestModelModel;
using Environment = FDMF.Core.Environment;

namespace FDMF.Tests;

[Collection(DatabaseCollection.DatabaseCollectionName)]
public sealed class HistoryTests
{
    [Fact]
    public void History_Commits_Can_Be_Enumerated()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());

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
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());

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

        Assert.Contains(addCommitA.EventsByObject[aId], e => e.Type == HistoryEventType.AsoAdded);
        Assert.Contains(addCommitB.EventsByObject[bId], e => e.Type == HistoryEventType.AsoAdded);

        var removeCommitA = History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsA[1]);
        var removeCommitB = History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsB[1]);
        Assert.NotNull(removeCommitA);
        Assert.NotNull(removeCommitB);

        Assert.Contains(removeCommitA.EventsByObject[aId], e => e.Type == HistoryEventType.AsoRemoved);
        Assert.Contains(removeCommitB.EventsByObject[bId], e => e.Type == HistoryEventType.AsoRemoved);
    }

    [Fact]
    public void History_Field_Delete_Is_Recorded_As_Defaulting()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());

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

        var events = commit.EventsByObject[objId];
        var fldEvent = Assert.Single(events, e => e.Type == HistoryEventType.FldChanged);

        Assert.Equal(TestingFolder.Fields.TestIntegerField, fldEvent.FldId);
        Assert.True(fldEvent.OldValue.Length > 0);
        Assert.Empty(fldEvent.NewValue);
    }

    [Fact]
    public void History_Records_Field_Changes_For_All_Types()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());

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

        var events = commit.EventsByObject[objId];

        Assert.Contains(events, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.Name);
        Assert.Contains(events, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.TestIntegerField);
        Assert.Contains(events, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.TestDecimalField);
        Assert.Contains(events, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.TestDateField);
        Assert.Contains(events, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.TestBoolField);
    }

    [Fact]
    public void History_Object_Delete_Records_Assoc_Removals_Without_Field_Noise()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());

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


        Assert.Null(readSession.GetObjFromGuid<TestingFolder>(bId));
        Assert.Null(readSession.GetObjFromGuid<TestingFolder>(aId)!.Value.Parent);

        var commitsA = History.GetCommitsForObject(env, readSession.Store.ReadTransaction, aId).ToList();
        var commitsB = History.GetCommitsForObject(env, readSession.Store.ReadTransaction, bId).ToList();

        Assert.Equal(2, commitsA.Count);
        Assert.Equal(2, commitsB.Count);

        var deleteCommitA = History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsA[1]);
        var deleteCommitB = History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsB[1]);
        Assert.NotNull(deleteCommitA);
        Assert.NotNull(deleteCommitB);

        var aEvents = deleteCommitA.EventsByObject[aId];
        Assert.Contains(aEvents, e => e.Type == HistoryEventType.AsoRemoved && e.FldId == TestingFolder.Fields.Parent);

        var bEvents = deleteCommitB.EventsByObject[bId];
        Assert.Contains(bEvents, e => e.Type == HistoryEventType.ObjDeleted);
        Assert.DoesNotContain(bEvents, e => e.Type == HistoryEventType.FldChanged);
    }

    [Fact]
    public void History_Multiple_Changes_In_Single_Commit_Are_Recorded()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());

        Guid aId;
        Guid bId;
        Guid childId;

        using (var session = new DbSession(env))
        {
            var a = new TestingFolder(session);
            var b = new TestingFolder(session);
            var child = new TestingFolder(session);

            aId = a.ObjId;
            bId = b.ObjId;
            childId = child.ObjId;

            // 3 fields
            a.Name = "multi";

            long i = 7;
            bool bb = true;
            session.SetFldValue(aId, TestingFolder.Fields.TestIntegerField, i.AsSpan());
            session.SetFldValue(aId, TestingFolder.Fields.TestBoolField, bb.AsSpan());

            // 2 associations affecting object A (Parent and Subfolders)
            a.Parent = b;
            child.Parent = a;

            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);

        var commitsA = History.GetCommitsForObject(env, readSession.Store.ReadTransaction, aId).ToList();
        Assert.Single(commitsA);

        var commit = History.TryGetCommit(env, readSession.Store.ReadTransaction, commitsA[0]);
        Assert.NotNull(commit);

        var eventsA = commit.EventsByObject[aId];

        Assert.Contains(eventsA, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.Name);
        Assert.Contains(eventsA, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.TestIntegerField);
        Assert.Contains(eventsA, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.TestBoolField);

        Assert.Contains(eventsA, e => e.Type == HistoryEventType.AsoAdded && e.FldId == TestingFolder.Fields.Parent && e.ObjBId == bId);
        Assert.Contains(eventsA, e => e.Type == HistoryEventType.AsoAdded && e.FldId == TestingFolder.Fields.Subfolders && e.ObjBId == childId);
    }

    [Fact]
    public void History_Last_Write_Wins_Within_A_Commit_For_A_Field()
    {
        using var env = Environment.CreateDatabase(dbName: DatabaseCollection.GetTempDbDirectory(), dumpFile: DatabaseCollection.GetTestModelDumpFile());

        Guid objId;

        using (var session = new DbSession(env))
        {
            var folder = new TestingFolder(session);
            objId = folder.ObjId;

            long v1 = 1;
            long v2 = 2;

            session.SetFldValue(objId, TestingFolder.Fields.TestIntegerField, v1.AsSpan());
            session.SetFldValue(objId, TestingFolder.Fields.TestIntegerField, v2.AsSpan());

            session.Commit();
        }

        using var readSession = new DbSession(env, readOnly: true);

        var commits = History.GetCommitsForObject(env, readSession.Store.ReadTransaction, objId).ToList();
        Assert.Single(commits);

        var commit = History.TryGetCommit(env, readSession.Store.ReadTransaction, commits[0]);
        Assert.NotNull(commit);

        var events = commit.EventsByObject[objId];
        var fldEvent = Assert.Single(events, e => e.Type == HistoryEventType.FldChanged && e.FldId == TestingFolder.Fields.TestIntegerField);

        // New value should be the last write.
        Assert.Equal(2, MemoryMarshal.Read<long>(fldEvent.NewValue));
    }

    //Todo, GuidV7 are not correctly ordered in all cases, we need to switch to our own id, in the mean time, we have this test commented out

    // [Fact]
    // public async Task History_All_Commits_Are_Time_Ordered()
    // {
    //     var testModel = ProjectModel.CreateFromDirectory("TestModel");
    //     using var env = Environment.Create(testModel, dbName: DatabaseCollection.GetTempDbDirectory());
    //
    //     Guid objId;
    //
    //     using (var session = new DbSession(env))
    //     {
    //         var folder = new TestingFolder(session);
    //         objId = folder.ObjId;
    //
    //         folder.Name = "1";
    //         session.Commit();
    //
    //         await Task.Delay(2);
    //
    //         folder.Name = "2";
    //         session.Commit();
    //
    //         await Task.Delay(2);
    //
    //         folder.Name = "3";
    //         session.Commit();
    //     }
    //
    //     using var readSession = new DbSession(env, readOnly: true);
    //
    //     var commits = History.GetAllCommits(env, readSession.Store.ReadTransaction).ToList();
    //     Assert.True(commits.Count >= 3);
    //
    //     // Verify timestamps are non-decreasing in enumeration order.
    //     for (int i = 1; i < commits.Count; i++)
    //     {
    //         Assert.True(commits[i].TimestampUtc >= commits[i - 1].TimestampUtc);
    //     }
    //
    //     var objCommits = History.GetCommitsForObject(env, readSession.Store.ReadTransaction, objId).ToList();
    //     Assert.Equal(3, objCommits.Count);
    //
    //     var c0 = History.TryGetCommit(env, readSession.Store.ReadTransaction, objCommits[0]);
    //     var c1 = History.TryGetCommit(env, readSession.Store.ReadTransaction, objCommits[1]);
    //     var c2 = History.TryGetCommit(env, readSession.Store.ReadTransaction, objCommits[2]);
    //     Assert.NotNull(c0);
    //     Assert.NotNull(c1);
    //     Assert.NotNull(c2);
    //
    //     Assert.True(c1.TimestampUtc >= c0.TimestampUtc);
    //     Assert.True(c2.TimestampUtc >= c1.TimestampUtc);
    // }
}
