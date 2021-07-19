﻿namespace Microsoft.Dafny.LanguageServer.Workspace.Notifications {
  /// <summary>
  /// Enumeration that identifies that actual compilation status.
  /// </summary>
  public enum CompilationStatus {
    ParsingFailed,
    ResolutionFailed,
    VerificationStarted,
    VerificationFailed,
    VerificationSucceeded
  }
}
