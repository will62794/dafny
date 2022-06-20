using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DafnyServer.CounterexampleGeneration;
using Microsoft.Boogie;
using Microsoft.Dafny;
using MapType = Microsoft.Dafny.MapType;
using Type = Microsoft.Dafny.Type;

namespace DafnyTestGeneration {

  /// <summary> Allows converting a counterexample to a test method </summary>
  public class TestMethod {

    private static int nextId; // next unique id to be assigned

    // list of values to mock together with their types
    public readonly List<(string id, Type type)> ObjectsToMock = new();
    // maps a variable that is mocked to its unique id
    private readonly Dictionary<DafnyModelVariable, string> mockedVarId = new();
    public readonly List<(string parentId, string fieldName, string childId)> Assignments = new();
    private readonly int id = nextId++;
    public readonly DafnyInfo DafnyInfo;
    // name of the method for which the counterexample is generated
    public readonly string MethodName;
    // values of the arguments to be passed to the method call
    public readonly List<string> ArgValues;
    // number of type parameters for the method (all will be set to defaultType)
    public readonly int NOfTypeParams;
    // default type to replace any type variable with
    private readonly Type defaultType = Type.Int;
    // the DafnyModel that describes the inputs to this test method
    private readonly DafnyModel dafnyModel;
    private readonly Dictionary<string, string> functions;
    // Set of all types for which a {:synthesize} - annotated method is needed
    // These methods are used to get fresh instances of the corresponding types
    private static readonly HashSet<string> TypesToSynthesize = new();
    // is set to true wheneve the tool ecnounters something it does not support
    private bool encouteredUnsupportedType = false; 

    public TestMethod(DafnyInfo dafnyInfo, string log) {
      DafnyInfo = dafnyInfo;
      functions = new();
      var typeNames = ExtractPrintedInfo(log, "Types | ");
      var argumentNames = ExtractPrintedInfo(log, "Impl | ");
      dafnyModel = DafnyModel.ExtractModel(log);
      MethodName = argumentNames.First();
      argumentNames.RemoveAt(0);
      NOfTypeParams = dafnyInfo.GetNOfTypeParams(MethodName);
      ArgValues = ExtractInputs(dafnyModel.States.First(), argumentNames, typeNames);
    }

    public static void ClearTypesToSynthesize() {
      TypesToSynthesize.Clear();
    }

    /// <summary>
    /// Returns the name given to a {:synthesize} - annotated method that
    /// returns a value of certain type
    /// </summary>
    private static string GetSynthesizeMethodName(string typ) {
      return "getFresh" + Regex.Replace(typ, "[^a-zA-Z]", "");
    }

    /// <summary>
    /// Returns a string that contains all the {:synthesize} annotated methods
    /// necessary to compile the tests
    /// </summary>
    public static string EmitSynthesizeMethods() {
      var result = "";
      foreach (var typ in TypesToSynthesize) {
        var methodName = GetSynthesizeMethodName(typ);
        result += $"\nmethod {{:synthesize}} {methodName}() " +
                  $"returns (o:{typ}) ensures fresh(o)";
      }
      return result;
    }

    /// <summary>
    /// Extract values that certain elements have at a certain state in the
    /// model.
    /// </summary>
    /// <param name="state"> DafnyModelState to work with</param>
    /// <param name="printOutput"> Output of print command for each element.
    /// This can either be a value of a basic type ("1.0", "false", etc.),
    /// a reference to an element ("T@U!val!25", etc.) or an empty string,
    /// which means that one has to come up with a value based on its
    /// type alone </param>
    /// <param name="types">the types of the elements</param>
    /// <returns></returns>
    private List<string> ExtractInputs(DafnyModelState state, IReadOnlyList<string> printOutput, IReadOnlyList<string> types) {
      var result = new List<string>();
      var vars = state.ExpandedVariableSet(null);
      var parameterIndex = DafnyInfo.IsStatic(MethodName) ? -1 : -2; // TODO: this is wrong
      for (var i = 0; i < printOutput.Count; i++) {
        if (types[i] == "Ty") {
          continue; // this means that this parameter is a type variable
        }
        parameterIndex++;
        if (printOutput[i] == "") {
          result.Add(GetDefaultValue(DafnyInfo.GetParameterTypes(MethodName)[parameterIndex]));
          continue;
        }
        if (!printOutput[i].StartsWith("T@")) {
          if (Regex.IsMatch(printOutput[i], "^[0-9]+bv[0-9]+$")) {
            var baseIndex = printOutput[i].IndexOf('b');
            result.Add($"({printOutput[i][..baseIndex]} as {printOutput[i][baseIndex..]})");
          } else {
            result.Add(printOutput[i]);
          }
          continue;
        }
        foreach (var variable in vars) {
          if ((variable.Element as Model.Uninterpreted)?.Name != printOutput[i]) {
            continue;
          }
          result.Add(ExtractVariable(variable));
          break;
        }
      }
      return result;
    }

