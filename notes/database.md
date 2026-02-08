# Database

## Introduction
TODO

## Sessions
We need some kind of construct on top of a LMDB transaction, we will call it session. There can't be multiple write LMDB transactions at the same time, 
but we need it. 

The idea is to have a separate in memory B+  tree that stores the temporary dataset of the sessions.  

When querring the session, 
we first query the dataset and if the value is not in the dataset we query the main database. 

Once we want to commit a session, we open a write transaction onto the db and write in all the changes of the dataset. 

In the future, when we have multiple replicas of the database, we also send this changeset across the wire. 

## Searching

We have a flag on each field that defines if an index should be created for it.
Indexes are just separate LMDB Databases. Maybe we can even combine them into a single db, 
for example the key could consist of [fldId + value] and the value is then the list of objGuids whose field has this value.
This way we only use a single database for all indexes. We also need to have a session system ontop of this index system, 
so when the user does queries something within a _write_ session, we also search through the changes, 
but we don't need an index for them, as the change set it is generally quite small.

TODO: fulltext search

## Saving


1. Acquire lock
2. Open an LMDB write transaction
3. Apply the changes from the changset
4. Execute all SaveActions
5. Execute all Validators
6. If no validator reported an error, we commit to LMDB.
7. Release lock

## IDs
Currently we use Guids everywhere for IDs, they are used for ObjIds and for Model IDs. The problem is that Guids are 16 Bytes and therefore make the DB much larger than it needs to be. We should consider switching to a new system, especially for the Model IDs. We should switch to some 8-Bit ID, something similar to https://en.wikipedia.org/wiki/Snowflake_ID.
