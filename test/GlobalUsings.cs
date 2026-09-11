// Test-only global imports. This file is compiled in Debug development builds only
// (export builds exclude test\**\*.cs), so these using directives never affect the
// exported game assembly. Root GlobalUsings.cs still supplies LanguageExt, ZLinq,
// and the shared aliases for the whole compilation.
global using FunProject.Tests;
global using static FunProject.Tests.TestData;
