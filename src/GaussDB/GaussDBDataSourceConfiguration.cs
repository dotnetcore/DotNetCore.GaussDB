using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using GaussDB.Internal;

namespace GaussDB;

sealed record GaussDBDataSourceConfiguration(string? Name,
    GaussDBLoggingConfiguration LoggingConfiguration,
    TransportSecurityHandler TransportSecurityHandler,
    IntegratedSecurityHandler userCertificateValidationCallback,
    RemoteCertificateValidationCallback? UserCertificateValidationCallback,
    Action<X509CertificateCollection>? ClientCertificatesCallback,
    Func<GaussDBConnectionStringBuilder, string>? PasswordProvider,
    Func<GaussDBConnectionStringBuilder, CancellationToken, ValueTask<string>>? PasswordProviderAsync,
    Func<GaussDBConnectionStringBuilder, CancellationToken, ValueTask<string>>? PeriodicPasswordProvider,
    TimeSpan PeriodicPasswordSuccessRefreshInterval,
    TimeSpan PeriodicPasswordFailureRefreshInterval,
    IEnumerable<IPgTypeInfoResolver> ResolverChain,
    List<HackyEnumTypeMapping> HackyEnumMappings,
    IGaussDBNameTranslator DefaultNameTranslator,
    Action<GaussDBConnection>? ConnectionInitializer,
    Func<GaussDBConnection, Task>? ConnectionInitializerAsync);
