# TODO

This file describes the next steps that need to be taken.

The codebase should be divided into layers, they should have clear responsibility

## Phase 1 (Database Layer)

In Phase 1 we want to implement a database. This includes the following points.

- Schema Definition
- Schema Code Generation
- Transactions
- Indexes/Searching

- Add more FLD data types (guid! / enum) / think about how we are going to handle custom data types
- implement access management (path lang)
- add a user to the base model / have the concept of the current user
- add inheritance (multi?)
- add unions

## Phase 2 (Networking Layer)

In Phase 2 we implement the communication between the client and the server

This includes the following points:

- RPC
- ObjectSync
- Serializing and executing LINQ queries

## Phase 3 (Business Layer)

- SaveProcess
  - Save Actions
  - Validator

- Signals / Computed values

## Phase 4 (UI Layer)
This depends upon the release of pangui
