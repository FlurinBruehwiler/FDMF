# Scalability

The idea is to have two types. One of them is does use graph db the other one doesnt (or when ur really needs data (like some data, it can fetch it (like the client).

So in the extreme case we have
- n clients
- n non data dependant servers
- n data dependant servers (non authoritive)
- a single authoritive server

But all of this code can also run in a single process!

Much of the Business Logic should be donw in reloadable code (separate assemblyLoadContex, WASM, separate process) but still have access (eg shared memory) to the database.

The push service, which updates the client db, can be considered non data dependant, in general we should strive to have as much of the code as possible in the non data dependant server.