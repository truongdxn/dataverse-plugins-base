using System.Security.Cryptography;
using System.Text;

namespace Dataverse.Plugins.Tooling.Infrastructure;

/// <summary>
/// Derives stable GUIDs from names, using the RFC 4122 version 5 (SHA-1) scheme.
/// <para>
/// This is what lets the two deployment paths agree. A step registered directly by
/// <c>dv sync</c> and the same step arriving inside a solution import carry the identical
/// <c>sdkmessageprocessingstepid</c>, so re-running either is idempotent and neither creates
/// a duplicate of the other's records.
/// </para>
/// <para>
/// SHA-1 is used because RFC 4122 specifies it for version 5, not as a security choice - these
/// ids are identifiers, never secrets.
/// </para>
/// </summary>
public static class DeterministicGuid
{
    /// <summary>
    /// Namespace for every id this tool derives. Changing this value re-identifies every
    /// component in every environment, so it must stay fixed for the life of the repo.
    /// </summary>
    private static readonly Guid ToolNamespace = new("6f8b8f2e-1d3a-4c9b-9e5f-2a7c4d6e8b10");

    /// <summary>
    /// Builds an id from the given parts. Parts are lower-cased and trimmed so that a casing
    /// change in a type or message name does not silently produce a different component.
    /// </summary>
    public static Guid Create(params string[] parts)
    {
        var name = string.Join(
            "|",
            parts.Select(part => (part ?? string.Empty).Trim().ToLowerInvariant()));

        return CreateVersion5(ToolNamespace, name);
    }

    private static Guid CreateVersion5(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        ToBigEndian(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);

        var buffer = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, buffer, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, buffer, namespaceBytes.Length, nameBytes.Length);

        var hash = SHA1.HashData(buffer);

        var result = new byte[16];
        Array.Copy(hash, 0, result, 0, 16);

        result[6] = (byte)((result[6] & 0x0F) | 0x50); // version 5
        result[8] = (byte)((result[8] & 0x3F) | 0x80); // RFC 4122 variant

        ToBigEndian(result);
        return new Guid(result);
    }

    /// <summary>
    /// Guid.ToByteArray lays the first three fields out little-endian; RFC 4122 hashes them
    /// big-endian. Swapping in place converts between the two (and back again).
    /// </summary>
    private static void ToBigEndian(byte[] guid)
    {
        Array.Reverse(guid, 0, 4);
        Array.Reverse(guid, 4, 2);
        Array.Reverse(guid, 6, 2);
    }
}
