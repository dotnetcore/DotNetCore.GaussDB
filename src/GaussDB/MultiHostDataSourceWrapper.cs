using GaussDB.Internal;
using GaussDB.Util;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;

namespace GaussDB;

sealed class MultiHostDataSourceWrapper : GaussDBDataSource
{
    internal override bool OwnsConnectors => false;

    readonly GaussDBMultiHostDataSource _wrappedSource;

    public MultiHostDataSourceWrapper(GaussDBMultiHostDataSource source, TargetSessionAttributes targetSessionAttributes)
        : base(CloneSettingsForTargetSessionAttributes(source.Settings, targetSessionAttributes), source.Configuration)
        => _wrappedSource = source;

    static GaussDBConnectionStringBuilder CloneSettingsForTargetSessionAttributes(
        GaussDBConnectionStringBuilder settings,
        TargetSessionAttributes targetSessionAttributes)
    {
        var clonedSettings = settings.Clone();
        clonedSettings.TargetSessionAttributesParsed = targetSessionAttributes;
        return clonedSettings;
    }

    internal override (int Total, int Idle, int Busy) Statistics => _wrappedSource.Statistics;

    internal override void Clear() => _wrappedSource.Clear();
    internal override ValueTask<GaussDBConnector> Get(GaussDBConnection conn, GaussDBTimeout timeout, bool async, CancellationToken cancellationToken)
        => _wrappedSource.Get(conn, timeout, async, cancellationToken);
    internal override bool TryGetIdleConnector([NotNullWhen(true)] out GaussDBConnector? connector)
        => throw new GaussDBException("GaussDB bug: trying to get an idle connector from " + nameof(MultiHostDataSourceWrapper));
    internal override ValueTask<GaussDBConnector?> OpenNewConnector(GaussDBConnection conn, GaussDBTimeout timeout, bool async, CancellationToken cancellationToken)
        => throw new GaussDBException("GaussDB bug: trying to open a new connector from " + nameof(MultiHostDataSourceWrapper));
    internal override void Return(GaussDBConnector connector)
        => _wrappedSource.Return(connector);

    internal override void AddPendingEnlistedConnector(GaussDBConnector connector, Transaction transaction)
        => _wrappedSource.AddPendingEnlistedConnector(connector, transaction);
    internal override bool TryRemovePendingEnlistedConnector(GaussDBConnector connector, Transaction transaction)
        => _wrappedSource.TryRemovePendingEnlistedConnector(connector, transaction);
    internal override bool TryRentEnlistedPending(Transaction transaction, GaussDBConnection connection,
        [NotNullWhen(true)] out GaussDBConnector? connector)
        => _wrappedSource.TryRentEnlistedPending(transaction, connection, out connector);
}
