using Kimlik.Domain.Users;

namespace Kimlik.Domain.Tests.Users;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StartsActiveUser_WithTimeOrderedId()
    {
        var user = User.Create(" ada@example.com ", "Ada", "Lovelace", "en", Now);

        user.Id.Version.ShouldBe(7);
        user.Email.ShouldBe("ada@example.com");
        user.Status.ShouldBe(UserStatus.Active);
        user.CanSignIn.ShouldBeTrue();
        user.CreatedAt.ShouldBe(Now);
        user.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Create_UsesIdAsInternalUserName()
    {
        var user = User.Create("ada@example.com", null, null, null, Now);

        user.UserName.ShouldBe(user.Id.ToString("N"));
    }

    [Theory]
    [InlineData("Ada", "Lovelace", "Ada Lovelace")]
    [InlineData("Ada", null, "Ada")]
    [InlineData(null, "Lovelace", "Lovelace")]
    [InlineData(" ", "  ", null)]
    public void Name_CombinesKnownNameParts(string? givenName, string? familyName, string? expectedName)
    {
        var user = User.Create("ada@example.com", givenName, familyName, null, Now);

        user.Name.ShouldBe(expectedName);
    }
}
