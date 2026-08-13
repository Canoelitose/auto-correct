using AutoCorrect.Core.Engines.Llm;

namespace AutoCorrect.Core.Tests.Cases;

public static class ModelCatalogueTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Models: the configured one is taken when it is installed", () =>
        {
            Assert.Equal(
                "qwen2.5:3b",
                ModelCatalogue.Choose("qwen2.5:3b", ["llama3.2:1b", "qwen2.5:3b", "mistral:7b"]));
        });

        runner.Add("Models: a different quantisation of the same model still matches", () =>
        {
            // What the user actually hits: the documented tag and the installed tag differ only
            // in decoration, and demanding an exact match sends them off to pull a duplicate.
            Assert.Equal(
                "qwen2.5:3b-instruct-q4_K_M",
                ModelCatalogue.Choose("qwen2.5:3b", ["qwen2.5:3b-instruct-q4_K_M"]));

            Assert.Equal(
                "qwen2.5:3b",
                ModelCatalogue.Choose("qwen2.5:3b-instruct-q4_K_M", ["qwen2.5:3b"]));
        });

        runner.Add("Models: nothing installed means no choice", () =>
        {
            Assert.Equal(null, ModelCatalogue.Choose("qwen2.5:3b", []));
        });

        runner.Add("Models: something else installed is used rather than failing", () =>
        {
            Assert.Equal("llama3.2:3b", ModelCatalogue.Choose("qwen2.5:3b", ["llama3.2:3b"]));
        });

        runner.Add("Models: the preferred family wins over the others", () =>
        {
            Assert.Equal(
                "qwen2.5:7b",
                ModelCatalogue.Choose("nicht-da", ["mistral:7b", "qwen2.5:7b", "gemma2:9b"]));
        });

        runner.Add("Models: within a family the smaller one wins, because it answers sooner", () =>
        {
            Assert.Equal(
                "qwen2.5:1.5b",
                ModelCatalogue.Choose("nicht-da", ["qwen2.5:7b", "qwen2.5:1.5b", "qwen2.5:3b"]));
        });

        runner.Add("Models: an embedding model is never chosen", () =>
        {
            // It has no chat endpoint at all; picking it would turn a clear error into a strange one.
            Assert.Equal(
                "llama3.2:3b",
                ModelCatalogue.Choose("nicht-da", ["nomic-embed-text", "llama3.2:3b"]));

            Assert.Equal(null, ModelCatalogue.Choose("nicht-da", ["nomic-embed-text", "bge-m3"]));
        });

        runner.Add("Models: an unknown family is still used when it is all there is", () =>
        {
            Assert.Equal("solar:10.7b", ModelCatalogue.Choose("nicht-da", ["solar:10.7b"]));
        });

        runner.Add("Models: the size is read out of the tag", () =>
        {
            Assert.Equal(3d, ModelCatalogue.SizeInBillions("qwen2.5:3b"));
            Assert.Equal(1.5d, ModelCatalogue.SizeInBillions("qwen2.5:1.5b"));
            Assert.Equal(70d, ModelCatalogue.SizeInBillions("llama3.1:70b-instruct"));
            Assert.Equal(10.7d, ModelCatalogue.SizeInBillions("solar:10.7b"));

            // No size in the name: must sort last, never ahead of a model known to be small.
            Assert.Equal(double.MaxValue, ModelCatalogue.SizeInBillions("mistral:latest"));
        });

        runner.Add("Models: a letter before the b is not a size", () =>
        {
            // "gemma2:2b" has a size, "codellama:latest" does not - and "b" occurs in plenty
            // of words that have nothing to do with parameter counts.
            Assert.Equal(2d, ModelCatalogue.SizeInBillions("gemma2:2b"));
            Assert.Equal(double.MaxValue, ModelCatalogue.SizeInBillions("phi:chat-turbo"));
        });
    }
}
