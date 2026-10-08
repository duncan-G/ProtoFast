namespace ProtoFast.DocumentImport.Screenplay.Models;

public interface ILanguageModelFactory
{
    /// <param name="modelClass">One of <see cref="Engine.Executors.ModelClasses"/>.</param>
    ILanguageModel For(string modelClass);

    /// <summary>On one provider, whatever the configured default, and with no fallback.</summary>
    ILanguageModel For(string modelClass, string provider);
}
