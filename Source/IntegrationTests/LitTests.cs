using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Dafny;
using Xunit;
using Xunit.Abstractions;
using XUnitExtensions;
using XUnitExtensions.Lit;

[assembly: TestCollectionOrderer("XUnitExtensions.TestCollectionShardFilter", "XUnitExtensions")]

namespace IntegrationTests {
  public class LitTests {

    // Change this to true in order to debug the execution of commands like %dafny.
    // This is false by default because the main dafny CLI implementation currently has shared static state, which
    // causes errors when invoking the CLI in the same process on multiple inputs in sequence, much less in parallel.
    private const bool InvokeMainMethodsDirectly = false;

    private static readonly Assembly DafnyDriverAssembly = typeof(DafnyDriver).Assembly;
    private static readonly Assembly DafnyServerAssembly = typeof(Server).Assembly;

    private static readonly string[] DefaultDafnyArguments = new[] {
      // Try to verify 2 verification conditions at once
      "/vcsCores:2",

      // We do not want absolute or relative paths in error messages, just the basename of the file
      "/useBaseNameForFileName",

      // We do not want output such as "Compiled program written to Foo.cs"
      // from the compilers, since that changes with the target language
      "/compileVerbose:0",

      // Hide Boogie execution traces since they are meaningless for Dafny programs
      "/errorTrace:0",
      
      // Set a default time limit, to catch cases where verification time runs off the rails
      "/timeLimit:300"
    };

    private static readonly string[] DefaultDafny0Arguments = DefaultDafnyArguments.Prepend("/countVerificationErrors:0").ToArray();

    private static ILitCommand MainWithArguments(Assembly assembly, IEnumerable<string> arguments,
      LitTestConfiguration config, bool invokeDirectly) {
      return MainMethodLitCommand.Parse(assembly, arguments, config, invokeDirectly);
    }

    private static readonly LitTestConfiguration Config;

    static LitTests() {
      // Allow extra arguments to Dafny subprocesses. This can be especially
      // useful for capturing prover logs.
      var extraDafnyArguments =
        Environment.GetEnvironmentVariable("DAFNY_EXTRA_TEST_ARGUMENTS");

      IEnumerable<string> AddExtraArgs(IEnumerable<string> args, IEnumerable<string> local) {
        return (extraDafnyArguments is null ? args : args.Append(extraDafnyArguments)).Concat(local);
      }

      var repositoryRoot = Path.GetFullPath("../../../../../"); // Up from Source/IntegrationTests/bin/Debug/net6.0/

      var substitutions = new Dictionary<string, string> {
        { "%diff", "diff" },
        { "%binaryDir", "." },
        { "%z3", Path.Join("z3", "bin", "z3") },
        { "%repositoryRoot", repositoryRoot.Replace(@"\", "/") },
        { "%refmanexamples", Path.Join("TestFiles", "LitTests", "LitTest", "refman", "examples") }
      };

      var commands = new Dictionary<string, Func<IEnumerable<string>, LitTestConfiguration, ILitCommand>> {
        {
          "%baredafny", (args, config) =>
            MainMethodLitCommand.Parse(DafnyDriverAssembly, args, config, InvokeMainMethodsDirectly)
        }, {
          "%dafny_0", (args, config) =>
            MainMethodLitCommand.Parse(DafnyDriverAssembly, AddExtraArgs(DefaultDafny0Arguments, args),
              config, InvokeMainMethodsDirectly)
        }, {
          "%dafny", (args, config) =>
            MainMethodLitCommand.Parse(DafnyDriverAssembly, AddExtraArgs(DefaultDafnyArguments, args),
              config, InvokeMainMethodsDirectly)
        }, {
          "%server", (args, config) =>
            MainMethodLitCommand.Parse(DafnyServerAssembly, args, config, InvokeMainMethodsDirectly)
        }, {
          "%diff", (args, config) => DiffCommand.Parse(args.ToArray())
        }, {
          "%sed", (args, config) => SedCommand.Parse(args.ToArray())
        }, {
          "%OutputCheck", (args, config) =>
            OutputCheckCommand.Parse(args, config)
        }
      };

      var passthroughEnvironmentVariables = new[] { "PATH", "HOME", "DOTNET_NOLOGO" };

      // Silence dotnet's welcome message
      Environment.SetEnvironmentVariable("DOTNET_NOLOGO", "true");

      string[] features;
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
        features = new[] { "ubuntu", "posix" };
      } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
        features = new[] { "windows" };
        string path = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var directory = System.IO.Path.GetDirectoryName(path);
        Environment.SetEnvironmentVariable("DOTNET_CLI_HOME", directory);
        if (directory != null) {
          Directory.SetCurrentDirectory(directory);
        }

        Environment.SetEnvironmentVariable("HOME",
          Environment.GetEnvironmentVariable("HOMEDRIVE") + Environment.GetEnvironmentVariable("HOMEPATH"));
        passthroughEnvironmentVariables = passthroughEnvironmentVariables
          .Concat(new[] { // Careful: Keep this list in sync with the one in lit.site.cfg
            "APPDATA",
            "HOMEDRIVE",
            "HOMEPATH",
            "INCLUDE",
            "LIB",
            "LOCALAPPDATA",
            "NODE_PATH",
            "ProgramFiles",
            "ProgramFiles(x86)",
            "SystemRoot",
            "SystemDrive",
            "TEMP",
            "TMP",
            "USERPROFILE"
          }).ToArray();
      } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
        features = new[] { "macosx", "posix" };
      } else {
        throw new Exception($"Unsupported OS: {RuntimeInformation.OSDescription}");
      }

      var dafnyReleaseDir = Environment.GetEnvironmentVariable("DAFNY_RELEASE");
      if (dafnyReleaseDir != null) {
        commands["%baredafny"] = (args, config) =>
          new ShellLitCommand(config, Path.Join(dafnyReleaseDir, "dafny"), args, config.PassthroughEnvironmentVariables);
        commands["%dafny_0"] = (args, config) =>
          new ShellLitCommand(config, Path.Join(dafnyReleaseDir, "dafny"),
            AddExtraArgs(DefaultDafny0Arguments, args), config.PassthroughEnvironmentVariables);
        commands["%dafny"] = (args, config) =>
          new ShellLitCommand(config, Path.Join(dafnyReleaseDir, "dafny"),
            AddExtraArgs(DefaultDafnyArguments, args), config.PassthroughEnvironmentVariables);
        commands["%server"] = (args, config) =>
          new ShellLitCommand(config, Path.Join(dafnyReleaseDir, "DafnyServer"), args, config.PassthroughEnvironmentVariables);
        substitutions["%z3"] = Path.Join(dafnyReleaseDir, "z3", "bin", "z3");
      }

      Config = new LitTestConfiguration(substitutions, commands, features, passthroughEnvironmentVariables);
    }

    private readonly ITestOutputHelper output;

    public LitTests(ITestOutputHelper output) {
      this.output = output;
    }

    [FileTheory]
    [FileData(Includes = new[] { "**/*.dfy", "**/*.transcript" },
              Excludes = new[] { "**/Inputs/**/*", "**/Output/**/*", "refman/examples/**/*",
                "tutorial/AutoExtern", // This is tested separately in the unit tests of Source/AutoExtern
              })]
    public void LitTest(string path) {
      LitTestCase.Run(path, Config, output);
    }
  }
}
