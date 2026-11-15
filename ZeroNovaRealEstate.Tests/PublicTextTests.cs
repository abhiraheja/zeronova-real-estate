using ZeroNovaRealEstate.Application.Features.Common;

namespace ZeroNovaRealEstate.Tests;

/// <summary>What a customer may read: the place and the property, never a way round the dealer.</summary>
public sealed class PublicTextTests
{
    [Theory]
    [InlineData("Call Sharma ji 98765 43210 for keys", "Call Sharma ji for keys")]
    [InlineData("Owner +91 98765-43210, after 6 pm", "Owner, after 6 pm")]
    [InlineData("Contact 9876543210 / owner@gmail.com", "Contact")]
    [InlineData("Office 0172-2701234", "Office")]
    [InlineData("Mail me: rajesh.kumar@yahoo.co.in", "Mail me")]
    public void Contacts_never_reach_the_public(string typed, string shown) => Assert.Equal(shown, PublicText.Listing(typed));

    [Theory]
    [InlineData("H.No 1234, Sector 22, Chandigarh", "Sector 22, Chandigarh")]
    [InlineData("House No. 1234-B, Phase 11, Mohali", "Phase 11, Mohali")]
    [InlineData("#1234, Sector 22-C, Chandigarh", "Sector 22-C, Chandigarh")]
    [InlineData("Flat C-1204, Tower C, Garden Phase", "Tower C, Garden Phase")]
    [InlineData("Flat No: 1204, Green Valley", "Green Valley")]
    [InlineData("Plot No 45/2, Aerocity", "Aerocity")]
    [InlineData("SCO 123, Sector 17", "Sector 17")]
    [InlineData("Kothi no 7 corner, park facing", "corner, park facing")]
    [InlineData("मकान नंबर 1234, सेक्टर 22", "सेक्टर 22")]
    public void The_exact_unit_never_reaches_the_public(string typed, string shown) => Assert.Equal(shown, PublicText.Listing(typed));

    [Theory]
    [InlineData("Corner, Sector 22, Chandigarh, Phase 11, Garden Phase")]
    [InlineData("5 marla kothi, 5 BHK, park facing")]
    [InlineData("Flat 3 BHK on 2nd floor, lift")]
    [InlineData("Plot 200 gaj, 30 ft road")]
    [InlineData("House 5 marla, double storey")]
    [InlineData("Price ₹1,60,00,000 negotiable")]
    [InlineData("Built 2019, possession 2026-12")]
    [InlineData("Sector 70, 1 kanal, 40x90")]
    public void The_place_and_the_property_stay(string typed) => Assert.Equal(typed, PublicText.Listing(typed));

    [Fact]
    public void A_builders_project_keeps_its_address_and_loses_only_contacts() =>
        Assert.Equal("Plot No 12, Sector 82, Mohali. Sales office", PublicText.Contact("Plot No 12, Sector 82, Mohali. Sales office 98765 43210"));

    [Fact]
    public void Lines_stay_lines_and_nothing_left_is_null()
    {
        Assert.Equal("Sector 22\nCorner", PublicText.Listing("H.No 12, Sector 22\nCorner, 9876543210"));
        Assert.Null(PublicText.Listing("9876543210"));
        Assert.Equal("P-0001 House", PublicText.ListingOr("P-0001 House"));
    }

    [Theory]
    [InlineData("https://youtu.be/abc", "https://youtu.be/abc")]
    [InlineData("call 9876543210", null)]
    [InlineData("youtube.com/watch?v=1", null)]
    public void A_video_link_is_only_a_web_address(string typed, string? shown) => Assert.Equal(shown, PublicText.Link(typed));
}