    // Returns a new value of the defaultType type (set to int by default)
    private string GetADefaultTypeValue(DafnyModelVariable variable) {
      return dafnyModel.GetUnreservedNumericValue(variable.Element, Type.Int);
    }

    private string GetFunctionOfType(ArrowType type) {
      type = (ArrowType) DafnyModelTypeUtils.ReplaceTypeVariables(type, defaultType);
      var name = $"test{id}function{functions.Count}";
      var definition = 
        $"function method {name} " + // name
        $"({string.Join(",", type.TypeArgs.SkipLast(1).Select((t, i) => "a" + i + ":" + t))})" + // parameter types
        $":{type.TypeArgs.Last()}" + // return type
        $"{{ {GetDefaultValue(type.TypeArgs.Last())} }}"; // body
      functions.Add(name, definition);
      return name;
    }

    /// <summary>
    /// Extract the value of a variable. This can have side-effects on
    /// assignments, reservedValues, reservedValuesMap, and objectsToMock.
    /// </summary>
    private string ExtractVariable(DafnyModelVariable variable) {
      if (mockedVarId.ContainsKey(variable)) {
        return mockedVarId[variable];
      }

      if (variable is DuplicateVariable duplicateVariable) {
        return ExtractVariable(duplicateVariable.Original);
      }

      if (variable is FunctionVariable) {
        var varId = GetFunctionOfType((ArrowType)variable.Type);
        mockedVarId.Add(variable, varId);
        return varId;
      }

      List<string> elements = new();
      var variableType =
        DafnyModelTypeUtils.ReplaceTypeVariables(variable.Type, defaultType);
      if (variableType.ToString() == defaultType.ToString() && 
          variableType.ToString() != variable.Type.ToString()) {
        return GetADefaultTypeValue(variable);
      }
      switch (variableType) {
        case IntType:
        case RealType:
        case BoolType:
        case CharType:
        case BitvectorType:
          return variable.Value;
        case SeqType seqType:
          var seqVar = variable as SeqVariable;
          if (seqVar?.GetLength() == null) {
            return "[]";
          }
          for (var i = 0; i < seqVar.GetLength(); i++) {
            var element = seqVar[i];
            if (element == null) {
              elements.Add(GetDefaultValue(seqType.Arg));
              continue;
            }
            elements.Add(ExtractVariable(element));
          }
          return $"[{string.Join(", ", elements)}]";
        case SetType:
          if (!variable.children.ContainsKey("true")) {
            return "{}";
          }
          foreach (var element in variable.children["true"]) {
            elements.Add(ExtractVariable(element));
          }
          return $"{{{string.Join(", ", elements)}}}";
        case MapType:
          var mapVar = variable as MapVariable;
          List<string> mappingStrings = new();
          foreach (var mapping in mapVar?.Mappings ?? new()) {
            mappingStrings.Add($"{ExtractVariable(mapping.Key)} := {ExtractVariable(mapping.Value)}");
          }
          return $"map[{string.Join(", ", mappingStrings)}]";
        case UserDefinedType undefined when undefined.Name == DafnyModel.UndefinedType.Name:
          return "null";
        case UserDefinedType arrType when new Regex("^_System.array[0-9]*\\?$").IsMatch(arrType.Name):
          encouteredUnsupportedType = true;
          break; // arrays not supported
        case UserDefinedType tupleType when tupleType.Name.StartsWith("_System.Tuple"):
          // TODO: what about the order of fields?
          return "(" + string.Join(",", variable.children.Values
            .Select(v => ExtractVariable(v.First()))) +")";
        case UserDefinedType dataType when (variable.CanonicalName() is not "" or "null"):
          // TODO: What about type variables?
          dataType = (UserDefinedType) DafnyModelTypeUtils
            .GetInDafnyFormat(DafnyModelTypeUtils
              .ReplaceTypeVariables(variableType, defaultType));
          List<string> fields = new();
          foreach (var filedName in variable.children.Keys) {
            if (variable.children[filedName].Count != 1) {
              continue;
            }
            fields.Add(filedName + ":=" + ExtractVariable(variable.children[filedName].First()));
          }
          if (fields.Count == 0) {
            return dataType.Name + "." + variable.CanonicalName();
          }
          return dataType.Name + "." + variable.CanonicalName() + "(" + string.Join(",", fields) + ")";
        default:
          var varId = $"v{ObjectsToMock.Count}";
          var dafnyType =
            DafnyModelTypeUtils.GetInDafnyFormat(
              DafnyModelTypeUtils.GetNonNullable(variableType));
          ObjectsToMock.Add(new(varId, dafnyType));
          TypesToSynthesize.Add(dafnyType.ToString());
          mockedVarId[variable] = varId;
          foreach (var fieldName in variable.children.Keys) {
            if (variable.children[fieldName].Count != 1) {
              continue;
            }
            Assignments.Add(new(varId, fieldName, ExtractVariable(variable.children[fieldName].First())));
          }
          return varId;
      }
      encouteredUnsupportedType = true;
      return "null";
    }

