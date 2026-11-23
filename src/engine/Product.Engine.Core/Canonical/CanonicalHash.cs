using System;
using System.Security.Cryptography;

namespace Product.Engine.Core.Canonical;

// SHA-256 over canonical JSON (document 25, sections 6.2, 8 and 9).
// A hash is 64 lowercase hex characters. The same JSON value gives the same hash on net48, netstandard2.0 and net10.0.
//
// Content Hash (C02): the snapshot form, over the whole Content Json document.
// Whatever the configuration assembly puts in Content Json is hashed. If it keeps the version number out of
// Content Json, a revert reproduces the hash of the version it returns to.
//
// Request Hash (O24): the request form, over the request payload only.
// Leave the idempotency key, the correlation ID and other per-call values out of the payload, or a retry hashes differently.
public static class CanonicalHash
{
    public const int HexLength = 64;

    // Content Hash of a snapshot or bundle in JSON text.
    public static string ComputeContentHash(string snapshotJson)
    {
        return Sha256Hex(CanonicalJson.Canonicalize(snapshotJson, CanonicalProfile.Snapshot));
    }

    public static string ComputeContentHash(JsonTreeNode snapshot)
    {
        return Sha256Hex(CanonicalJson.Write(snapshot, CanonicalProfile.Snapshot));
    }

    // Request Hash of a request payload in JSON text.
    public static string ComputeRequestHash(string requestJson)
    {
        return Sha256Hex(CanonicalJson.Canonicalize(requestJson, CanonicalProfile.Request));
    }

    public static string ComputeRequestHash(JsonTreeNode request)
    {
        return Sha256Hex(CanonicalJson.Write(request, CanonicalProfile.Request));
    }

    // True when the snapshot's content hashes to the expected Content Hash (document 25, section 6.3, step 3).
    // A false answer maps to snapshot.hash-mismatch.
    public static bool VerifyContentHash(string snapshotJson, string expectedHash)
    {
        if (!IsHash(expectedHash))
        {
            return false;
        }

        return string.Equals(ComputeContentHash(snapshotJson), expectedHash, StringComparison.Ordinal);
    }

    // SHA-256 of text as UTF-8 without a byte order mark.
    public static string Sha256Hex(string text)
    {
        return Sha256Hex(CanonicalJson.ToUtf8(text));
    }

    public static string Sha256Hex(byte[] data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        byte[] digest;
        using (SHA256 sha = SHA256.Create())
        {
            digest = sha.ComputeHash(data);
        }

        var chars = new char[digest.Length * 2];
        for (int i = 0; i < digest.Length; i++)
        {
            chars[i * 2] = HexDigit(digest[i] >> 4);
            chars[(i * 2) + 1] = HexDigit(digest[i] & 0xF);
        }

        return new string(chars);
    }

    // True for 64 lowercase hex characters.
    public static bool IsHash(string? value)
    {
        if (value == null || value.Length != HexLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }

    private static char HexDigit(int value)
    {
        return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
    }
}
