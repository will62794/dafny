using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Boogie;
using Microsoft.Dafny;
using Program = Microsoft.Boogie.Program;

namespace DafnyTestGeneration {

  /// <summary>
  /// Records a modification of the boogie program under test. The modified
  /// program has an assertion that should fail provided a certain block is
  /// visited / path is taken.
  /// </summary>
  public class ProgramModification {

    private readonly string procedure; // procedure to be tested
    protected readonly string? log;

    /// <summary>
    /// Return the log or null if the counterexample in the log does not
    /// cover any new blocks \ paths.
    /// </summary>
    public virtual string? Log => log;

    public ProgramModification(Program program, string procedure) {
      this.procedure = procedure;
      log = GetCounterExampleLog(program);
    }

    /// <summary>
    /// Setup CommandLineArguments to prepare verification. This is necessary
    /// because the procsToCheck field in CommandLineOptions (part of Boogie)
    /// is private meaning that the only way of setting this field is by calling
    /// options.Parse() on a new DafnyObject.
    /// </summary>
    private static DafnyOptions SetupOptions(string procedure) {
      var options = new DafnyOptions();
      options.Parse(new[] { "/proc:" + procedure });
      options.EnhancedErrorMessages = 1;
      options.ModelViewFile = "-";
      options.ProverOptions = new List<string>() {
        "O:model_compress=false",
        "O:model.completion=true",
        "O:model_evaluator.completion=true"};
      options.ProverOptions.AddRange(DafnyOptions.O.ProverOptions);
      options.LoopUnrollCount = DafnyOptions.O.LoopUnrollCount;
      options.DefiniteAssignmentLevel = DafnyOptions.O.DefiniteAssignmentLevel;
      options.WarnShadowing = DafnyOptions.O.WarnShadowing;
      options.VerifyAllModules = DafnyOptions.O.VerifyAllModules;
      return options;
    }

    /// <summary>
    /// Return the counterexample log produced by trying to verify this modified
    /// version of the original boogie program. Return null if verification
    /// succeeded or timed out.
    /// </summary>
    protected string? GetCounterExampleLog(Program program) {
      var oldOptions = DafnyOptions.O;
      var options = SetupOptions(procedure);
      DafnyOptions.Install(options);
      var uniqueId = Guid.NewGuid().ToString();
      program.Resolve();
      program.Typecheck();
      ExecutionEngine.EliminateDeadVariables(program);
      ExecutionEngine.CollectModSets(program);
      ExecutionEngine.CoalesceBlocks(program);
      ExecutionEngine.Inline(program);
      var result = Utils.CaptureConsoleOutput(
        () => ExecutionEngine.InferAndVerify(program,
          new PipelineStatistics(), uniqueId,
          _ => { }, uniqueId));
      DafnyOptions.Install(oldOptions);
      // make sure that there is a counterexample (i.e. no parse errors, etc):
      string? line;
      var stringReader = new StringReader(result);
      while ((line = stringReader.ReadLine()) != null) {
        if (line.StartsWith("Block |")) {
          return result;
        }
      }
      return result;
    }
  }
}