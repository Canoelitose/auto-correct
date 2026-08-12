using AutoCorrect.Core.Tests;
using AutoCorrect.Core.Tests.Cases;

var runner = new TestRunner();

CorrectionApplierTests.Register(runner);
HotkeyDefinitionTests.Register(runner);
SettingsStoreTests.Register(runner);
LanguageToolEngineTests.Register(runner);
EngineRouterTests.Register(runner);
LoggingTests.Register(runner);

Console.WriteLine("AutoCorrect.Core tests");
Console.WriteLine();

return await runner.RunAsync(args.Length > 0 ? args[0] : null);
