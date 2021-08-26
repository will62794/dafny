using System.Collections.Generic;
using Microsoft.Boogie;

namespace DafnyTestGeneration {

  /// <summary>
  /// A version of ProgramModifier that inserts assertions into the code
  /// that fail when a particular basic block is visited
  /// </summary>
  public class BlockBasedModifier : ProgramModifier {

    private string? implName; // name of the implementation currently traversed
    private Program? program; // the original program
    private List<(Block block, string procedureName)> toAssert = new();

    protected override IEnumerable<ProgramModification> GetModifications(Program p) {
      toAssert = new();
      VisitProgram(p);
      foreach (var instance in toAssert) {
        instance.block.cmds.Add(GetCmd("assert false;"));
        var modification = new BlockBasedModification(p, instance.procedureName,
          instance.block.UniqueId, ExtractCapturedStates(instance.block));
        yield return modification;
        instance.block.cmds.RemoveAt(instance.block.cmds.Count - 1);
      }
    }

    public override Block VisitBlock(Block node) {
      if (program == null || implName == null) {
        return node;
      }
      base.VisitBlock(node);
      if (node.cmds.Count == 0) { // ignore blocks with zero commands
        return node;
      }
      toAssert.Add(new (node, ProcedureName ?? implName));
      return node;
    }

    public override Implementation VisitImplementation(Implementation node) {
      implName = node.Name;
      if (ProcedureIsToBeTested(node.Name)) {
        VisitBlockList(node.Blocks);
      }
      return node;
    }

    public override Program VisitProgram(Program node) {
      program = node;
      return base.VisitProgram(node);
    }

    /// <summary>
    /// Return the list of all states covered by the block.
    /// A state is represented by the string recorded via :captureState
    /// </summary>
    private static ISet<string> ExtractCapturedStates(Block node) {
      HashSet<string> result = new();
      foreach (var cmd in node.cmds) {
        if (!(cmd is AssumeCmd assumeCmd)) {
          continue;
        }
        if (assumeCmd.Attributes?.Key == "captureState") {
          result.Add(assumeCmd.Attributes?.Params?[0]?.ToString() ?? "");
        }
      }
      return result;
    }
  }
}