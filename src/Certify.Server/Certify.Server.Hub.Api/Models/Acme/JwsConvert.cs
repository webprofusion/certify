
/// <summary>
/// Base64 URL encoding/decoding utility for JWS (JSON Web Signature)
/// </summary>
public static class JwsConvert
{
    /// <summary>
    /// Encodes the data to the base64url string without padding.
    /// </summary>
    /// <param name="data">The data to encode.</param>
    /// <returns>The encoded data.</returns>
    public static string ToBase64String(byte[] data)
    {
        var s = Convert.ToBase64String(data); // Regular base64 encoder
        s = s.Split('=')[0]; // Remove any trailing '='s
        s = s.Replace('+', '-'); // 62nd char of encoding
        s = s.Replace('/', '_'); // 63rd char of encoding
        return s;
    }

    /// <summary>
    /// Decodes the base64url string without padding.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>The decoded data.</returns>
    /// <exception cref="System.ArgumentException">If <paramref name="data"/> is illegal base64 URL string.</exception>
    public static byte[] FromBase64String(string data)
    {
        var s = data;
        s = s.Replace('-', '+'); // 62nd char of encoding
        s = s.Replace('_', '/'); // 63rd char of encoding
        switch (s.Length % 4) // Pad with trailing '='s
        {
            case 0: break; // No pad chars in this case
            case 2: s += "=="; break; // Two pad chars
            case 3: s += "="; break; // One pad char
            default:
                throw new ArgumentException("Invalid base64url string");
        }

        return Convert.FromBase64String(s); // Standard base64 decoder
    }
}
