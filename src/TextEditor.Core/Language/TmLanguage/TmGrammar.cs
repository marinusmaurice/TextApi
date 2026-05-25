namespace TextEditor.Core.Language.TmLanguage;

internal sealed class TmRule
{
    public string?      Name        { get; set; }  // scope name
    public string?      ContentName { get; set; }  // scope name for content between begin/end
    public string?      Match       { get; set; }  // regex for match rules
    public string?      Begin       { get; set; }  // regex for begin/end rules
    public string?      End         { get; set; }  // regex for begin/end rules (may have \1 etc)
    public List<TmRule> Patterns    { get; set; } = [];
    public string?      Include     { get; set; }  // "$self" or "#ruleName"
}

internal sealed class TmGrammar
{
    public string                     ScopeName  { get; set; } = "";
    public string                     LanguageId { get; set; } = "";
    public List<TmRule>               Patterns   { get; set; } = [];
    public Dictionary<string, TmRule> Repository { get; set; } = [];
}
