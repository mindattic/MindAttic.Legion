using MindAttic.Legion;
using MindAttic.Legion.Cli;

// Top-level entry point — delegates argument parsing and command dispatch
// to LegionCli.RunAsync, returning its exit code as the process exit code.
return await new LegionCli().RunAsync(args);
