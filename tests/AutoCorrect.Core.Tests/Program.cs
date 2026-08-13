using AutoCorrect.Core.Tests;
using AutoCorrect.Core.Tests.Cases;

var runner = new TestRunner();

CorrectionApplierTests.Register(runner);
HotkeyDefinitionTests.Register(runner);
SettingsStoreTests.Register(runner);
LanguageToolEngineTests.Register(runner);
EngineRouterTests.Register(runner);
LlmEngineTests.Register(runner);
ModelCatalogueTests.Register(runner);
ResponseFilterTests.Register(runner);
ResultCacheTests.Register(runner);
LoggingTests.Register(runner);
LocalizationTests.Register(runner);

// Only registered when AUTOCORRECT_LT_ENDPOINT points at a real LanguageTool server.
if (LanguageToolIntegrationTests.IsEnabled)
{
    LanguageToolIntegrationTests.Register(runner);
    Console.WriteLine($"LanguageTool integration tests enabled against {Environment.GetEnvironmentVariable(LanguageToolIntegrationTests.EndpointVariable)}");
}

// The same for AUTOCORRECT_LLM_ENDPOINT and a real Ollama or llama.cpp server.
if (LlmIntegrationTests.IsEnabled)
{
    LlmIntegrationTests.Register(runner);
    Console.WriteLine($"Model integration tests enabled against {Environment.GetEnvironmentVariable(LlmIntegrationTests.EndpointVariable)}");
}

Console.WriteLine("AutoCorrect.Core tests");
Console.WriteLine();

return await runner.RunAsync(args.Length > 0 ? args[0] : null);
