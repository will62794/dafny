// RUN: %dafny_0 /compile:0 "%s" > "%t"
// RUN: %diff "%s.expect" "%t"


module A {
  module B {
    module C {
      import A.B
    }
  }

}

