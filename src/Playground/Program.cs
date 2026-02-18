//Wasi Tests

using Wasmtime;

using var engine = new Engine();

//can also use FromFile here
using var module =  Module.FromText(engine, "testmodule", """(module (func $hello (import "" "hello")) (func (export "run") (call $hello)))""");

using var linker = new Linker(engine);
using var store = new Store(engine);

linker.Define("", "hello", Function.FromCallback(store, () => Console.WriteLine("Hello From C#")));

var instance = linker.Instantiate(store, module);

var run = instance.GetAction("run");
run();