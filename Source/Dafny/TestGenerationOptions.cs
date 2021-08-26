using System;
using JetBrains.Annotations;
using Bpl = Microsoft.Boogie;

namespace Microsoft.Dafny {

  /// <summary>
  /// An extension of DafnyOptions
  /// </summary>
  public class TestGenerationOptions {

    public enum Modes { None, Block, Path };
    public Modes Mode = Modes.None;
    [CanBeNull] public string TargetMethod = null;
    public uint? SeqLengthLimit = null;
    public uint InlineDepth = 0;

    public bool ParseOption(string name, string value, DafnyOptions options) {
      switch (name) {
        case "testMode":

          Mode = value switch {
            "None" => Modes.None,
            "Block" => Modes.Block,
            "Path" => Modes.Path,
            _ => throw new Exception("Invalid value for testMode")
          };
          if (Mode != Modes.None) {
            options.Compile = false;
            options.DafnyVerify = false;
          }
          return true;

        case "testSeqLengthLimit":
          var limit = 0;
          if (GetNumericArgument(ref limit, name, value, i => i >= 0)) {
            SeqLengthLimit = (uint) limit;
          }
          return true;

        case "testTargetMethod":
          TargetMethod = value;
          return true;

        case "testInlineDepth":
          var depth = 0;
          if (GetNumericArgument(ref depth, name, value, i => i >= 0)) {
            InlineDepth = (uint) depth;
          }
          return true;
      }

      return false;
    }

    private static bool GetNumericArgument(ref int arg, string name,
      string value, Predicate<int> filter) {
      try {
        var int32 = Convert.ToInt32(value);
        if (filter == null || filter(int32)) {
          arg = int32;
          return true;
        }
      }
      catch (FormatException) { }
      catch (OverflowException) { }
      Console.Error.WriteLine($"Invalid argument \"{value}\" to option {name}");
      return false;
    }

    public static string Help => @"
/testMode:<None|Block|Path>
    None is the default and has no effect.
    Block prints block-coverage tests for the given program.
    Path prints path-coverage tests for the given program.
    Using \definiteAssignment:3 and \loopUnroll is highly recommended when
    generating tests.
/testSeqLengthLimit:<n>
    If \testMode is not None, using this argument adds an axiom that sets the
    length of all sequences to be no greater than <n>. This is useful in
    conjunction with loop unrolling.
/testTargetMethod:<methodName>
    If specified, only this method will be tested.
/testInlineDepth:<n>
    0 is the default. When used in conjunction with \testTargetMethod, this
    argument specifies the depth up to which all non-tested methods should be
    inlined.";

  }
}