using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GaussDB.BackendMessages;
using GaussDB.Util;
using static GaussDB.Util.Statics;

namespace GaussDB.Internal;

partial class GaussDBConnector
{
    async Task Authenticate(string username, GaussDBTimeout timeout, bool async, CancellationToken cancellationToken)
    {
        while (true)
        {
            timeout.CheckAndApply(this);
            var msg = ExpectAny<AuthenticationRequestMessage>(await ReadMessage(async).ConfigureAwait(false), this);
            switch (msg.AuthRequestType)
            {
            case AuthenticationRequestType.AuthenticationOk:
                return;

            case AuthenticationRequestType.AuthenticationCleartextPassword:
                await AuthenticateCleartext(username, async, cancellationToken).ConfigureAwait(false);
                break;

            case AuthenticationRequestType.AuthenticationMD5Password:
                await AuthenticateMD5(username, ((AuthenticationMD5PasswordMessage)msg).Salt, async, cancellationToken).ConfigureAwait(false);
                break;

            case AuthenticationRequestType.AuthenticationSASL:
                var authenticationSASLMessage = (AuthenticationSASLMessage)msg;
                await GaussDBAuthenticateSASLSha256(authenticationSASLMessage.Salt,
                    authenticationSASLMessage.Token,
                    authenticationSASLMessage.Iteration,
                    username, async,
                    cancellationToken).ConfigureAwait(false);
                break;

            case AuthenticationRequestType.AuthenticationGSS:
            case AuthenticationRequestType.AuthenticationSSPI:
                await DataSource.IntegratedSecurityHandler.NegotiateAuthentication(async, this).ConfigureAwait(false);
                return;

            case AuthenticationRequestType.AuthenticationGSSContinue:
                throw new GaussDBException("Can't start auth cycle with AuthenticationGSSContinue");

            default:
                throw new NotSupportedException($"Authentication method not supported (Received: {msg.AuthRequestType})");
            }
        }
    }

    async Task AuthenticateCleartext(string username, bool async, CancellationToken cancellationToken = default)
    {
        var passwd = await GetPassword(username, async, cancellationToken).ConfigureAwait(false) ?? throw new GaussDBException("No password has been provided but the backend requires one (in cleartext)");
        var encoded = new byte[Encoding.UTF8.GetByteCount(passwd) + 1];
        Encoding.UTF8.GetBytes(passwd, 0, passwd.Length, encoded, 0);

        await WritePassword(encoded, async, cancellationToken).ConfigureAwait(false);
        await Flush(async, cancellationToken).ConfigureAwait(false);
    }

    async Task GaussDBAuthenticateSASLSha256(string salt, string token, int iteration, string username, bool async, CancellationToken cancellationToken = default)
    {
        var passwd = GetPassword(username) ?? throw new GaussDBException($"No password has been provided but the backend requires one (in SASL/Sha256)");

        var saltbytes = HexStringToBytes(salt);

        var tokenbytes = HexStringToBytes(token);

        var passwordK = Hi(passwd.Normalize(NormalizationForm.FormKC), saltbytes, iteration);

        var clientKey = HMAC(passwordK, Encoding.ASCII.GetBytes("Client Key"));

        using var sha256 = SHA256.Create();

        var storedKey = sha256.ComputeHash(clientKey);

        var serverKey = HMAC(passwordK, Encoding.ASCII.GetBytes("Server Key"));

        var tokenKey = HMAC(storedKey, tokenbytes);

        var hmac = XOR_Between_Password(tokenKey, clientKey, clientKey.Length);

        var length = hmac.Length * 2 + 1;

        var result = new byte[length];

        BytesToHex(hmac, result, 0, hmac.Length);

        result[length - 1] = 0;

        await WriteSha256Response(result, async, cancellationToken).ConfigureAwait(false);

        await Flush(async, cancellationToken).ConfigureAwait(false);

        Expect<AuthenticationRequestMessage>(await ReadMessage(async).ConfigureAwait(false), this);

        static byte[] Hi(string password, byte[] salt, int iteration)
        {
            using var di = new Rfc2898DeriveBytes(password, salt, iteration);

            return di.GetBytes(32);
        }

        static byte[] XOR_Between_Password(byte[] password1, byte[] password2, int length)
        {
            var temp = new byte[length];
            for (var i = 0; i < length; ++i)
            {
                temp[i] = (byte)(password1[i] ^ password2[i]);
            }
            return temp;
        }

        static byte[] HMAC(byte[] data, byte[] key)
        {
            using var hmacsha256 = new HMACSHA256(data);
            return hmacsha256.ComputeHash(key);
        }
    }

