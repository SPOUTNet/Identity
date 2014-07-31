// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Security.DataProtection;

namespace Microsoft.AspNet.Identity.Owin
{
    /// <summary>
    ///     Token provider that uses an IDataProtector to generate encrypted tokens based off of the security stamp
    /// </summary>
    public class DataProtectorTokenProvider<TUser> : IUserTokenProvider<TUser>
        where TUser : class
    {
        /// <summary>
        ///     Constructor
        /// </summary>
        /// <param name="protector"></param>
        public DataProtectorTokenProvider(IDataProtector protector)
        {
            if (protector == null)
            {
                throw new ArgumentNullException("protector");
            }
            Protector = protector;
            TokenLifespan = TimeSpan.FromDays(1);
        }

        /// <summary>
        ///     IDataProtector for the token
        /// </summary>
        public IDataProtector Protector { get; private set; }

        /// <summary>
        ///     Lifespan after which the token is considered expired
        /// </summary>
        public TimeSpan TokenLifespan { get; set; }

        /// <summary>
        ///     Generate a protected string for a user
        /// </summary>
        /// <param name="purpose"></param>
        /// <param name="manager"></param>
        /// <param name="user"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public virtual async Task<string> GenerateAsync(string purpose, UserManager<TUser> manager, TUser user,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }
            var ms = new MemoryStream();
            using (var writer = ms.CreateWriter())
            {
                writer.Write(DateTimeOffset.UtcNow);
                var id = await manager.GetUserIdAsync(user, cancellationToken);
                writer.Write(id);
                writer.Write(purpose ?? "");
                string stamp = null;
                if (manager.SupportsUserSecurityStamp)
                {
                    stamp = await manager.GetSecurityStampAsync(user);
                }
                writer.Write(stamp ?? "");
            }
            var protectedBytes = Protector.Protect(ms.ToArray());
            return Convert.ToBase64String(protectedBytes);
        }

        /// <summary>
        ///     Return false if the token is not valid
        /// </summary>
        /// <param name="purpose"></param>
        /// <param name="token"></param>
        /// <param name="manager"></param>
        /// <param name="user"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public virtual async Task<bool> ValidateAsync(string purpose, string token, UserManager<TUser> manager,
            TUser user, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var unprotectedData = Protector.Unprotect(Convert.FromBase64String(token));
                var ms = new MemoryStream(unprotectedData);
                using (var reader = ms.CreateReader())
                {
                    var creationTime = reader.ReadDateTimeOffset();
                    var expirationTime = creationTime + TokenLifespan;
                    if (expirationTime < DateTimeOffset.UtcNow)
                    {
                        return false;
                    }

                    var userId = reader.ReadString();
                    var id = await manager.GetUserIdAsync(user, cancellationToken);
                    if (!String.Equals(userId, id))
                    {
                        return false;
                    }
                    var purp = reader.ReadString();
                    if (!String.Equals(purp, purpose))
                    {
                        return false;
                    }
                    var stamp = reader.ReadString();
                    if (reader.PeekChar() != -1)
                    {
                        return false;
                    }

                    if (manager.SupportsUserSecurityStamp)
                    {
                        var expectedStamp = await manager.GetSecurityStampAsync(user);
                        return stamp == expectedStamp;
                    }
                    return stamp == "";
                }
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch
            {
                // Do not leak exception
            }
            return false;
        }

        /// <summary>
        ///     Returns true if the provider can be used to generate tokens for this user
        /// </summary>
        /// <param name="manager"></param>
        /// <param name="user"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task<bool> IsValidProviderForUserAsync(UserManager<TUser> manager, TUser user,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(true);
        }

        /// <summary>
        ///     This provider no-ops by default when asked to notify a user
        /// </summary>
        /// <param name="token"></param>
        /// <param name="manager"></param>
        /// <param name="user"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task NotifyAsync(string token, UserManager<TUser> manager, TUser user,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(0);
        }
    }

    // Based on Levi's authentication sample
    internal static class StreamExtensions
    {
        internal static readonly Encoding DefaultEncoding = new UTF8Encoding(false, true);

        public static BinaryReader CreateReader(this Stream stream)
        {
            return new BinaryReader(stream, DefaultEncoding, true);
        }

        public static BinaryWriter CreateWriter(this Stream stream)
        {
            return new BinaryWriter(stream, DefaultEncoding, true);
        }

        public static DateTimeOffset ReadDateTimeOffset(this BinaryReader reader)
        {
            return new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
        }

        public static void Write(this BinaryWriter writer, DateTimeOffset value)
        {
            writer.Write(value.UtcTicks);
        }
    }
}