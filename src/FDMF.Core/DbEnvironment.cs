using System.Runtime.InteropServices;
using System.Text;
using FDMF.Core.DatabaseLayer;
using LightningDB;

namespace FDMF.Core;

public sealed class DbEnvironment : IDisposable
{
    public required LightningEnvironment LightningEnvironment;
    public required LightningDatabase ObjectDb;
    public required LightningDatabase HistoryDb;
    public required LightningDatabase HistoryObjIndexDb;
    public required LightningDatabase StringSearchIndex;
    public required LightningDatabase NonStringSearchIndex;
    public required LightningDatabase FieldPresenceIndex;
    public required Guid ModelGuid;
    public required string DbDir;

    public static DbEnvironment CreateDatabase(string dbName, string dumpFile = "")
    {
        // NOTE: This is intentionally destructive and used by tests/dev.
        if (Directory.Exists(dbName))
        {
            Directory.Delete(dbName, recursive: true);
        }

        var env = OpenInternal(dbName, create: true);

        ImportMetaModel(env);

        //import base model
        using (var session = new DbSession(env))
        {
            env.ModelGuid = JsonDump.FromJson(EmbeddedResources.BaseModel, session).ObjId;
            session.Commit();
        }

        if (dumpFile != "")
        {
            // Importing a dump can be large; use a larger arena so the initial commit
            // (history/search index) has enough space.
            using var session = new DbSession(env, arenaSize: 5_000_000);
            if (File.Exists(dumpFile))
            {
                var json = File.ReadAllText(dumpFile);
                var model = JsonDump.FromJson(json, session);
                session.Commit();
                env.ModelGuid = model.ObjId;
            }
            else
            {
                //todo create helpers that read a file, and otherwise log
                Logging.Log(LogFlags.Error, $"File {dumpFile} doesn't exist");
            }
        }

        return env;
    }

    public static DbEnvironment Open(string dbDir)
    {
        if (!Directory.Exists(dbDir))
            throw new DirectoryNotFoundException($"Database directory '{dbDir}' does not exist");

        return OpenInternal(dbDir, create: false);
    }

    private static DbEnvironment OpenInternal(string dbDir, bool create)
    {
        Directory.CreateDirectory(dbDir);
        var lightningEnv = new LightningEnvironment(dbDir, new EnvironmentConfiguration
        {
            MaxDatabases = 128,
            MapSize = 10 * 1024 * 1024, // 500 MB
        });
        lightningEnv.Open(EnvironmentOpenFlags.NoThreadLocalStorage);

        using var lightningTransaction = lightningEnv.BeginTransaction();

        var createFlag = create ? DatabaseOpenFlags.Create : 0;

        var objDb = lightningTransaction.OpenDatabase(null, new DatabaseConfiguration
        {
            Flags = createFlag
        });

        var histDb = lightningTransaction.OpenDatabase(name: "HistoryDb", new DatabaseConfiguration
        {
            Flags = createFlag
        });

        var historyObjIndexDb = lightningTransaction.OpenDatabase(name: "HistoryObjIndexDb", new DatabaseConfiguration
        {
            Flags = createFlag | DatabaseOpenFlags.DuplicatesSort
        });

        var stringSearchIndex = lightningTransaction.OpenDatabase(name: "StringIndexDb", new DatabaseConfiguration
        {
            Flags = createFlag | DatabaseOpenFlags.DuplicatesSort
        });

        // We could combine string and nonstring index dbs. They're separate for now since custom comparers may be slower.
        var customComparer = new DatabaseConfiguration
        {
            Flags = createFlag | DatabaseOpenFlags.DuplicatesSort,
        };
        customComparer.CompareWith(new CustomIndexComparer());

        var nonStringSearchIndex = lightningTransaction.OpenDatabase(name: "NonStringIndexDb", customComparer);
 
        var fieldPresenceIndex = lightningTransaction.OpenDatabase(name: "FieldPresenceIndexDb", new DatabaseConfiguration
        {
            Flags = createFlag | DatabaseOpenFlags.DuplicatesSort
        });

        lightningTransaction.Commit();
 
        var env = new DbEnvironment
        {
            LightningEnvironment = lightningEnv,
            ObjectDb = objDb,
            HistoryDb = histDb,
            HistoryObjIndexDb = historyObjIndexDb,
            StringSearchIndex = stringSearchIndex,
            NonStringSearchIndex = nonStringSearchIndex,
            FieldPresenceIndex = fieldPresenceIndex,
            ModelGuid = Guid.Empty, //todo
            DbDir = dbDir
        };
        
        return env;
    }

