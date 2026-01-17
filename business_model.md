# Business Model

FMDB is the base layer, and on top there comes the business model. The entire business model can change at runtime without needing a restart.
The Code that is part of the business model, is reloadable (either through WASM, or through subprocesses). 

A Business Model consists of the following things
- EntityDefinitions
- Static/Default Entity Instances (With static ObjIds)
- Code

Models can import/inherit other Models. They are able to reference EntityDefinitions, so they can add fields, inherit from them and add references to them.
Each Model has an Identifier that is used as a namespace for the Entities/Fields. Each Model also has a version.

The Base Model defines:
- The User Entity
- The entities that are used to represent the model (this causes a self-referential problem in the base model, so we need to partially hardcode it)
- The code also just lives in the Entity Graph. Each code file is a simple Document that contains the code.

So the model itself also gets stored in the entity graph.

The Database consists of:
- The Entity Graph
- Indexes
- The history of the Entity Graph
- Cached Values?
- The Documents (Blob storage)

We need a logical database format, that can be used to export/import parts of the database. This has many usecases:
- Testing (Testdata can be stored in this format)
- Exporting/Importing models from on database into another one
- etc

Should this format be text based? Probably yes.

Right now we store the models in a JSON file, this needs to change. We want a basic cli that can be used to interact with the database, while we don't have a full GUI yet.
I'm not yet sure if we want a cli in the final product, but we probably do.

The Model is modeled like this:

- Model
    - Fields
    - ReferenceFields
    - ImportedModels
    - Code (List/Hierarchy of documents)
    - StaticEntities

The idea is to have to database schema, as normal data in the database. I'm not yet sure if this is a good idea.