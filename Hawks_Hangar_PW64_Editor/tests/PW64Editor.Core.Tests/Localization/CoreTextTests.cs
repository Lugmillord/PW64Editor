using PW64Editor.Core.Localization;
using PW64Editor.Core.Text;

namespace PW64Editor.Core.Tests.Localization;

public class CoreTextTests
{
    // The translator is shared by all tests. The one used here only changes the texts of these
    // tests and leaves every other text as it is, so tests running at the same time are not affected.
    private static readonly Dictionary<string, string> Table = new()
    {
        ["Core text test {0}"] = "Kerntext-Test {0}",
        ["Core text broken {0}"] = "Kaputt {1}",
        ["The name must not be empty."] = "Der Name darf nicht leer sein.",
    };

    private static T WithTranslator<T>(Func<T> action)
    {
        Func<string, string>? before = CoreText.Translator;
        CoreText.Translator = english => Table.TryGetValue(english, out string? text) ? text : english;
        try
        {
            return action();
        }
        finally
        {
            CoreText.Translator = before;
        }
    }

    [Fact]
    public void WithoutTranslator_TextsStayEnglish()
    {
        Assert.Equal("Core text test 5", CoreText.F("Core text test {0}", 5));
        Assert.Equal("The name must not be empty.", CoreText.T("The name must not be empty."));
    }

    [Fact]
    public void Translator_TranslatesAndFillsIn()
    {
        Assert.Equal("Kerntext-Test 5", WithTranslator(() => CoreText.F("Core text test {0}", 5)));
    }

    [Fact]
    public void BrokenTranslation_FallsBackToEnglish()
    {
        Assert.Equal("Core text broken 5", WithTranslator(() => CoreText.F("Core text broken {0}", 5)));
    }

    [Fact]
    public void Mark_DoesNotTranslate()
    {
        Assert.Equal("The name must not be empty.", WithTranslator(() => CoreText.K("The name must not be empty.")));
    }

    [Fact]
    public void CoreMessages_UseTheTranslator()
    {
        Assert.Equal("Der Name darf nicht leer sein.", WithTranslator(() => CustomTexts.CheckName("", [])));
    }
}