    private static void ImportMetaModel(DbEnvironment dbEnvironment)
    {
        using var session = new DbSession(dbEnvironment);

        // Stable id for the built-in base model instance.
        var baseModelId = Guid.Parse("5270c4a5-71e0-4474-9f94-50745b29b404");
        dbEnvironment.ModelGuid = baseModelId;

        // If it's already present, do nothing.
        if (session.GetTypId(baseModelId) != Guid.Empty)
            return;

        // Entity ids (and also the TypIds for their corresponding objects)
        var typModel = Guid.Parse("5739AE0D-E179-4D46-BAE2-081F99D699DA");
        var typEntityDefinition = Guid.Parse("c15f876f-4f74-4034-9acb-03bc3b521e81");
        var typFieldDefinition = Guid.Parse("42a6f33d-a938-4ad8-9682-aabdc92a53d2");
        var typReferenceFieldDefinition = Guid.Parse("2147ed0f-b37d-4429-a3f8-8312c1620383");
        var typEnumDefinition = Guid.Parse("9daec113-7ff2-4c52-8ba1-3d400ac39b7b");

        // FieldDataTypeEnum
        var fieldDataTypeEnum = Guid.Parse("1c65eb40-1820-40e7-8ddc-53618d56ea9b");

        // Model (instance) fields
        var m_fld_name = Guid.Parse("efe4fee5-bbca-4b19-8c26-c96ba8b2c008");
        var m_aso_entityDefinitions = Guid.Parse("06e78b99-4b0d-45d7-b0c8-cf57c73fc3e8");

        // EntityDefinition fields
        var ed_fld_key = Guid.Parse("95f47b2f-a39e-4031-b12f-f30e0762d671");
        var ed_fld_name = Guid.Parse("4546ebea-3552-4198-8730-89182dc91a34");
        var ed_fld_id = Guid.Parse("16800d40-9d5c-41c6-8642-bb956def6e17");

        // EntityDefinition reference fields
        var ed_aso_model = Guid.Parse("d2b98e24-5786-4502-b2cf-5dea14097765");
        var ed_aso_fieldDefinitions = Guid.Parse("8dc642e4-af58-403a-bed2-ce41baa95b21");
        var ed_aso_referenceFieldDefinitions = Guid.Parse("06950fac-de4f-487e-aa78-7095909805e4");
        // var ed_aso_parents = Guid.Parse("15928c28-398d-4caa-97d1-7c01e9020d9f");
        // var ed_aso_children = Guid.Parse("232c2342-682d-4526-a5fd-f943830d7bef");

        // FieldDefinition fields
        var fd_fld_key = Guid.Parse("8a5d90d2-1b1c-428e-a010-9ae7d603dd30");
        var fd_fld_name = Guid.Parse("c2ef8935-fcaf-4b33-aa31-59014a4c6765");
        var fd_fld_id = Guid.Parse("f91613e2-a9f9-4e3b-96e3-38650319dc0c");
        var fd_fld_dataType = Guid.Parse("b4f55a8c-6f73-41bc-b051-cbaeb18b0390");
        var fd_fld_isIndexed = Guid.Parse("2e699a43-98b9-42f7-a26c-20256305b7cb");

        // FieldDefinition reference fields
        var fd_aso_owningEntity = Guid.Parse("f097fe25-1d11-47b3-a02c-0072a781a528");
        var fd_aso_enumRef = Guid.Parse("ec74c01e-86b2-4334-b8cb-d800aaa827ae");

        // ReferenceFieldDefinition fields
        var rfd_fld_key = Guid.Parse("450c77f2-73ff-4125-a879-b75840275c99");
        var rfd_fld_name = Guid.Parse("32734a0c-eba5-4b97-80de-081857b356cc");
        var rfd_fld_id = Guid.Parse("eff45530-7e4e-401f-a53e-094f0d815e8a");
        var rfd_fld_refType = Guid.Parse("924ee396-a747-4790-a9da-051e3e8676a8");

        // ReferenceFieldDefinition reference fields
        var rfd_aso_owningEntity = Guid.Parse("500e3a9c-c469-4585-a7c4-e307dc295d88");
        var rfd_aso_otherReferenceFields = Guid.Parse("50ac96bd-7082-4821-95fa-57e9040246ab");

        // EnumDefinition reference fields
        var enumD_aso_fields = Guid.Parse("ec0d2deb-efe4-4620-9e77-92df907a5024");
        var enumD_fld_name = Guid.Parse("30a6d113-3c2a-45a6-b035-4d4ad6987dd7");
        var enumD_fld_variants = Guid.Parse("73b66d67-22c3-4a72-8309-d5ab81d483bc");

        var enum_fldDatatype = Guid.Parse("f2c24ac5-23bc-4a81-b7e7-d58928e8835a");
        var enum_asoRefType = Guid.Parse("daeb5769-8263-4444-ad3a-8d7a5ed4df61");

        static void SetGuid(DbSession s, Guid objId, Guid fldId, Guid value)
        {
            s.SetFldValue(objId, fldId, value.AsSpan());
        }

        static void SetString(DbSession s, Guid objId, Guid fldId, string value)
        {
            s.SetFldValue(objId, fldId, Encoding.Unicode.GetBytes(value));
        }

        static void SetBool(DbSession s, Guid objId, Guid fldId, bool value)
        {
            s.SetFldValue(objId, fldId, value.AsSpan());
        }

        void CreateEntityDefinition(Guid entityId, string key)
        {
            session.CreateObj(typEntityDefinition, entityId);
            SetString(session, entityId, ed_fld_key, key);
            SetString(session, entityId, ed_fld_name, key);
            SetGuid(session, entityId, ed_fld_id, entityId);

            // Link entity definition to the base model instance.
            session.CreateAso(entityId, ed_aso_model, baseModelId, m_aso_entityDefinitions);
        }

        void CreateFieldDefinition(Guid fieldId, string key, FieldDataType fieldDataType, bool isIndexed, Guid owningEntity, Guid enumDefinition = default)
        {
            session.CreateObj(typFieldDefinition, fieldId);
            SetString(session, fieldId, fd_fld_key, key);
            SetString(session, fieldId, fd_fld_name, key);
            SetGuid(session, fieldId, fd_fld_id, fieldId);
            session.SetFldValue(fieldId, fd_fld_dataType, fieldDataType.AsSpan());
            SetBool(session, fieldId, fd_fld_isIndexed, isIndexed);

            // Link field definition to owning entity definition.
            session.CreateAso(fieldId, fd_aso_owningEntity, owningEntity, ed_aso_fieldDefinitions);

            // Link to EnumDefinition for enum-typed scalar fields.
            if ((fieldDataType == FieldDataType.Enum) != (enumDefinition != Guid.Empty))
                throw new Exception();

            if (fieldDataType == FieldDataType.Enum)
                session.CreateAso(fieldId, fd_aso_enumRef, enumDefinition, enumD_aso_fields);
        }

        void CreateReferenceFieldDefinition(Guid refFieldId, string key, RefType refType, Guid owningEntity, Guid otherRefFieldId)
        {
            session.CreateObj(typReferenceFieldDefinition, refFieldId);
            SetString(session, refFieldId, rfd_fld_key, key);
            SetString(session, refFieldId, rfd_fld_name, key);
            SetGuid(session, refFieldId, rfd_fld_id, refFieldId);
            var refTypeOrdinal = (int)refType;
            session.SetFldValue(refFieldId, rfd_fld_refType, refTypeOrdinal.AsSpan());

            // Link ref field definition to owning entity definition.
            session.CreateAso(refFieldId, rfd_aso_owningEntity, owningEntity, ed_aso_referenceFieldDefinitions);

            // Link to opposite reference field definition.
            session.CreateAso(refFieldId, rfd_aso_otherReferenceFields, otherRefFieldId, rfd_aso_otherReferenceFields);
        }

        void CreateEnumDefinition(Guid definitionGuid, string name, string variants)
        {
            session.CreateObj(typEnumDefinition, definitionGuid);
            SetString(session, definitionGuid, enumD_fld_name, name);
            SetString(session, definitionGuid, enumD_fld_variants, variants);
        }

        // 1) Create the base model instance.
        session.CreateObj(typModel, baseModelId);
        SetString(session, baseModelId, m_fld_name, "BaseModel");

        // 2) Create entity definitions.
        CreateEntityDefinition(typModel, "Model");
        CreateEntityDefinition(typEntityDefinition, "EntityDefinition");
        CreateEntityDefinition(typFieldDefinition, "FieldDefinition");
        CreateEntityDefinition(typReferenceFieldDefinition, "ReferenceFieldDefinition");
        CreateEntityDefinition(typEnumDefinition, "EnumDefinition");

        // 3) Create field definitions (scalars) for each entity.
        // Model
        CreateFieldDefinition(m_fld_name, "Name", FieldDataType.String, false, typModel);

        // EntityDefinition
        CreateFieldDefinition(ed_fld_key, "Key", FieldDataType.String, false, typEntityDefinition);
        CreateFieldDefinition(ed_fld_name, "Name", FieldDataType.String, false, typEntityDefinition);
        CreateFieldDefinition(ed_fld_id, "Id", FieldDataType.Guid, false, typEntityDefinition);

        CreateEnumDefinition(enum_fldDatatype, "FieldDataType", "Integer,Decimal,String,DateTime,Boolean,Enum,Guid");

        // FieldDefinition
        CreateFieldDefinition(fd_fld_key, "Key", FieldDataType.String, false, typFieldDefinition);
        CreateFieldDefinition(fd_fld_name, "Name", FieldDataType.String, false, typFieldDefinition);
        CreateFieldDefinition(fd_fld_id, "Id", FieldDataType.Guid, false, typFieldDefinition);
        CreateFieldDefinition(fd_fld_dataType, "DataType", FieldDataType.Enum, false, typFieldDefinition, enumDefinition: enum_fldDatatype);
        CreateFieldDefinition(fd_fld_isIndexed, "IsIndexed", FieldDataType.Boolean, false, typFieldDefinition);

        CreateEnumDefinition(enum_asoRefType, "RefType", "SingleMandatory,SingleOptional,Multiple");

        // ReferenceFieldDefinition
        CreateFieldDefinition(rfd_fld_key, "Key", FieldDataType.String, false, typReferenceFieldDefinition);
        CreateFieldDefinition(rfd_fld_name, "Name", FieldDataType.String, false, typReferenceFieldDefinition);
        CreateFieldDefinition(rfd_fld_id, "Id", FieldDataType.Guid, false, typReferenceFieldDefinition);
        CreateFieldDefinition(rfd_fld_refType, "RefType", FieldDataType.Enum, false, typReferenceFieldDefinition, enum_asoRefType);

        // 4) Create reference field definitions.
        // ModelDefinition
        var rf_model_entityDefinitions = Guid.Parse("06e78b99-4b0d-45d7-b0c8-cf57c73fc3e8");
        var rf_entity_model = Guid.Parse("d2b98e24-5786-4502-b2cf-5dea14097765");
        var rf_model_importedModels = Guid.Parse("ca3470c1-cf36-415a-88c1-47c2700fc37d");
        var rf_model_importedBy = Guid.Parse("bff6be49-6aed-4998-91ab-28702a3e29b0");

        CreateReferenceFieldDefinition(rf_model_entityDefinitions, "EntityDefinitions", RefType.Multiple, typModel, rf_entity_model);
        CreateReferenceFieldDefinition(rf_model_importedModels, "ImportedModels", RefType.Multiple, typModel, rf_model_importedBy);
        CreateReferenceFieldDefinition(rf_model_importedBy, "ImportedBy", RefType.Multiple, typModel, rf_model_importedModels);

        // EntityDefinition
        var rf_entity_fieldDefinitions = Guid.Parse("8dc642e4-af58-403a-bed2-ce41baa95b21");
        var rf_entity_referenceFieldDefinitions = Guid.Parse("06950fac-de4f-487e-aa78-7095909805e4");
        //var rf_entity_parents = Guid.Parse("15928c28-398d-4caa-97d1-7c01e9020d9f");
        //var rf_entity_children = Guid.Parse("232c2342-682d-4526-a5fd-f943830d7bef");

        CreateReferenceFieldDefinition(rf_entity_model, "Model", RefType.SingleMandatory, typEntityDefinition, rf_model_entityDefinitions);
        CreateReferenceFieldDefinition(rf_entity_fieldDefinitions, "FieldDefinitions", RefType.Multiple, typEntityDefinition, fd_aso_owningEntity);
        CreateReferenceFieldDefinition(rf_entity_referenceFieldDefinitions, "ReferenceFieldDefinitions", RefType.Multiple, typEntityDefinition, rfd_aso_owningEntity);
        //CreateReferenceFieldDefinition(rf_entity_parents, "Parents", "Multiple", typEntityDefinition, rf_entity_children);
        //CreateReferenceFieldDefinition(rf_entity_children, "Children", "Multiple", typEntityDefinition, rf_entity_parents);

        // FieldDefinition (inverse for rf_entity_fieldDefinitions)
        CreateReferenceFieldDefinition(fd_aso_owningEntity, "OwningEntity", RefType.SingleMandatory, typFieldDefinition, rf_entity_fieldDefinitions);
        CreateReferenceFieldDefinition(fd_aso_enumRef, "Enum", RefType.SingleOptional, typFieldDefinition, enumD_aso_fields);

        // ReferenceFieldDefinition (owning entity + self-loop)
        CreateReferenceFieldDefinition(rfd_aso_owningEntity, "OwningEntity", RefType.SingleMandatory, typReferenceFieldDefinition, rf_entity_referenceFieldDefinitions);
        CreateReferenceFieldDefinition(rfd_aso_otherReferenceFields, "OtherReferenceFields", RefType.SingleMandatory, typReferenceFieldDefinition, rfd_aso_otherReferenceFields);

        // EnumDefinition
        CreateReferenceFieldDefinition(enumD_aso_fields, "FieldUsage", RefType.Multiple, typEnumDefinition, fd_aso_enumRef);
        CreateFieldDefinition(enumD_fld_name, "Name", FieldDataType.String, false, typEnumDefinition);
        CreateFieldDefinition(enumD_fld_variants, "Variants", FieldDataType.String, false, typEnumDefinition);

        // Finish
        session.Commit();

        dbEnvironment.ModelGuid = baseModelId;
    }