    /// <summary>
    /// Return the default value for a variable of a particular type.
    /// Note that default value is different from unspecified value.
    /// An unspecified value is such a value for which a model does reserve
    /// an element (e.g. T@U!val!25).
    /// </summary>
    private string GetDefaultValue(Type type) {
      type = DafnyModelTypeUtils.ReplaceTypeVariables(type, defaultType);
      switch (type) {
        case IntType:
          return "0";
        case RealType:
          return "0.0";
        case BoolType:
          return "false";
        case CharType:
          return "\'a\'";
        case BitvectorType bitvectorType:
          return $"(0 as bv{bitvectorType.Width})";
        case SeqType:
          return "[]";
        case SetType:
          return "{}";
        case MapType:
          return "map[]";
        case ArrowType arrowType:
          return GetFunctionOfType(arrowType);
        case UserDefinedType userDefinedType when userDefinedType.Name.EndsWith("?"):
          return "null";
        case UserDefinedType tupleType when tupleType.Name.StartsWith("_System.Tuple"):
          return "(" + string.Join(",", tupleType.TypeArgs.Select(GetDefaultValue)) + ")";
        // TODO: Add datatype support
      }
      // this should only be reached if the type is non-nullable
      var varId = $"v{ObjectsToMock.Count}";
      ObjectsToMock.Add(new(varId, type));
      TypesToSynthesize.Add(type.ToString());
      return varId;
    }

    /// <summary>
    /// Extract output of an "assume {:print ...} true;"  statement.
    /// </summary>
    /// <param name="log">the counterexample log as a string</param>
    /// <param name="prefix">the prefix of the print statement such as
    /// "Types" or "Impl" - these come from ProgramModifier</param>
    private static List<string> ExtractPrintedInfo(string log, string prefix) {
      var lines = log.Split("\n");
      foreach (var line in lines) {
        if (!line.StartsWith(prefix)) {
          continue;
        }

        var result = line.Split("|").ToList();
        result.RemoveAt(0);
        for (var i = 0; i < result.Count; i++) {
          result[i] = Regex.Replace(result[i],
            "/ *([0-9]+\\.[0-9]+) +([0-9]+\\.[0-9]+)", "$1/$2");
          result[i] = Regex.Replace(result[i], "[)( \\\\]", "");
        }

        return result;
      }

      return new List<string>();
    }

    /// <summary>  Return the test method as a list of lines of code </summary>
    private List<string> TestMethodLines() {

      if (encouteredUnsupportedType) {
        return new List<string>();
      }
      
      List<string> lines = new();
      lines.AddRange(functions.Values);

      // test method parameters and declaration:
      var mockedLines = ObjectsToMock
        .Select(kVPair => $"var {kVPair.id} := " +
                          $"{GetSynthesizeMethodName(kVPair.type.ToString())}();");
      var returnParNames = new List<string>();
      for (var i = 0; i < DafnyInfo.GetReturnTypes(MethodName).Count; i++) {
        returnParNames.Add("r" + i);
      }

      lines.Add($"method {{:test}} test{id}() {{");
      lines.AddRange(mockedLines);

      // assignments necessary to set up the test case:
      foreach (var assignment in Assignments) {
        lines.Add($"{assignment.parentId}.{assignment.fieldName} := " +
                  $"{assignment.childId};");
      }

      // the method call itself:
      var typeArguments = "";
      if (NOfTypeParams > 0) {
        typeArguments = "<" + string.Join(",", Enumerable.Repeat(defaultType.ToString(), NOfTypeParams)) + ">";
      }
      string methodCall;
      if (DafnyInfo.IsStatic(MethodName)) {
        methodCall = $"{MethodName}{typeArguments}({string.Join(", ", ArgValues)});";
      } else {
        var receiver = ArgValues[0];
        ArgValues.RemoveAt(0);
        methodCall = $"{receiver}.{MethodName.Split(".").Last()}" +
                     $"{typeArguments}({string.Join(", ", ArgValues)});";
        ArgValues.Insert(0, receiver);
      }

      var returnValues = "";
      if (returnParNames.Count != 0) {
        returnValues = "var " + string.Join(", ", returnParNames) + " := ";
      }

      lines.Add(returnValues + methodCall);
      lines.Add("}");

      return lines;
    }

    public override string ToString() {
      return string.Join("\n", TestMethodLines());
    }

    public override int GetHashCode() {
      var lines = TestMethodLines();
      lines.RemoveAt(0);
      var hashCode = string.Join("", lines).GetHashCode();
      return hashCode;
    }

    public override bool Equals(object? obj) {
      if (obj is not TestMethod other) {
        return false;
      }
      var otherLines = other.TestMethodLines();
      var lines = TestMethodLines();
      if (lines.Count != otherLines.Count) {
        return false;
      }
      if (lines.Count == 0) {
        return true;
      }
      lines.RemoveAt(0);
      otherLines.RemoveAt(0);
      return string.Join("", lines) == string.Join("", otherLines);
    }
  }
}