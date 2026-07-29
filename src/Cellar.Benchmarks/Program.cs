using BenchmarkDotNet.Running;

// Runs all benchmarks in this assembly. Examples:
//   dotnet run -c Release --project src/Cellar.Benchmarks -- --filter *
//   dotnet run -c Release --project src/Cellar.Benchmarks -- --filter *Compositor*
BenchmarkSwitcher.FromAssembly(typeof(Cellar.Benchmarks.Program).Assembly).Run(args);

// Block-scoped: a file-scoped namespace can't follow the top-level statements above (CS8956).
namespace Cellar.Benchmarks
{
    internal sealed partial class Program;
}