    public void Dispose()
    {
        ObjectDb.Dispose();
        HistoryDb.Dispose();
        HistoryObjIndexDb.Dispose();
        StringSearchIndex.Dispose();
        NonStringSearchIndex.Dispose();
        FieldPresenceIndex.Dispose();
        LightningEnvironment.Dispose();

        if(IsTesting)
            Directory.Delete(DbDir, true);
    }

    public static bool IsTesting = false;
}

public sealed class CustomIndexComparer : IComparer<MDBValue>
{
    public enum Comparison : byte
    {
        SignedLong,
        DateTime,
        Decimal,
        Boolean,
        Assoc,
        Type
    }

    public static int CompareStatic(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        // Key layout for NonStringIndexDb:
        // [0]   = Comparison
        // [1..] = 16-byte discriminator (fieldId for most indices, typId for type index)
        // [17..] = data payload (typed value / assoc bytes / empty for type index)
        if (a[0] != b[0])
            return a[0].CompareTo(b[0]);

        // Always include the 16-byte discriminator in ordering.
        var aDisc = a.Slice(1, 16);
        var bDisc = b.Slice(1, 16);
        var discCmp = BPlusTree.CompareLexicographic(aDisc, bDisc);
        if (discCmp != 0)
            return discCmp;

        var aData = a.Slice(1 + 16);
        var bData = b.Slice(1 + 16);

        var comparison = (Comparison)a[0];
        return comparison switch
        {
            Comparison.SignedLong => CompareGeneric<long>(aData, bData),
            Comparison.DateTime => CompareGeneric<DateTime>(aData, bData),
            Comparison.Decimal => CompareGeneric<decimal>(aData, bData),
            Comparison.Boolean => CompareGeneric<bool>(aData, bData),
            Comparison.Assoc => BPlusTree.CompareLexicographic(aData, bData),
            Comparison.Type => BPlusTree.CompareLexicographic(aData, bData),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public static int CompareGeneric<T>(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y) where T : unmanaged, IComparable<T>
    {
        return MemoryMarshal.Read<T>(x).CompareTo(MemoryMarshal.Read<T>(y));
    }

    public int Compare(MDBValue a, MDBValue b)
    {
        return CompareStatic(a.AsSpan(), b.AsSpan());
    }
}

public sealed class GuidComparer : IComparer<Guid>
{
    public int Compare(Guid x, Guid y)
    {
        return BPlusTree.CompareLexicographic(x.AsSpan(), y.AsSpan());
    }
}
