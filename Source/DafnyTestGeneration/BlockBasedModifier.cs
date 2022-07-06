using System.Collections.Generic;
using Microsoft.Boogie;
using System;
using System.Linq;

namespace DafnyTestGeneration {

  /// <summary>
  /// A version of ProgramModifier that inserts assertions into the code
  /// that fail when a particular basic block is visited
  /// </summary>
  public class BlockBasedModifier : ProgramModifier {

    private Implementation? impl; // the implementation currently traversed
    private Program? program; // the original program
    private List<ProgramModification> modifications = new();

    public List<string> prevCoveredBlocks;
    private bool doneCovering = false;

    public BlockBasedModifier() : base() { }

    public BlockBasedModifier(List<string> coveredBlocks) : base() {
      this.prevCoveredBlocks = coveredBlocks;
    }

    protected override IEnumerable<ProgramModification> GetModifications(Program p) {
      modifications = new List<ProgramModification>();
      VisitProgram(p);
      return modifications;
    }

    public override Block VisitBlock(Block node) {

      // Console.WriteLine("BlockBased VISITING BLOCK!" + node.UniqueId);

      // TODO: Work out how to avoid covering an unnecessary amount of extra blocks.

      // if(doneCovering){
      //   return node;
      // }
      // No need to annotate blocks we don't care about covering.
      // if(prevCoveredBlocks != null){
      //   ISet<string> captured = ExtractCapturedStates(node);
      //   var capturedList = captured.ToList();
      //   capturedList.Sort();
      //   var capturedStateBlock = capturedList.First();

      //   // Newly covered block, we mark it and note that we are done.
      //   if(!prevCoveredBlocks.Contains(capturedStateBlock)){
      //     doneCovering = true;
      //   }
      // }

      if (program == null || impl == null) {
        return node;
      }
      base.VisitBlock(node);
      if (node.cmds.Count == 0) { // ignore blocks with zero commands
        return node;
      }
      node.cmds.Add(GetCmd("assert false;"));
      var record = new BlockBasedModification(program,
        ImplementationToTarget?.VerboseName ?? impl.VerboseName,
        node.UniqueId, ExtractCapturedStates(node));
      modifications.Add(record);

      node.cmds.RemoveAt(node.cmds.Count - 1);
      return node;
    }

    public override Implementation VisitImplementation(Implementation node) {
      impl = node;
      if (ImplementationIsToBeTested(node)) {
        VisitBlockList(node.Blocks);
      }
      return node;
    }

    public override Program VisitProgram(Program node) {
      program = node;
      node.Implementations.Iter(i => VisitImplementation(i));
      return node;
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
          // result.Add(assumeCmd.Attributes?.Params?[0]?.ToString() ?? "");
          result.Add(assumeCmd.Attributes?.Params?[0]?.ToString().Replace(":", "_").Replace(" ", "_") ?? "");
        }
      }
      return result;
    }
  }
}