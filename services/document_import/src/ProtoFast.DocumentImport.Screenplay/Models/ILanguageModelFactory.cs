namespace ProtoFast.DocumentImport.Screenplay.Models;

public interface ILanguageModelFactory
{
    /// <param name="modelClass">One of <see cref="Engine.Executors.ModelClasses"/>.</param>
    ILanguageModel For(string modelClass);
}
