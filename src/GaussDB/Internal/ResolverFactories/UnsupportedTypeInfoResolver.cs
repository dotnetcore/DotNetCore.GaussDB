using System;
using System.Collections;
using GaussDB.Internal.Postgres;
using GaussDB.PostgresTypes;
using GaussDB.Properties;

namespace GaussDB.Internal.ResolverFactories;

sealed class UnsupportedTypeInfoResolver<TBuilder> : IPgTypeInfoResolver
{
    public PgTypeInfo? GetTypeInfo(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options)
    {
        if (options.IntrospectionMode)
            return null;

        RecordTypeInfoResolverFactory.CheckUnsupported<TBuilder>(type, dataTypeName, options);
        AdoTypeInfoResolverFactory.ThrowIfRangeUnsupported<TBuilder>(type, dataTypeName, options);
        AdoTypeInfoResolverFactory.ThrowIfMultirangeUnsupported<TBuilder>(type, dataTypeName, options);
        FullTextSearchTypeInfoResolverFactory.CheckUnsupported<TBuilder>(type, dataTypeName, options);
        LTreeTypeInfoResolverFactory.CheckUnsupported<TBuilder>(type, dataTypeName, options);

        if (type is null)
            return null;

        // These checks are here because their resolver types have RUC/RDC
        if (type != typeof(object))
        {
            switch (dataTypeName)
            {
            case "pg_catalog.json" or "pg_catalog.jsonb":
                throw new NotSupportedException(
                    string.Format(
                        GaussDBStrings.DynamicJsonNotEnabled,
                        type == typeof(object) ? "<unknown>" : type.Name,
                        nameof(GaussDBSlimDataSourceBuilder.EnableDynamicJson),
                        typeof(TBuilder).Name));

            case not null when options.DatabaseInfo.GetPostgresType(dataTypeName) is PostgresEnumType:
                throw new NotSupportedException(
                    string.Format(
                        GaussDBStrings.UnmappedEnumsNotEnabled,
                        nameof(GaussDBSlimDataSourceBuilder.EnableUnmappedTypes),
                        typeof(TBuilder).Name));

            case not null when options.DatabaseInfo.GetPostgresType(dataTypeName) is PostgresRangeType:
                throw new NotSupportedException(
                    string.Format(
                        GaussDBStrings.UnmappedRangesNotEnabled,
                        nameof(GaussDBSlimDataSourceBuilder.EnableUnmappedTypes),
                        typeof(TBuilder).Name));

            case not null when options.DatabaseInfo.GetPostgresType(dataTypeName) is PostgresMultirangeType:
                throw new NotSupportedException(
                    string.Format(
                        GaussDBStrings.UnmappedRangesNotEnabled,
                        nameof(GaussDBSlimDataSourceBuilder.EnableUnmappedTypes),
                        typeof(TBuilder).Name));
            }
        }

        if (TypeInfoMappingCollection.IsArrayLikeType(type, out var elementType) && TypeInfoMappingCollection.IsArrayLikeType(elementType, out _))
            throw new NotSupportedException("Writing is not supported for jagged collections, use a multidimensional array instead.");

        if (typeof(IEnumerable).IsAssignableFrom(type) && !typeof(IList).IsAssignableFrom(type) && type != typeof(string) && (dataTypeName is null || dataTypeName.Value.IsArray))
            throw new NotSupportedException("Writing is not supported for IEnumerable parameters, use an array or List instead.");

        return null;
    }
}
