using Spotnet.Mac.DAL;
using Spotnet.Mac.ViewModels;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// The reply form's toolbar writes the same UBB the Windows one does.
/// </summary>
public class CommentComposerTests
{
    private static SpotDetailViewModel Composer() =>
        new(new SpotDatabaseService(new MacSqliteDb(System.IO.Path.GetTempFileName())));

    [Theory]
    [InlineData("b", "[b]vet[/b]")]
    [InlineData("i", "[i]vet[/i]")]
    [InlineData("u", "[u]vet[/u]")]
    [InlineData("url", "[url=]vet[/url]")]
    public void ToolbarWrapsTheSelection(string tag, string expected)
    {
        var vm = Composer();
        vm.NewCommentText = "vet";
        vm.CommentSelectionStart = 0;
        vm.CommentSelectionEnd = 3;

        vm.WrapCommentCommand.Execute(tag);

        Assert.Equal(expected, vm.NewCommentText);
    }

    [Fact]
    public void ColourUsesTheCodeFromTheToolbarField()
    {
        var vm = Composer();
        vm.NewCommentText = "rood";
        vm.CommentSelectionStart = 0;
        vm.CommentSelectionEnd = 4;
        vm.CommentColor = "ff0000";

        vm.WrapCommentCommand.Execute("color");

        Assert.Equal("[color=#ff0000]rood[/color]", vm.NewCommentText);
    }

    [Fact]
    public void WithNothingSelectedTheTagsLandAtTheCaret()
    {
        var vm = Composer();
        vm.NewCommentText = "dank";
        vm.CommentSelectionStart = vm.CommentSelectionEnd = 4;

        vm.WrapCommentCommand.Execute("b");

        Assert.Equal("dank[b][/b]", vm.NewCommentText);
    }

    [Fact]
    public void SmileysInsertTheirUbbTagAndPreviewAsEmoji()
    {
        var vm = Composer();
        vm.NewCommentText = "dank";
        vm.CommentSelectionStart = vm.CommentSelectionEnd = 4;

        vm.InsertSmileyCommand.Execute("buigen");

        Assert.Equal("dank[img=buigen]", vm.NewCommentText);
        Assert.Equal("dank\U0001F647", vm.CommentPreview);
    }

    [Fact]
    public void PreviewResolvesLineBreaksAndStyling()
    {
        var vm = Composer();
        vm.NewCommentText = "[b]Top[/b][br]40 mapjes";

        Assert.Equal("Top\n40 mapjes", vm.CommentPreview);
    }

    [Fact]
    public void EveryBundledSmileyIsOfferedInThePicker()
    {
        // The 30 GIFs Windows ships in Data/Images/smileys.
        var smileys = Composer().Smileys;

        Assert.Equal(30, smileys.Count);
        Assert.Contains(smileys, s => s.Key == "buigen");
        Assert.Contains(smileys, s => s.Key == "schater");
        Assert.All(smileys, s => Assert.False(string.IsNullOrEmpty(s.Value)));
    }
}
