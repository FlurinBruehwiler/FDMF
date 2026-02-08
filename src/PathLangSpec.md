# Access Management (Path Rights)

This document specifies the access management system and the PathLang language used to define access predicates ("path rights").

## Overview

- A *path right* is a named predicate `P(input) -> bool`.
- Predicates are evaluated for a concrete input object (ObjId) in a database session.
- Evaluation is a constrained graph search/walk: the predicate defines which associations may be traversed, and which node/field constraints must hold.
- Predicates can be composed (AND/OR), can call other predicates, and can express recursion/repetition.

### Targets and reuse

A predicate may be used in different contexts, e.g.

- `Viewable(X)`: user may view object X
- `Editable(X)`: user may edit object X

Some predicates target the *current user* ("does there exist a path from X to the current user?").
Other predicates target a *property of some reachable object* ("does there exist a path from X to an object with `Visible=true`?").
This means the predicate itself always returns a boolean; the "target" is expressed by the last filter(s) and/or predicate calls.

### Core constraints (for performance and caching)

PathLang is intentionally restricted so evaluation can be cached:

- Evaluation is pure with respect to: `(predicate, inputObjId, currentUserObjId)`.
- Later steps must not depend on earlier path state (no backreferences to previous nodes).
- This enables memoization of sub-evaluations like `(predicateId, objId)`.

## Data model assumptions

- Objects are connected by typed associations (edges). Each association traversal yields 0..N next objects.
- Objects have fields (scalar values).
- Objects have a runtime type (TypId). PathLang can use a type guard to constrain a node.

## Syntax (EBNF)

Whitespace may appear between tokens.

```
program         = { predicateDef } ;

predicateDef    = ident, "(", typeName, ")", ":", expr ;

expr            = orExpr ;

orExpr          = andExpr, { "OR", andExpr } ;
andExpr         = term,   { "AND", term } ;

term            = pathExpr
                | "(", expr, ")"
                | predicateCall
                | repeatExpr
                ;

// Note: a filter can also be applied directly to the source, which is equivalent
// to a pathExpr with zero "->" steps and a trailing filter:
// this[$.Visible=true]


pathExpr        = sourceExpr, { "->", step } , [ filter ] ;

sourceExpr      = "this" | "$" ;

step            = ident, [ filter ] ;

filter          = "[", condition, "]" ;

condition       = condOr ;
condOr          = condAnd, { "OR", condAnd } ;
condAnd         = condAtom, { "AND", condAtom } ;

condAtom        = fieldCompare | "(", condition, ")" ;

fieldCompare    = [ typeGuard, "." ], ident, compareOp, literal ;

// type guard applies to the current node '$'
// examples: $(Person).CurrentUser=true
//           $(User).Id="..."
// NOTE: only type guards on '$' are supported in v1.
typeGuard       = "$(", typeName, ")" ;

compareOp       = "=" | "!=" ;

predicateCall   = ident, "(", argExpr, ")" ;
argExpr         = "$" | "this" ;

repeatExpr      = "repeat", "(", pathExpr, ")" ;

literal         = boolean | number | string ;
boolean         = "true" | "false" ;
number          = digit, { digit }, [ ".", digit, { digit } ] ;
string          = '"', { charNoQuote }, '"' ;

ident           = letter, { letter | digit | "_" } ;
typeName        = ident ;
```

## Semantics

### Predicate definition

`P(T): <expr>` defines a predicate named `P` with input type `T`.

- The type name is a schema-level name used for validation / tooling.
- Runtime evaluation uses ObjIds; the evaluator may optionally validate that the runtime type matches `T`.

### Evaluation model (existential)

PathLang expressions are evaluated as "existence of a successful path":

- A path expression like `this->A->B[cond]` succeeds if there exists at least one sequence of traversals that reaches a node where all encountered filters succeed.
- If a traversal yields multiple targets, evaluation behaves like `exists` over those targets.

### `this` and `$`

- `this` is the predicate input object (root).
- `$` is the current node of the current evaluation step.
- In filters, field access is always relative to `$`.

### Traversal

`X->AssocName[filter]`:

- Evaluate `X` to a starting node set.
- For each node in that set, traverse association `AssocName` to produce next nodes.
- If a filter is present, keep only nodes for which the filter condition returns true.
- The expression succeeds if any resulting node leads to success for the remainder of the chain.

### Filters / conditions

A filter `[cond]` is evaluated on each candidate node `$` produced by the preceding traversal step.

`Field = literal`:

