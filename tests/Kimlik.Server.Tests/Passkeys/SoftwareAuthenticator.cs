using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kimlik.Server.Tests.Passkeys;

/// <summary>
/// A passkey authenticator in software. It answers WebAuthn options as a browser with a platform authenticator does,
/// with real P-256 keys and signatures, so Kimlik verifies its answers as it would a device's. Each passkey counts its
/// signatures, as device-bound authenticators do; a cloned one can be simulated by turning a counter back.
/// </summary>
internal sealed class SoftwareAuthenticator(string origin = "http://localhost") : IDisposable
{
    private const byte UserPresent = 0x01;
    private const byte UserVerified = 0x04;
    private const byte BackupEligible = 0x08;
    private const byte BackedUp = 0x10;
    private const byte AttestedCredentialData = 0x40;

    private readonly List<Passkey> _passkeys = [];

    public IReadOnlyList<Passkey> Passkeys => _passkeys;

    /// <summary>Creates a passkey for the creation options and returns the answer as <c>credential.toJSON()</c> does.</summary>
    public string Create(JsonElement options, bool synced = false)
    {
        var rpId = options.GetProperty("rp").GetProperty("id").GetString()!;
        var passkey = new Passkey(RandomNumberGenerator.GetBytes(16), ECDsa.Create(ECCurve.NamedCurves.nistP256), rpId, options.GetProperty("user").GetProperty("id").GetString()!);
        _passkeys.Add(passkey);

        var key = passkey.Key.ExportParameters(includePrivateParameters: false);
        var publicKey = Cbor.Map((Cbor.Int(1), Cbor.Int(2)), (Cbor.Int(3), Cbor.Int(-7)), (Cbor.Int(-1), Cbor.Int(1)), (Cbor.Int(-2), Cbor.Bytes(key.Q.X!)), (Cbor.Int(-3), Cbor.Bytes(key.Q.Y!)));
        var flags = (byte)(UserPresent | UserVerified | AttestedCredentialData | (synced ? BackupEligible | BackedUp : 0));
        byte[] authenticatorData = [.. RpIdHash(rpId), flags, .. Counter(0), .. new byte[16], .. Length(passkey.Id), .. passkey.Id, .. publicKey];
        var attestation = Cbor.Map((Cbor.Text("fmt"), Cbor.Text("none")), (Cbor.Text("attStmt"), Cbor.Map()), (Cbor.Text("authData"), Cbor.Bytes(authenticatorData)));

        return Json(passkey, new JsonObject
        {
            ["clientDataJSON"] = Encode(ClientData("webauthn.create", options)),
            ["attestationObject"] = Encode(attestation),
            ["transports"] = new JsonArray("internal"),
        });
    }

    /// <summary>Signs in with <paramref name="passkey"/>, or the first one for the options' relying party.</summary>
    public string Get(JsonElement options, Passkey? passkey = null)
    {
        var rpId = options.GetProperty("rpId").GetString()!;
        passkey ??= _passkeys.First(candidate => candidate.RpId == rpId);
        passkey.SignCount++;

        byte[] authenticatorData = [.. RpIdHash(passkey.RpId), (byte)(UserPresent | UserVerified), .. Counter(passkey.SignCount)];
        var clientData = ClientData("webauthn.get", options);
        var signature = passkey.Key.SignData([.. authenticatorData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return Json(passkey, new JsonObject
        {
            ["clientDataJSON"] = Encode(clientData),
            ["authenticatorData"] = Encode(authenticatorData),
            ["signature"] = Encode(signature),
            ["userHandle"] = passkey.UserHandle,
        });
    }

    public void Dispose()
    {
        foreach (var passkey in _passkeys)
        {
            passkey.Key.Dispose();
        }
    }

    private byte[] ClientData(string type, JsonElement options) => Encoding.UTF8.GetBytes(new JsonObject
    {
        ["type"] = type,
        ["challenge"] = options.GetProperty("challenge").GetString(),
        ["origin"] = origin,
        ["crossOrigin"] = false,
    }.ToJsonString());

    private static string Json(Passkey passkey, JsonObject response) => new JsonObject
    {
        ["id"] = Encode(passkey.Id),
        ["rawId"] = Encode(passkey.Id),
        ["type"] = "public-key",
        ["response"] = response,
        ["clientExtensionResults"] = new JsonObject(),
        ["authenticatorAttachment"] = "platform",
    }.ToJsonString();

    private static byte[] RpIdHash(string rpId) => SHA256.HashData(Encoding.UTF8.GetBytes(rpId));

    private static byte[] Counter(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Length(byte[] value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value.Length);
        return bytes;
    }

    private static string Encode(byte[] value) => Base64Url.EncodeToString(value);

    internal sealed class Passkey(byte[] id, ECDsa key, string rpId, string userHandle)
    {
        public byte[] Id { get; } = id;

        public ECDsa Key { get; } = key;

        public string RpId { get; } = rpId;

        public string UserHandle { get; } = userHandle;

        public uint SignCount { get; set; }
    }

    /// <summary>Just enough CBOR (RFC 8949) for attestation objects and COSE keys: integers, strings and maps.</summary>
    private static class Cbor
    {
        public static byte[] Int(long value) => value >= 0 ? Head(0, (ulong)value) : Head(1, (ulong)(-1 - value));

        public static byte[] Bytes(byte[] value) => [.. Head(2, (ulong)value.Length), .. value];

        public static byte[] Text(string value) => [.. Head(3, (ulong)Encoding.UTF8.GetByteCount(value)), .. Encoding.UTF8.GetBytes(value)];

        public static byte[] Map(params (byte[] Key, byte[] Value)[] entries) =>
            [.. Head(5, (ulong)entries.Length), .. entries.SelectMany(entry => entry.Key.Concat(entry.Value))];

        private static byte[] Head(byte major, ulong value) => value switch
        {
            < 24 => [(byte)((major << 5) | (byte)value)],
            <= byte.MaxValue => [(byte)((major << 5) | 24), (byte)value],
            <= ushort.MaxValue => [(byte)((major << 5) | 25), (byte)(value >> 8), (byte)value],
            _ => [(byte)((major << 5) | 26), (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value],
        };
    }
}
