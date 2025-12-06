# Database

## Introduction
TODO

## Sessions
We need some kind of construct on top of a LMDB transaction, we will call it session. There can't be multiple write LMDB transactions at the same time, 
but we need it. 

The idea is to have a separate LMDB DB that stores the temporary dataset of the sessions.  

When querring the session, 
we first query the dataset and if the value is not in the dataset we query the main database. 
Once we want to commit a session, we open a write transaction onto the db and write in all the changes of the dataset. 
In the future, when we have multiple replicas of the database, we also send this changeset across the wire. 

## Searching

We have a flag on each field that defines if an index should be created for it.
Indexes are just separate LMDB Databases. Maybe we can even combine them into a single db, 
for example the key could consist of [fldId + value] and the value is then the list of objGuids whose field has this value.
This way we only use a single database for all indexes. We also need to have a session system ontop of this index system, 
as we want to be able to search within the changes of a session. This means our session system should be general enough to work for 
the obj and idx databases.

TODO: fulltext search