- Reads scalar field `Field` from `$`.
- Compares it to the literal.

Type guard: `$(TypeName).Field = literal`:

- First checks that `$` is of runtime type `TypeName`.
- If the type guard fails, the condition is false.
- If the type guard succeeds, the field compare is evaluated.

Boolean composition inside filters:

- `[A AND B]` requires both to be true.
- `[A OR B]` requires at least one to be true.

### Predicate calls

`OtherPredicate($)` evaluates `OtherPredicate` for the argument object.

- This is the main reuse mechanism.
- The called predicate may be a "user target" predicate or any other predicate.

### Logical composition at expression level

`E1 AND E2`:

- Both subexpressions must succeed.
- Semantics are existential within each subexpression; AND combines the boolean results.

`E1 OR E2`:

- At least one subexpression must succeed.

Operator precedence:

- `AND` binds stronger than `OR`.
- Parentheses override precedence.

### Repeat

`this->repeat(->Parent)->SomeAssoc` is intended for recursive traversal.

Repeat is a kind of path segment:
- The input needs to be the same as the output. 

- Semantics: repeatedly apply the inner path expression zero or more times.
- v1 restriction: `repeat` must contain a path expression whose last step yields a single association traversal.
- The evaluator must include cycle detection (visited set) to avoid infinite loops.

## Examples

The examples below are meant to clarify both syntax and intent. They also highlight the
"direction" of the system: reusable predicates, type guards for unions, and composition.

### Current user reachability (type guard)

"A document is viewable if you can reach the current user via known ownership edges."

```
OwnerCanView(Document): this->Business->Owners[$(Person).CurrentUser=true]
```

Notes:
- `Owners` may be a union (e.g. `Person | OrgUnit | ...`). The type guard `$(Person)` ensures only `Person` matches.

### Computed predicates vs field checks

`Visible`/`Viewable`/`Editable` are *computed predicates*, not intrinsic fields.

- Field checks always use the `$.FieldName` form.
- Predicate checks always use a predicate call like `Visible($)`.

So this is a field check:

```
HasTitle(Document): this[$.Title="Hello"]
```

And this is a predicate check (computed visibility):

```
IsVisible(Any): Visible(this)=true
```

### Predicate calls / composition

Predicates can call other predicates to enable reuse and composition.

Pattern A: call on the root

```
CanEdit(Document): Editable(this)=true
```

Pattern B: call inside a traversal filter

```
TaskViewable(Task): this->Document[Viewable($)=true]
```

Pattern C: combine predicates

```
CanEdit(Document):
    Viewable(this)=true
    AND
    Editable(this)=true
```

### Predicate reuse (boolean composition)

"An attachment is viewable when its session is visible OR the user is explicitly granted access."

```
CanView(Attachment):
    this->AgendaItem->Session[
        Visible($)=true
    ]
    OR
    this->ExplicitViewers[$(Person).CurrentUser=true]
```

### AND composition (must satisfy both)

"Editable requires viewable AND not locked."

```
CanEdit(Document):
    CanView(this)=true
    AND
    this[$.Locked=false]
```

### Multi-step with intermediate filter

"A document is viewable if its business is active and has the current user as an owner."

```
CanView(Document):
    this
        ->Business[$.State="Active"]
        ->Owners[$(Person).CurrentUser=true]
```

### Calling a predicate on a reached node

"A task is editable if the referenced document is editable."

```
TaskEditable(Task): this->Document[CanEdit($)=true]
```

### Recursion / repeat (hierarchies)

"A user can view an org unit if they are a member of this org unit or any parent org unit."

```
CanViewOrgUnit(OrgUnit):
    repeat(this->Parent)->Members[$(Person).CurrentUser=true]
```

### Reuse + repeat + OR (typical "real world" rule)

"A document is viewable if either:
- it is visible via its session visibility, OR
- the current user is an owner via the business graph, OR
- the current user is explicitly granted view rights.

This shows how PathLang rules can stay readable while still being composable.

```
CanView(Document):
    (
        this->AgendaItem->Session[Visible($)=true]
        OR
        OwnerCanView(this)=true
        OR
        this->ExplicitViewers[$(Person).CurrentUser=true]
    )
```

### Notes on formatting

PathLang is whitespace-insensitive, so the examples use newlines and indentation for readability.
In storage and tooling, you can keep it single-line if desired.

## Non-goals (v1)

- No arbitrary user-defined functions besides predicate calls.
- No access to "previous" nodes in the current path.
- No side effects.
- No mutation.
