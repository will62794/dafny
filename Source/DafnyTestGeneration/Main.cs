using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Dafny;
using Program = Microsoft.Dafny.Program;

namespace DafnyTestGeneration {

  public static class Main {

    /// <summary>
    /// This method returns each capturedState that is unreachable, one by one,
    /// and then a line with the summary of how many such states there are, etc.
    /// Note that loop unrolling may cause false positives and the absence of
    /// loop unrolling may cause false negatives.
    /// </summary>
    /// <returns></returns>
    public static async IAsyncEnumerable<string> GetDeadCodeStatistics(Program program) {

      var modifications = GetModifications(program).ToList();
      var blocksReached = modifications.Count;
      HashSet<string> allStates = new();
      HashSet<string> allDeadStates = new();

      // Generate tests based on counterexamples produced from modifications
      for (var i = modifications.Count - 1; i >= 0; i--) {
        await modifications[i].GetCounterExampleLog();
        var deadStates = ((BlockBasedModification)modifications[i]).GetKnownDeadStates();
        if (deadStates.Count != 0) {
          foreach (var capturedState in deadStates) {
            yield return $"Code at {capturedState} is potentially unreachable.";
          }
          blocksReached--;
          allDeadStates.UnionWith(deadStates);
        }
        allStates.UnionWith(((BlockBasedModification)modifications[i]).GetAllStates());
      }

      yield return $"Out of {modifications.Count} basic blocks " +
                   $"({allStates.Count} capturedStates), {blocksReached} " +
                   $"({allStates.Count - allDeadStates.Count}) are reachable. " +
                   $"There might be false negatives if you are not unrolling " +
                   $"loops. False positives are always possible.";
    }

    public static async IAsyncEnumerable<string> GetDeadCodeStatistics(string sourceFile) {
      var source = await new StreamReader(sourceFile).ReadToEndAsync();
      var program = Utils.Parse(source, sourceFile);
      if (program == null) {
        yield return "Cannot parse program";
        yield break;
      }
      await foreach (var line in GetDeadCodeStatistics(program)) {
        yield return line;
      }
    }

    private static IEnumerable<ProgramModification> GetModifications(Program program) {
      var dafnyInfo = new DafnyInfo(program);
      // Translate the Program to Boogie:
      var oldPrintInstrumented = DafnyOptions.O.PrintInstrumented;
      DafnyOptions.O.PrintInstrumented = true;
      var boogiePrograms = Translator
        .Translate(program, program.Reporter)
        .ToList().ConvertAll(tuple => tuple.Item2);
      DafnyOptions.O.PrintInstrumented = oldPrintInstrumented;

      // Create modifications of the program with assertions for each block\path
      ProgramModifier programModifier =
        DafnyOptions.O.TestGenOptions.Mode == TestGenerationOptions.Modes.Path
          ? new PathBasedModifier()
          // : new BlockBasedModifier();
          : new BlockBasedModifier(DafnyOptions.O.TestGenOptions.prevCoveredBlocks);

      return programModifier.GetModifications(boogiePrograms, dafnyInfo);
    }

    /// <summary>
    /// Generate test methods for a certain Dafny program.
    /// </summary>
    /// <returns></returns>
    public static async IAsyncEnumerable<TestMethod> GetTestMethodsForProgram(Program program) {

      var dafnyInfo = new DafnyInfo(program);
      var modifications = GetModifications(program).ToList();

      // Generate tests based on counterexamples produced from modifications
      var testMethodToUniqueId = new ConcurrentDictionary<TestMethod, string>();
      for (var i = modifications.Count - 1; i >= 0; i--) {
        var log = await modifications[i].GetCounterExampleLog();
        if (log == null) {
          continue;
        }

        var coveredBlocks = DafnyOptions.O.TestGenOptions.prevCoveredBlocks;
        // Print out approximate source code line corresponding to covered block.
        BlockBasedModification bm = (BlockBasedModification)modifications[i];
        var capturedStates = bm.getCapturedStates();
        // foreach(var cs in capturedStates){
        //   Console.WriteLine("captured state:" + cs);
        // }
        var capturedList = capturedStates.ToList();
        var capturedStateBlock = "";
        if (capturedList.Count > 0) {
          capturedList.Sort();
          capturedStateBlock = capturedList.First();
        }

        if (coveredBlocks != null && coveredBlocks.Contains(capturedStateBlock)) {
          // Don't generate test for this block, if we already did.
          continue;
        }

        if (coveredBlocks != null) {
          i = 0;
        }

        Console.WriteLine("COVERED:" + capturedStateBlock);

        if (DafnyOptions.O.TestGenOptions.Verbose) {
          Console.WriteLine(
            $"// Extracting the test for {modifications[i].uniqueId} from the counterexample...");
        }

        var testMethod = new TestMethod(dafnyInfo, log);
        var assignments = testMethod.Assignments;

        foreach (var assignment in assignments) {
          Console.WriteLine("ASSIGNMENT:" + assignment.parentId + ":" + assignment.fieldName + ":" + assignment.childId);
        }


        if (testMethodToUniqueId.ContainsKey(testMethod)) {
          if (DafnyOptions.O.TestGenOptions.Verbose) {
            Console.WriteLine(
              $"// Test for {modifications[i].uniqueId} matches a test previously generated for {testMethodToUniqueId[testMethod]}.");
          }
          continue;
        }
        testMethodToUniqueId[testMethod] = modifications[i].uniqueId;
        yield return testMethod;
      }
    }

    /// <summary>
    /// Return a Dafny class (list of lines) with tests for the given Dafny file
    /// </summary>
    public static async IAsyncEnumerable<string> GetTestClassForProgram(string sourceFile) {

      TestMethod.ClearTypesToSynthesize();
      var source = new StreamReader(sourceFile).ReadToEnd();
      var program = Utils.Parse(source, sourceFile);
      if (program == null) {
        yield break;
      }
      var dafnyInfo = new DafnyInfo(program);
      var rawName = Path.GetFileName(sourceFile).Split(".").First();

      string EscapeDafnyStringLiteral(string str) {
        return $"\"{str.Replace(@"\", @"\\")}\"";
      }

      yield return $"include {EscapeDafnyStringLiteral(sourceFile)}";
      yield return $"module {rawName}UnitTests {{";
      foreach (var module in dafnyInfo.ToImportAs.Keys) {
        // TODO: disambiguate between modules amongst generated tests
        if (module.Split(".").Last() == dafnyInfo.ToImportAs[module]) {
          yield return $"import {module}";
        } else {
          yield return $"import {dafnyInfo.ToImportAs[module]} = {module}";
        }
      }

      await foreach (var method in GetTestMethodsForProgram(program)) {
        yield return method.ToString();
      }

      yield return TestMethod.EmitSynthesizeMethods();

      yield return "}";
    }
  }
}