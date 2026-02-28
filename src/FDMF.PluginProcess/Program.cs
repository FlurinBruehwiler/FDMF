// See https://aka.ms/new-console-template for more information

using System.IO.Pipes;
using System.Text;

Console.WriteLine("Hello, World!");

var client = new NamedPipeClientStream(".", "FDMF", PipeDirection.InOut);
await client.ConnectAsync();
await client.WriteAsync("Test123"u8.ToArray());