    static void BytesToHex(byte[] bytes, byte[] hex, int offset, int length)
    {
        var lookup = new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };
        var pos = offset;
        for (var i = 0; i < length; ++i)
        {
            var c = bytes[i] & 255;
            var j = c >> 4;
            hex[pos++] = (byte)lookup[j];
            j = c & 15;
            hex[pos++] = (byte)lookup[j];
        }
    }

    static byte[] HexStringToBytes(string hex)
    {
        var NumberChars = hex.Length;

        var bytes = new byte[NumberChars / 2];

        for (var i = 0; i < NumberChars; i += 2)
        {
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
        return bytes;
    }

    string? GetPassword(string username)
    {
        var password = Settings.Password;

        if (!string.IsNullOrEmpty(password))
        {
            return password;
        }

        if (ProvidePasswordCallback is { } passwordCallback)
            try
            {
                password = passwordCallback(Host, Port, Settings.Database!, username);
            }
            catch (Exception e)
            {
                throw new GaussDBException($"Obtaining password using {nameof(GaussDBConnection)}.{nameof(ProvidePasswordCallback)} delegate failed", e);
            }

        password ??= PostgresEnvironment.Password;

        if (password != null)
        {
            return password;
        }

        var passFile = Settings.Passfile ?? PostgresEnvironment.PassFile ?? PostgresEnvironment.PassFileDefault;

        if (passFile != null)
        {
            var matchingEntry = new PgPassFile(passFile!)
                .GetFirstMatchingEntry(Host, Port, Settings.Database!, username);

            if (matchingEntry != null)
            {
                password = matchingEntry.Password;
            }
        }

        return password;
    }

    internal void AuthenticateSASLSha256Plus(ref string mechanism, ref string cbindFlag, ref string cbind,
        ref bool successfulBind)
    {
        // The check below is copied from libpq (with commentary)
        // https://github.com/postgres/postgres/blob/98640f960eb9ed80cf90de3ef5d2e829b785b3eb/src/interfaces/libpq/fe-auth.c#L507-L517

        // The server offered SCRAM-SHA-256-PLUS, but the connection
        // is not SSL-encrypted. That's not sane. Perhaps SSL was
        // stripped by a proxy? There's no point in continuing,
        // because the server will reject the connection anyway if we
        // try authenticate without channel binding even though both
        // the client and server supported it. The SCRAM exchange
        // checks for that, to prevent downgrade attacks.
        if (!IsSecure)
            throw new GaussDBException("Server offered SCRAM-SHA-256-PLUS authentication over a non-SSL connection");

        var sslStream = (SslStream)_stream;
        if (sslStream.RemoteCertificate is null)
        {
            ConnectionLogger.LogWarning("Remote certificate null, falling back to SCRAM-SHA-256");
            return;
        }

        using var remoteCertificate = new X509Certificate2(sslStream.RemoteCertificate);
        // Checking for hashing algorithms
        HashAlgorithm? hashAlgorithm = null;
        var algorithmName = remoteCertificate.SignatureAlgorithm.FriendlyName;
        if (algorithmName is null)
        {
            ConnectionLogger.LogWarning("Signature algorithm was null, falling back to SCRAM-SHA-256");
        }
        else if (algorithmName.StartsWith("sha1", StringComparison.OrdinalIgnoreCase) ||
                 algorithmName.StartsWith("md5", StringComparison.OrdinalIgnoreCase) ||
                 algorithmName.StartsWith("sha256", StringComparison.OrdinalIgnoreCase))
        {
            hashAlgorithm = SHA256.Create();
        }
        else if (algorithmName.StartsWith("sha384", StringComparison.OrdinalIgnoreCase))
        {
            hashAlgorithm = SHA384.Create();
        }
        else if (algorithmName.StartsWith("sha512", StringComparison.OrdinalIgnoreCase))
        {
            hashAlgorithm = SHA512.Create();
        }
        else
        {
            ConnectionLogger.LogWarning(
                $"Support for signature algorithm {algorithmName} is not yet implemented, falling back to SCRAM-SHA-256");
        }

        if (hashAlgorithm != null)
        {
            using var _ = hashAlgorithm;

            // RFC 5929
            mechanism = "SCRAM-SHA-256-PLUS";
            // PostgreSQL only supports tls-server-end-point binding
            cbindFlag = "p=tls-server-end-point";
            // SCRAM-SHA-256-PLUS depends on using ssl stream, so it's fine
            var cbindFlagBytes = Encoding.UTF8.GetBytes($"{cbindFlag},,");

            var certificateHash = hashAlgorithm.ComputeHash(remoteCertificate.GetRawCertData());
            var cbindBytes = new byte[cbindFlagBytes.Length + certificateHash.Length];
            cbindFlagBytes.CopyTo(cbindBytes, 0);
            certificateHash.CopyTo(cbindBytes, cbindFlagBytes.Length);
            cbind = Convert.ToBase64String(cbindBytes);
            successfulBind = true;
            IsScramPlus = true;
        }
    }

#if NET6_0_OR_GREATER
    static byte[] Hi(string str, byte[] salt, int count)
        => Rfc2898DeriveBytes.Pbkdf2(str, salt, count, HashAlgorithmName.SHA256, 256 / 8);
#endif

    static byte[] Xor(byte[] buffer1, byte[] buffer2)
    {
        for (var i = 0; i < buffer1.Length; i++)
            buffer1[i] ^= buffer2[i];
        return buffer1;
    }

    static byte[] HMAC(byte[] key, string data)
    {
        var dataBytes = Encoding.UTF8.GetBytes(data);
#if NET7_0_OR_GREATER
        return HMACSHA256.HashData(key, dataBytes);
#else
        using var ih = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        ih.AppendData(dataBytes);
        return ih.GetHashAndReset();
#endif
    }

    async Task AuthenticateMD5(string username, byte[] salt, bool async, CancellationToken cancellationToken = default)
    {
        var passwd = await GetPassword(username, async, cancellationToken).ConfigureAwait(false);
        if (passwd == null)
            throw new GaussDBException("No password has been provided but the backend requires one (in MD5)");

        byte[] result;
#if !NET7_0_OR_GREATER
        using (var md5 = MD5.Create())
#endif
        {
            // First phase
            var passwordBytes = GaussDBWriteBuffer.UTF8Encoding.GetBytes(passwd);
            var usernameBytes = GaussDBWriteBuffer.UTF8Encoding.GetBytes(username);
            var cryptBuf = new byte[passwordBytes.Length + usernameBytes.Length];
            passwordBytes.CopyTo(cryptBuf, 0);
            usernameBytes.CopyTo(cryptBuf, passwordBytes.Length);

            var sb = new StringBuilder();
#if NET7_0_OR_GREATER
            var hashResult = MD5.HashData(cryptBuf);
#else
            var hashResult = md5.ComputeHash(cryptBuf);
#endif
            foreach (var b in hashResult)
                sb.Append(b.ToString("x2"));

            var prehash = sb.ToString();

            var prehashbytes = GaussDBWriteBuffer.UTF8Encoding.GetBytes(prehash);
            cryptBuf = new byte[prehashbytes.Length + 4];

            Array.Copy(salt, 0, cryptBuf, prehashbytes.Length, 4);

            // 2.
            prehashbytes.CopyTo(cryptBuf, 0);

            sb = new StringBuilder("md5");
#if NET7_0_OR_GREATER
            hashResult = MD5.HashData(cryptBuf);
#else
            hashResult = md5.ComputeHash(cryptBuf);
#endif
            foreach (var b in hashResult)
                sb.Append(b.ToString("x2"));

            var resultString = sb.ToString();
            result = new byte[Encoding.UTF8.GetByteCount(resultString) + 1];
            Encoding.UTF8.GetBytes(resultString, 0, resultString.Length, result, 0);
            result[result.Length - 1] = 0;
        }

        await WritePassword(result, async, cancellationToken).ConfigureAwait(false);
        await Flush(async, cancellationToken).ConfigureAwait(false);
    }

#if NET7_0_OR_GREATER
    internal async Task AuthenticateGSS(bool async)
    {
        var targetName = $"{KerberosServiceName}/{Host}";

        using var authContext = new NegotiateAuthentication(new NegotiateAuthenticationClientOptions{ TargetName = targetName});
        var data = authContext.GetOutgoingBlob(ReadOnlySpan<byte>.Empty, out var statusCode)!;
        Debug.Assert(statusCode == NegotiateAuthenticationStatusCode.ContinueNeeded);
        await WritePassword(data, 0, data.Length, async, UserCancellationToken).ConfigureAwait(false);
        await Flush(async, UserCancellationToken).ConfigureAwait(false);
        while (true)
        {
            var response = ExpectAny<AuthenticationRequestMessage>(await ReadMessage(async).ConfigureAwait(false), this);
            if (response.AuthRequestType == AuthenticationRequestType.AuthenticationOk)
                break;
            if (response is not AuthenticationGSSContinueMessage gssMsg)
                throw new GaussDBException($"Received unexpected authentication request message {response.AuthRequestType}");
            data = authContext.GetOutgoingBlob(gssMsg.AuthenticationData.AsSpan(), out statusCode)!;
            if (statusCode is not NegotiateAuthenticationStatusCode.Completed and not NegotiateAuthenticationStatusCode.ContinueNeeded)
                throw new GaussDBException($"Error while authenticating GSS/SSPI: {statusCode}");
            // We might get NegotiateAuthenticationStatusCode.Completed but the data will not be null
            // This can happen if it's the first cycle, in which case we have to send that data to complete handshake (#4888)
            if (data is null)
                continue;
            await WritePassword(data, 0, data.Length, async, UserCancellationToken).ConfigureAwait(false);
            await Flush(async, UserCancellationToken).ConfigureAwait(false);
        }
    }
#endif

    async ValueTask<string?> GetPassword(string username, bool async, CancellationToken cancellationToken = default)
    {
        var password = await DataSource.GetPassword(async, cancellationToken).ConfigureAwait(false);

        if (password is not null)
            return password;

        if (ProvidePasswordCallback is { } passwordCallback)
        {
            try
            {
                ConnectionLogger.LogTrace($"Taking password from {nameof(ProvidePasswordCallback)} delegate");
                password = passwordCallback(Host, Port, Settings.Database!, username);
            }
            catch (Exception e)
            {
                throw new GaussDBException($"Obtaining password using {nameof(GaussDBConnection)}.{nameof(ProvidePasswordCallback)} delegate failed", e);
            }
        }

        password ??= PostgresEnvironment.Password;

        if (password != null)
            return password;

        var passFile = Settings.Passfile ?? PostgresEnvironment.PassFile ?? PostgresEnvironment.PassFileDefault;
        if (passFile != null)
        {
            var matchingEntry = new PgPassFile(passFile!)
                .GetFirstMatchingEntry(Host, Port, Settings.Database!, username);
            if (matchingEntry != null)
            {
                ConnectionLogger.LogTrace("Taking password from pgpass file");
                password = matchingEntry.Password;
            }
        }

        return password;
    }
}
