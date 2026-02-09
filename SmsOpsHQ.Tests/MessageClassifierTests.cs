using SmsOpsHQ.Core.Utilities;
using Xunit;

namespace SmsOpsHQ.Tests;

public class MessageClassifierTests
{
    // --- Classify: Directions (highest priority) ---

    [Theory]
    [InlineData("Check out maps.google.com/abc123")]
    [InlineData("Visit goo.gl/maps/xyz for our location")]
    [InlineData("See google.com/maps for route")]
    [InlineData("Open apple.com/maps to find us")]
    [InlineData("Our store is on maps.apple.com/store")]
    [InlineData("Here are directions to our shop")]
    [InlineData("Click here for directions")]
    [InlineData("Get directions from your phone")]
    [InlineData("Find us at 123 Main Street")]
    [InlineData("We are located at the corner of 5th and Main")]
    public void Classify_DirectionPatterns_ReturnsDirections(string body)
    {
        string result = MessageClassifier.Classify(body);
        Assert.Equal("directions", result);
    }

    // --- Classify: Promotions ---

    [Theory]
    [InlineData("We are 5 \u2B50 rated on Google!")]
    [InlineData("Come in - we are highly rated!!")]
    [InlineData("Do you have pawns at another pawnshop?")]
    [InlineData("Visit King Gold and Pawn today!")]
    [InlineData("We offer top prices for your items")]
    [InlineData("Trusted for over 30 years")]
    [InlineData("We have the best prices in town")]
    [InlineData("Highest prices paid for gold")]
    [InlineData("We pay more than anyone else")]
    [InlineData("Bring your items to us today")]
    [InlineData("Special offer this weekend only")]
    [InlineData("Limited time deal on all jewelry")]
    [InlineData("SALE 50% off all merchandise")]
    [InlineData("Everything is 20% off today")]
    public void Classify_PromotionPatterns_ReturnsPromotions(string body)
    {
        string result = MessageClassifier.Classify(body);
        Assert.Equal("promotions", result);
    }

    // --- Classify: Reminders ---

    [Theory]
    [InlineData("Your ticket #12345 is due tomorrow")]
    [InlineData("Ticket #: 98765 expires in 3 days")]
    [InlineData("Reminder: #12345 pickup available")]
    [InlineData("ticket 12345 is ready")]
    [InlineData("Your pawn #12345 is expiring soon")]
    [InlineData("pawn 98765 renewal notice")]
    [InlineData("Please renew ticket #123456")]
    public void Classify_ReminderPatterns_ReturnsReminder(string body)
    {
        string result = MessageClassifier.Classify(body);
        Assert.Equal("reminder", result);
    }

    // --- Classify: General ---

    [Theory]
    [InlineData("Hello, how are you?")]
    [InlineData("Thank you for your message")]
    [InlineData("Yes, I'll be there at 3pm")]
    [InlineData("Can I ask a question?")]
    public void Classify_NoPatternMatch_ReturnsGeneral(string body)
    {
        string result = MessageClassifier.Classify(body);
        Assert.Equal("general", result);
    }

    // --- Classify: Null / Empty / Whitespace ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Classify_NullOrWhitespace_ReturnsGeneral(string? body)
    {
        string result = MessageClassifier.Classify(body);
        Assert.Equal("general", result);
    }

    // --- Classify: Case insensitivity ---

    [Fact]
    public void Classify_UpperCase_StillMatchesDirections()
    {
        string result = MessageClassifier.Classify("GET DIRECTIONS TO OUR STORE");
        Assert.Equal("directions", result);
    }

    [Fact]
    public void Classify_MixedCase_StillMatchesPromotion()
    {
        string result = MessageClassifier.Classify("SPECIAL OFFER This Weekend!");
        Assert.Equal("promotions", result);
    }

    [Fact]
    public void Classify_MixedCase_StillMatchesReminder()
    {
        string result = MessageClassifier.Classify("Your TICKET #12345 is due");
        Assert.Equal("reminder", result);
    }

    // --- Priority order: directions > promotions > reminder > general ---

    [Fact]
    public void Classify_DirectionsBeatsPromotion_WhenBothMatch()
    {
        // "find us at" appears in both directions and promotions.
        // Directions should win.
        string result = MessageClassifier.Classify("Find us at the sale event!");
        Assert.Equal("directions", result);
    }

    [Fact]
    public void Classify_PromotionsBeatsReminder_WhenBothMatch()
    {
        // Contains both a promotion pattern and a ticket reference.
        string result = MessageClassifier.Classify("Special offer on ticket #12345 renewal!");
        Assert.Equal("promotions", result);
    }

    // --- Milestone manual test cases ---

    [Fact]
    public void Classify_MilestoneTestCases()
    {
        Assert.Equal("reminder", MessageClassifier.Classify("Your ticket #12345 is due"));
        Assert.Equal("directions", MessageClassifier.Classify("Here are directions: maps.google.com"));
        Assert.Equal("promotions", MessageClassifier.Classify("SALE 50% off"));
        Assert.Equal("general", MessageClassifier.Classify("Hello"));
    }

    // --- GetDisplayName ---

    [Theory]
    [InlineData("reminder", "Reminders")]
    [InlineData("directions", "Directions")]
    [InlineData("promotions", "Promotions")]
    [InlineData("general", "General")]
    [InlineData("unknown", "General")]
    [InlineData("", "General")]
    public void GetDisplayName_ReturnsCorrectName(string category, string expectedName)
    {
        string result = MessageClassifier.GetDisplayName(category);
        Assert.Equal(expectedName, result);
    }

    // --- GetValidCategories ---

    [Fact]
    public void GetValidCategories_ReturnsFourCategories()
    {
        List<string> categories = MessageClassifier.GetValidCategories();
        Assert.Equal(4, categories.Count);
        Assert.Contains("reminder", categories);
        Assert.Contains("directions", categories);
        Assert.Contains("promotions", categories);
        Assert.Contains("general", categories);
    }
}
