﻿using Microsoft.Dafny.LanguageServer.Workspace.Notifications;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace Microsoft.Dafny.LanguageServer.Workspace {
  public class CompilationStatusNotificationPublisher : ICompilationStatusNotificationPublisher {
    private readonly ITextDocumentLanguageServer _languageServer;

    public CompilationStatusNotificationPublisher(ITextDocumentLanguageServer languageServer) {
      _languageServer = languageServer;
    }

    public void ParsingFailed(TextDocumentItem textDocument) {
      SendStatusNotification(textDocument, CompilationStatus.ParsingFailed);
    }

    public void ResolutionFailed(TextDocumentItem textDocument) {
      SendStatusNotification(textDocument, CompilationStatus.ResolutionFailed);
    }

    public void VerificationStarted(TextDocumentItem textDocument) {
      SendStatusNotification(textDocument, CompilationStatus.VerificationStarted);
    }

    public void VerificationFailed(TextDocumentItem textDocument) {
      SendStatusNotification(textDocument, CompilationStatus.VerificationFailed);
    }

    public void VerificationSucceeded(TextDocumentItem textDocument) {
      SendStatusNotification(textDocument, CompilationStatus.VerificationSucceeded);
    }

    private void SendStatusNotification(TextDocumentItem textDocument, CompilationStatus status) {
      _languageServer.SendNotification(new CompilationStatusParams {
        Uri = textDocument.Uri,
        Version = textDocument.Version,
        Status = status
      });
    }
  }
}
