namespace AutoCorrect.Core.Privacy;

/// <summary>
/// Answers whether a word is an ordinary word of the language.
///
/// This is what turns name detection from "names I have a list of" into "words that are not
/// words". In German every noun is capitalised, so capitalisation says nothing - but a
/// capitalised word that no dictionary knows is almost always a name.
///
/// The implementation lives in the Windows layer, because the dictionaries belong to Windows.
/// Core only needs the question, which also keeps it testable without a dictionary.
/// </summary>
public interface IWordKnowledge
{
    /// <summary>
    /// True when the word is a normal word of the language, or a plain misspelling of one.
    ///
    /// The second half matters: a misspelled noun must not be treated as a name, because then
    /// it would be replaced before sending and come back uncorrected. An implementation should
    /// therefore also answer true when the dictionary has a close suggestion for the word.
    /// </summary>
    bool IsDictionaryWord(string word);
}
