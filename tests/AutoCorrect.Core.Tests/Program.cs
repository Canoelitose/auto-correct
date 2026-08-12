using AutoCorrect.Core.Tests;
using AutoCorrect.Core.Tests.Cases;

var runner = new TestRunner();

CorrectionApplierTests.Register(runner);
HotkeyDefinitionTests.Register(runner);
SettingsStoreTests.Register(runner);
LanguageToolEngineTests.Register(runner);
EngineRouterTests.Register(runner);
LoggingTests.Register(runner);

// Only registered when AUTOCORRECT_LT_ENDPOINT points at a real LanguageTool server.
if (LanguageToolIntegrationTests.IsEnabled)
{
    LanguageToolIntegrationTests.Register(runner);
    Console.WriteLine($"Integration tests enabled against {Environment.GetEnvironmentVariable(LanguageToolIntegrationTests.EndpointVariable)}");
}

Console.WriteLine("AutoCorrect.Core tests");
Console.WriteLine();

return await runner.RunAsync(args.Length > 0 ? args[0] : null);
