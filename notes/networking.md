if we are on the server....
if the client doesn't already have this obj, we send it (it being all the fields, and single reference fields)
maybe we could have an api like this: client.InformHasObj

note: it is important that the updates to the client are being received in order, i.e. when the server updates a field of an obj in a request, then returns the obj, the client needs to have received the field update before the request completes....
note: do the client/server need to be able to share a transaction?? i.e. can we send non commited objs over the network
    I think the answer should be yes: the question is, at what point do we send the changes within a transaction over the network?
    What even is a transaction in the context of a client? Should a client allways just have a single transaction?
    Well no, because at some point we want to do a "Save" and at that point all the validators run.
    This means that only valid states can be commited.
    The problem obviously beeing that if two transactions are running concurrently, each of them might be in a valid state, but after both are merged, the system is in an invalid state.
    So it is clear that we need a lock on the save process, only one transaction can be saved at the same time.
    Now the question is, in LMDB, can i update a transaction from the "main" dataset?


we know the objId, if

    [MemoryPackOnDeserializing]
    public static void OnDeserializing(ref MemoryPackReader reader, ref Folder value)
    {
        value._transaction = (Transaction)reader.Options.ServiceProvider.GetService(typeof(Transaction));
    }

    [MemoryPackOnSerializing]
    static void OnSerializing<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, ref Folder? value)
        where TBufferWriter : IBufferWriter<byte> // .NET Standard 2.1, use where TBufferWriter : class, IBufferWriter<byte>
    {
        if (value is {} folder)
        {
            var objId = folder._objId;

        }
    }
