using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Server.Tests.Security;

public sealed class DataProtectionKeyRingTests(KimlikServerFixture server)
{
    [Fact]
    public async Task KeyRing_IsStoredInDatabase_EncryptedWithMasterKey()
    {
        var protector = server.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Kimlik.Tests");
        protector.Unprotect(protector.Protect("payload")).ShouldBe("payload");

        var keys = await server.QueryDatabaseAsync(context => context.DataProtectionKeys.Select(key => key.Xml).ToListAsync());

        keys.ShouldNotBeEmpty();
        keys.ShouldAllBe(xml => xml != null && xml.Contains("MasterKeyXmlDecryptor") && !xml.Contains("<masterKey"));
    }
}
